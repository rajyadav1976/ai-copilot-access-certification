using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Pathlock.Cloud.Components.Certification.AI.Agent;
using Pathlock.Cloud.Components.Certification.AI.Models;
using Pathlock.Cloud.Platform.Attributes;
using Pathlock.Cloud.Platform.Database;
using Pathlock.Cloud.Platform.Session;
using Pathlock.Cloud.Shared.Entities.Components.Certifications;

namespace Pathlock.Cloud.Components.Certification.AI;

/// <summary>
/// Orchestrates the complete AI recommendation pipeline:
/// 1. Aggregates review item context
/// 2. Runs anomaly detection
/// 3. Generates LLM-based risk analysis
/// 4. Persists results to the AIRecommendations table
/// </summary>
[Component(typeof(IRecommendationEngine), ComponentType.Service)]
public class RecommendationEngine : IRecommendationEngine
{
    private readonly IDataContextAggregator _contextAggregator;
    private readonly IAnomalyDetector _anomalyDetector;
    private readonly IAgentOrchestrator _agentOrchestrator;
    private readonly ICampaignAnalysisAgent _campaignAnalysisAgent;
    private readonly IConfidenceCalibrator _confidenceCalibrator;
    private readonly IEscalationRouter _escalationRouter;
    private readonly IFeedbackEnricher _feedbackEnricher;
    private readonly ISessionContext _sessionContext;
    private readonly IBaseDbContextFactory _dbContextFactory;
    private readonly AIServiceSettings _settings;
    private readonly ILogger<RecommendationEngine> _logger;

    /// <summary>
    /// Current prompt template version for tracking reproducibility.
    /// </summary>
    private const string PromptVersion = "v1.0.0";

    /// <summary>
    /// Model version identifier.
    /// </summary>
    private const string ModelVersion = "gpt-4o-mini-2024-07-18";

    public RecommendationEngine(
        IDataContextAggregator contextAggregator,
        IAnomalyDetector anomalyDetector,
        IAgentOrchestrator agentOrchestrator,
        ICampaignAnalysisAgent campaignAnalysisAgent,
        IConfidenceCalibrator confidenceCalibrator,
        IEscalationRouter escalationRouter,
        IFeedbackEnricher feedbackEnricher,
        ISessionContext sessionContext,
        IBaseDbContextFactory dbContextFactory,
        IOptions<AIServiceSettings> settings,
        ILogger<RecommendationEngine> logger)
    {
        _contextAggregator = contextAggregator;
        _anomalyDetector = anomalyDetector;
        _agentOrchestrator = agentOrchestrator;
        _campaignAnalysisAgent = campaignAnalysisAgent;
        _confidenceCalibrator = confidenceCalibrator;
        _escalationRouter = escalationRouter;
        _feedbackEnricher = feedbackEnricher;
        _sessionContext = sessionContext;
        _dbContextFactory = dbContextFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AIRecommendationSummary> GenerateRecommendationsAsync(
        long certificationProcessId,
        string triggeredBy,
        bool forceRegenerate = false,
        long[]? specificStepIds = null,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var db = _sessionContext.DbContext;

        _logger.LogInformation(
            "Starting AI recommendation generation for campaign {CertificationProcessId} by {TriggeredBy}. " +
            "ForceRegenerate: {ForceRegenerate}, SpecificSteps: {StepCount}",
            certificationProcessId, triggeredBy, forceRegenerate,
            specificStepIds?.Length ?? -1);

        // 1. Check for existing recommendations
        if (!forceRegenerate)
        {
            var existingCount = await db.Set<AIRecommendation>()
                .CountAsync(r => r.CertificationProcessId == certificationProcessId
                              && r.Status == "Generated",
                    cancellationToken);

            if (existingCount > 0 && specificStepIds == null)
            {
                _logger.LogInformation(
                    "Campaign {CertificationProcessId} already has {Count} recommendations; skipping (use forceRegenerate to override)",
                    certificationProcessId, existingCount);

                return await BuildSummaryFromExistingAsync(db, certificationProcessId, cancellationToken);
            }
        }

        // 2. If regenerating, clean up old pending/failed entries for this campaign
        if (forceRegenerate)
        {
            await CleanupExistingRecommendationsAsync(db, certificationProcessId, specificStepIds, cancellationToken);
        }

        // 3. Aggregate context (with server-side sampling for large campaigns)
        _logger.LogInformation("Step 1/4: Aggregating context data...");
        var allContexts = await _contextAggregator.AggregateContextAsync(
            certificationProcessId, specificStepIds);

        if (allContexts.Count == 0)
        {
            _logger.LogWarning("No review items found for campaign {CertificationProcessId}", certificationProcessId);
            return new AIRecommendationSummary
            {
                CertificationProcessId = certificationProcessId,
                Status = "NoItems"
            };
        }

        // 3b. Apply in-memory sampling cap (secondary guard if DB-side sampling returned more than cap)
        var contexts = ApplySamplingCap(allContexts);

        // 4. Run anomaly detection
        _logger.LogInformation("Step 2/4: Running anomaly detection on {Count} items...", contexts.Count);
        var anomalyScores = await _anomalyDetector.DetectAnomaliesAsync(contexts, cancellationToken);

        // 4b. Run campaign-level pre-analysis (UC3) if agent mode is enabled
        CampaignInsights? campaignInsights = null;
        if (_settings.EnableCampaignAnalysis)
        {
            _logger.LogInformation("Step 2b/4: Running campaign-level analysis (UC3)...");
            var campaignToolContext = new AgentToolContext
            {
                DbContext = db,
                CertificationProcessId = certificationProcessId,
                AllItems = contexts,
                AnomalyScores = anomalyScores.ToDictionary(kv => kv.Key, kv => kv.Value)
            };
            campaignInsights = await _campaignAnalysisAgent.AnalyzeCampaignAsync(
                campaignToolContext, cancellationToken);
        }

        // 5b. Pre-fetch feedback examples sequentially (FeedbackEnricher uses shared DbContext — not thread-safe)
        _logger.LogInformation("Step 3b/5: Pre-loading feedback examples for {Count} items...", contexts.Count);
        var feedbackSections = new Dictionary<long, string>();
        foreach (var ctx in contexts)
        {
            var examples = await _feedbackEnricher.GetRelevantExamplesAsync(ctx, maxExamples: 5, cancellationToken);
            feedbackSections[ctx.StepId] = _feedbackEnricher.FormatAsPromptSection(examples);
        }

        // 6. Generate agentic recommendations in parallel
        // Each parallel task gets its own DbContext via IBaseDbContextFactory (DbContext is NOT thread-safe)
        var maxConcurrency = Math.Max(1, _settings.MaxConcurrentLlmCalls);
        _logger.LogInformation(
            "Step 4/5: Generating agentic recommendations for {Count} items (concurrency: {Concurrency})...",
            contexts.Count, maxConcurrency);

        var recommendations = new ConcurrentBag<AIRecommendation>();
        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var processedCount = 0;

        var tasks = contexts.Select((context, index) => Task.Run(async () =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var current = Interlocked.Increment(ref processedCount);
                _logger.LogInformation("Processing item {Index}/{Total} (step {StepId})...",
                    current, contexts.Count, context.StepId);

                // Create a dedicated DbContext for this parallel task with extended timeout
                using var taskDbContext = _dbContextFactory.CreateDbContext(_sessionContext);
                taskDbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));

                var feedbackSection = feedbackSections.GetValueOrDefault(context.StepId, string.Empty);

                var recommendation = await GenerateSingleRecommendationAsync(
                    context,
                    anomalyScores.GetValueOrDefault(context.StepId, 0m),
                    triggeredBy,
                    contexts,
                    anomalyScores,
                    campaignInsights,
                    taskDbContext,
                    feedbackSection,
                    cancellationToken);

                recommendations.Add(recommendation);
            }
            finally
            {
                semaphore.Release();
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks);

        // 7. Persist results
        var recommendationsList = recommendations.ToList();
        _logger.LogInformation("Step 5/5: Persisting {Count} recommendations...", recommendationsList.Count);
        await PersistRecommendationsAsync(db, recommendationsList, cancellationToken);

        totalStopwatch.Stop();
        _logger.LogInformation(
            "AI recommendation generation complete for campaign {CertificationProcessId}. " +
            "Total items: {Count} (sampled from {OriginalCount}), Total time: {ElapsedMs}ms",
            certificationProcessId, recommendationsList.Count, allContexts.Count,
            totalStopwatch.ElapsedMilliseconds);

        return BuildSummaryFromResults(certificationProcessId, recommendationsList);
    }

    /// <summary>
    /// Applies a stratified sampling cap for large campaigns.
    /// Selects a representative subset prioritizing high-risk items (zero usage, SoD violations, etc.).
    /// </summary>
    private IReadOnlyList<ReviewItemContext> ApplySamplingCap(IReadOnlyList<ReviewItemContext> contexts)
    {
        var maxItems = _settings.MaxItemsCap;
        if (maxItems <= 0 || contexts.Count <= maxItems)
        {
            _logger.LogInformation("Processing all {Count} items (within cap of {Cap})", contexts.Count, maxItems);
            return contexts;
        }

        _logger.LogInformation(
            "Campaign has {TotalCount} items, applying stratified sampling to cap at {MaxItems}",
            contexts.Count, maxItems);

        // Stratified sampling: prioritize high-risk items, then ensure diversity
        var highRisk = contexts.Where(c =>
            c.UsagePercentage == 0 ||
            c.HasSodViolation ||
            c.DaysSinceLastUsed is > 180).ToList();

        var mediumRisk = contexts.Where(c =>
            !highRisk.Contains(c) &&
            (c.UsagePercentage < 20 || c.DaysSinceLastUsed is > 90)).ToList();

        var lowRisk = contexts.Where(c =>
            !highRisk.Contains(c) && !mediumRisk.Contains(c)).ToList();

        var sampled = new List<ReviewItemContext>();
        var rng = new Random(42); // Deterministic for reproducibility

        // Take high-risk up to 60% of cap, ensuring role diversity
        var highRiskCap = (int)(maxItems * 0.6);
        sampled.AddRange(SampleWithDiversity(highRisk, highRiskCap, rng));

        // Fill with medium-risk up to 25% of cap
        var mediumRiskCap = (int)(maxItems * 0.25);
        sampled.AddRange(SampleWithDiversity(mediumRisk, mediumRiskCap, rng));

        // Fill remainder with random low-risk items
        var remaining = maxItems - sampled.Count;
        if (remaining > 0 && lowRisk.Count > 0)
        {
            sampled.AddRange(SampleWithDiversity(lowRisk, remaining, rng));
        }

        // If we still haven't filled cap, backfill from remaining items
        if (sampled.Count < maxItems)
        {
            var alreadyIncluded = new HashSet<long>(sampled.Select(s => s.StepId));
            var backfill = contexts.Where(c => !alreadyIncluded.Contains(c.StepId))
                .OrderBy(_ => rng.Next())
                .Take(maxItems - sampled.Count);
            sampled.AddRange(backfill);
        }

        _logger.LogInformation(
            "Sampled {SampledCount} items: {HighRisk} high-risk, {MediumRisk} medium-risk, {LowRisk} low-risk, {UniqueRoles} unique roles",
            sampled.Count,
            sampled.Count(c => highRisk.Contains(c)),
            sampled.Count(c => mediumRisk.Contains(c)),
            sampled.Count(c => lowRisk.Contains(c)),
            sampled.Select(c => c.RoleName).Distinct().Count());

        return sampled;
    }

    /// <summary>
    /// Samples items ensuring diversity by role name.
    /// Picks round-robin across roles before doubling up on any single role.
    /// </summary>
    private static List<ReviewItemContext> SampleWithDiversity(
        List<ReviewItemContext> pool, int maxCount, Random rng)
    {
        if (pool.Count == 0 || maxCount <= 0)
            return [];

        if (pool.Count <= maxCount)
            return pool.OrderBy(_ => rng.Next()).ToList();

        // Group by role, then pick round-robin to ensure variety
        var byRole = pool.GroupBy(c => c.RoleName ?? "Unknown")
            .Select(g => new Queue<ReviewItemContext>(g.OrderBy(_ => rng.Next())))
            .OrderBy(_ => rng.Next())
            .ToList();

        var result = new List<ReviewItemContext>(maxCount);
        while (result.Count < maxCount && byRole.Count > 0)
        {
            for (var i = byRole.Count - 1; i >= 0 && result.Count < maxCount; i--)
            {
                if (byRole[i].Count > 0)
                {
                    result.Add(byRole[i].Dequeue());
                }
                else
                {
                    byRole.RemoveAt(i);
                }
            }
        }

        return result;
    }
    private async Task<AIRecommendation> GenerateSingleRecommendationAsync(
        ReviewItemContext context,
        decimal anomalyScore,
        string triggeredBy,
        IReadOnlyList<ReviewItemContext> allContexts,
        IReadOnlyDictionary<long, decimal> allAnomalyScores,
        CampaignInsights? campaignInsights,
        DbContext taskDbContext,
        string feedbackPromptSection,
        CancellationToken cancellationToken)
    {
        var itemStopwatch = Stopwatch.StartNew();
        var recommendation = new AIRecommendation
        {
            SysId = Guid.NewGuid(),
            CertificationProcessId = context.CertificationProcessId,
            ReviewItemStepId = context.StepId,
            UserId = context.UserId,
            RoleId = context.RoleId,
            SystemId = context.SystemId,
            UsagePercentage = context.UsagePercentage,
            DaysSinceLastUsed = context.DaysSinceLastUsed,
            HasSodViolation = context.HasSodViolation,
            PeerUsagePercent = context.PeerUsagePercent,
            IsAnomaly = anomalyScore >= 0.65m,
            AnomalyScore = anomalyScore,
            Decision = "Pending",
            ConfidenceScore = 0m,
            RiskLevel = "Medium",
            ModelVersion = ModelVersion,
            PromptVersion = PromptVersion,
            Status = "Pending",
            CreatedBy = triggeredBy,
            UpdatedBy = triggeredBy
        };

        try
        {
            // ── Agentic AI Pipeline (UC1 + UC2) ──
            // Use the task-dedicated DbContext for thread safety
            var db = taskDbContext;

            // Feedback examples were pre-fetched before the parallel loop (thread-safe)

            var toolContext = new AgentToolContext
            {
                DbContext = db,
                CertificationProcessId = context.CertificationProcessId,
                CurrentItem = context,
                AllItems = allContexts,
                AnomalyScores = allAnomalyScores.ToDictionary(kv => kv.Key, kv => kv.Value),
                FeedbackPromptSection = feedbackPromptSection
            };

            var agentResult = await _agentOrchestrator.RunAsync(
                context, anomalyScore, toolContext, campaignInsights, cancellationToken);

            if (agentResult.IsSuccess)
            {
                // UC4: Confidence Calibration
                if (_settings.EnableConfidenceCalibration)
                {
                    agentResult = await _confidenceCalibrator.CalibrateAsync(
                        agentResult, context, toolContext, cancellationToken);
                }

                // UC6: Escalation Routing
                EscalationResult? escalation = null;
                if (_settings.EnableEscalation)
                {
                    escalation = _escalationRouter.Classify(agentResult, context, anomalyScore);
                }

                recommendation.Decision = agentResult.Decision;
                recommendation.ConfidenceScore = agentResult.ConfidenceScore;
                recommendation.RiskLevel = agentResult.RiskLevel;
                recommendation.RiskSummary = agentResult.RiskSummary;
                recommendation.RiskFactors = JsonSerializer.Serialize(agentResult.RiskFactors);
                recommendation.TokensUsed = agentResult.TotalTokensUsed;
                recommendation.Status = "Generated";
                recommendation.GeneratedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                // Agent failed after all retries — mark as Failed with error details
                var errorMsg = agentResult.ErrorMessage ?? "Unknown error";
                _logger.LogError(
                    "LLM failed for step {StepId}: {Error}. No fallback — marking as Failed.",
                    context.StepId, errorMsg);

                recommendation.Status = "Failed";
                recommendation.ErrorMessage = $"LLM failed: {errorMsg}";
                recommendation.TokensUsed = agentResult.TotalTokensUsed;
                recommendation.GeneratedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM failed for step {StepId}: {Error}. No fallback — marking as Failed.",
                context.StepId, ex.Message);

            recommendation.Status = "Failed";
            recommendation.ErrorMessage = $"LLM failed: {ex.Message}";
            recommendation.GeneratedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            itemStopwatch.Stop();
            recommendation.ProcessingTimeMs = (int)itemStopwatch.ElapsedMilliseconds;
        }

        return recommendation;
    }



    private async Task CleanupExistingRecommendationsAsync(
        DbContext db,
        long certificationProcessId,
        long[]? specificStepIds,
        CancellationToken cancellationToken)
    {
        var query = db.Set<AIRecommendation>()
            .Where(r => r.CertificationProcessId == certificationProcessId);

        if (specificStepIds is { Length: > 0 })
        {
            query = query.Where(r => specificStepIds.Contains(r.ReviewItemStepId));
        }

        var existing = await query.ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            db.Set<AIRecommendation>().RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Cleaned up {Count} existing recommendations for campaign {CertificationProcessId}",
                existing.Count, certificationProcessId);
        }
    }

    private async Task PersistRecommendationsAsync(
        DbContext db,
        List<AIRecommendation> recommendations,
        CancellationToken cancellationToken)
    {
        // Deduplicate by ReviewItemStepId to prevent unique index violations
        var deduplicated = recommendations
            .GroupBy(r => r.ReviewItemStepId)
            .Select(g => g.First())
            .ToList();

        if (deduplicated.Count != recommendations.Count)
        {
            _logger.LogWarning(
                "Deduplicated recommendations: {Original} → {Deduplicated} (removed {Removed} duplicates)",
                recommendations.Count, deduplicated.Count, recommendations.Count - deduplicated.Count);
        }

        // Insert in batches to handle large campaigns
        const int batchSize = 100;
        var batches = deduplicated.Chunk(batchSize);

        foreach (var batch in batches)
        {
            await db.Set<AIRecommendation>().AddRangeAsync(batch, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<AIRecommendationSummary> BuildSummaryFromExistingAsync(
        DbContext db,
        long certificationProcessId,
        CancellationToken cancellationToken)
    {
        var recs = await db.Set<AIRecommendation>()
            .Where(r => r.CertificationProcessId == certificationProcessId && r.Status == "Generated")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return BuildSummaryFromResults(certificationProcessId, recs);
    }

    private static AIRecommendationSummary BuildSummaryFromResults(
        long certificationProcessId,
        List<AIRecommendation> recommendations)
    {
        var generated = recommendations.Where(r => r.Status == "Generated").ToList();
        var failed = recommendations.Count(r => r.Status == "Failed");
        var pending = recommendations.Count(r => r.Status == "Pending");

        // Determine overall status:
        // - If no items were processed, status is "NoItems"
        // - If ALL items failed, status is "Failed"
        // - If some are pending, status is "Processing"
        // - Otherwise, "Generated"
        string status;
        if (recommendations.Count == 0)
            status = "NoItems";
        else if (generated.Count == 0 && failed > 0)
            status = "Failed";
        else if (pending > 0)
            status = "Processing";
        else
            status = "Generated";

        return new AIRecommendationSummary
        {
            CertificationProcessId = certificationProcessId,
            TotalItems = recommendations.Count,
            RecommendedApprove = generated.Count(r => r.Decision == "Approve"),
            RecommendedReject = generated.Count(r => r.Decision == "Reject"),
            NeedsReview = generated.Count(r => r.Decision == "NeedsReview"),
            HighRiskCount = generated.Count(r => r.RiskLevel is "High" or "Critical"),
            AnomalyCount = generated.Count(r => r.IsAnomaly),
            SodViolationCount = generated.Count(r => r.HasSodViolation),
            AverageConfidence = generated.Count > 0
                ? Math.Round(generated.Average(r => r.ConfidenceScore), 4)
                : 0m,
            Status = status,
            GeneratedAt = generated.MaxBy(r => r.GeneratedAt)?.GeneratedAt
        };
    }
}
