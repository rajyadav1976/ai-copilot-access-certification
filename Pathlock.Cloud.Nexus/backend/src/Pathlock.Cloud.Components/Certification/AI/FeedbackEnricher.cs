using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Pathlock.Cloud.Components.Certification.AI.Models;
using Pathlock.Cloud.Platform.Attributes;
using Pathlock.Cloud.Platform.Session;
using Pathlock.Cloud.Shared.Entities.Components.Certifications;

namespace Pathlock.Cloud.Components.Certification.AI;

/// <summary>
/// Interface for the Feedback Enricher (Learning Loop Layer 2 — Few-Shot Injection).
/// Retrieves relevant historical feedback examples and formats them for LLM prompt injection.
/// </summary>
public interface IFeedbackEnricher
{
    /// <summary>
    /// Retrieves maximally informative feedback examples similar to the current review item.
    /// Prioritizes cases where reviewers DISAGREED with the AI (correction signals).
    /// </summary>
    Task<List<FeedbackExample>> GetRelevantExamplesAsync(
        ReviewItemContext currentItem,
        int maxExamples = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Formats feedback examples into a prompt section for LLM injection.
    /// </summary>
    string FormatAsPromptSection(List<FeedbackExample> examples);
}

/// <summary>
/// Feedback Enricher — Learning Loop Layer 2.
/// 
/// Implements few-shot prompt injection by:
/// 1. Finding similar past review items that have feedback (joined AIRecommendation + AIRecommendationFeedback)
/// 2. Prioritizing DISAGREEMENTS (where the human corrected the AI) as the most informative examples
/// 3. Formatting them as structured examples in the LLM prompt
///
/// This enables the model to learn from past mistakes at inference time without any fine-tuning.
/// </summary>
[Component(typeof(IFeedbackEnricher), ComponentType.Service)]
public class FeedbackEnricher : IFeedbackEnricher
{
    private readonly ISessionContext _sessionContext;
    private readonly ILogger<FeedbackEnricher> _logger;

    /// <summary>
    /// Minimum number of feedback entries required before enrichment is applied.
    /// Below this threshold, few-shot examples may be misleading.
    /// </summary>
    private const int MinFeedbackThreshold = 3;

    public FeedbackEnricher(
        ISessionContext sessionContext,
        ILogger<FeedbackEnricher> logger)
    {
        _sessionContext = sessionContext;
        _logger = logger;
    }

    public async Task<List<FeedbackExample>> GetRelevantExamplesAsync(
        ReviewItemContext currentItem,
        int maxExamples = 5,
        CancellationToken cancellationToken = default)
    {
        var db = _sessionContext.DbContext;

        // Step 1: Find all feedback-enriched recommendations from PAST campaigns
        // Load feedback and recommendation data separately to avoid cross-table LINQ issues
        var allFeedbacks = await db.Set<AIRecommendationFeedback>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // TODO: For production scalability, replace ToListAsync() with server-side filtering
        // once migrated from SQLite to SQL Server (avoids LINQ translation issues with SQLite).
        var recIdSet = allFeedbacks.Select(f => f.AIRecommendationId).Distinct().ToHashSet();
        var allRecommendations = await db.Set<AIRecommendation>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var recommendations = allRecommendations
            .Where(r => r.SysId.HasValue
                      && recIdSet.Contains(r.SysId.Value)
                      && r.CertificationProcessId != currentItem.CertificationProcessId
                      && r.Status == "Generated")
            .ToList();

        var recLookup = recommendations.ToDictionary(r => r.SysId!.Value);

        var feedbackData = allFeedbacks
            .Where(f => recLookup.ContainsKey(f.AIRecommendationId))
            .Select(f =>
            {
                var r = recLookup[f.AIRecommendationId];
                return new
                {
                    r.ReviewItemStepId,
                    r.CertificationProcessId,
                    r.Decision,
                    r.ConfidenceScore,
                    r.RiskLevel,
                    r.UsagePercentage,
                    r.HasSodViolation,
                    f.ActualDecision,
                    f.AgreedWithAI,
                    f.OverrideReason,
                    f.QualityRating,
                    f.FeedbackTimestamp
                };
            })
            .ToList();

        if (feedbackData.Count < MinFeedbackThreshold)
        {
            _logger.LogDebug(
                "Insufficient feedback data ({Count} < {Threshold}) for enrichment; skipping",
                feedbackData.Count, MinFeedbackThreshold);
            return [];
        }

        // Step 2: Look up role names for matched items
        var stepIdSet = feedbackData.Select(f => f.ReviewItemStepId).Distinct().ToHashSet();
        var certIdSet = feedbackData.Select(f => f.CertificationProcessId).Distinct().ToHashSet();

        var allReviewItems = await db.Set<ReviewItems>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var roleNames = allReviewItems
            .Where(ri => certIdSet.Contains(ri.CertificationProcessId) && stepIdSet.Contains(ri.Id))
            .Select(ri => new { ri.Id, ri.CertificationProcessId, ri.RoleName })
            .ToList();

        var roleNameLookup = roleNames.ToDictionary(
            r => (r.CertificationProcessId, r.Id),
            r => r.RoleName ?? "Unknown");

        // Step 3: Compute relevance score for each feedback item
        var scored = feedbackData.Select(f =>
        {
            roleNameLookup.TryGetValue((f.CertificationProcessId, f.ReviewItemStepId), out var roleName);
            roleName ??= "Unknown";

            // Compute similarity to current item
            var score = ComputeRelevanceScore(
                currentItem,
                roleName,
                (int)(f.UsagePercentage ?? 0),
                f.HasSodViolation,
                f.RiskLevel,
                f.AgreedWithAI,
                f.QualityRating);

            return new
            {
                Example = new FeedbackExample
                {
                    RoleName = roleName,
                    UsagePercentage = (int)(f.UsagePercentage ?? 0),
                    RiskLevel = f.RiskLevel,
                    HasSodViolation = f.HasSodViolation,
                    AiDecision = f.Decision,
                    AiConfidence = f.ConfidenceScore,
                    ReviewerDecision = f.ActualDecision,
                    AgreedWithAI = f.AgreedWithAI,
                    OverrideReason = f.OverrideReason,
                    QualityRating = f.QualityRating
                },
                Score = score
            };
        })
        .OrderByDescending(x => x.Score)
        .Take(maxExamples)
        .Select(x => x.Example)
        .ToList();

        _logger.LogDebug(
            "Feedback enricher found {Count} examples for step {StepId} (from {TotalFeedback} total)",
            scored.Count, currentItem.StepId, feedbackData.Count);

        return scored;
    }

    public string FormatAsPromptSection(List<FeedbackExample> examples)
    {
        if (examples.Count == 0)
            return string.Empty;

        var lines = new List<string>
        {
            "",
            "## Historical Reviewer Feedback (Learn from Past Decisions)",
            "",
            "The following are similar items where reviewers provided feedback on AI recommendations.",
            "Use these to calibrate your decision — pay special attention to cases where reviewers",
            "DISAGREED with the AI and their reasons why.",
            ""
        };

        for (int i = 0; i < examples.Count; i++)
        {
            var ex = examples[i];
            lines.Add($"Example {i + 1}: Role \"{ex.RoleName}\" (usage: {ex.UsagePercentage}%, " +
                       $"risk: {ex.RiskLevel}{(ex.HasSodViolation ? ", SoD violation" : "")})");
            lines.Add($"  AI recommended: {ex.AiDecision} (confidence: {ex.AiConfidence:F2})");
            lines.Add($"  Reviewer decision: {ex.ReviewerDecision}");

            if (!ex.AgreedWithAI && !string.IsNullOrEmpty(ex.OverrideReason))
            {
                lines.Add($"  Override reason: \"{ex.OverrideReason}\"");
            }

            if (ex.QualityRating.HasValue)
            {
                lines.Add($"  Quality rating: {ex.QualityRating}/5");
            }

            lines.Add("");
        }

        lines.Add("Based on these patterns, apply similar reasoning to the current item.");
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Computes a relevance score (0.0–1.0) indicating how informative a feedback example is
    /// for the current review item.
    /// 
    /// Prioritizes:
    /// 1. Disagreements (where the human corrected the AI) — most informative signal
    /// 2. Same role name — directly applicable patterns
    /// 3. Similar usage range — comparable scenarios
    /// 4. Same risk level / SoD status — matching risk profile
    /// 5. Low quality ratings — indicates AI was particularly unhelpful
    /// 6. Recency (implicit via ordering)
    /// </summary>
    private static decimal ComputeRelevanceScore(
        ReviewItemContext currentItem,
        string roleName,
        int usage,
        bool hasSod,
        string riskLevel,
        bool agreedWithAI,
        int? qualityRating)
    {
        var score = 0m;

        // Disagreements get a massive boost — they are the correction signals we learn from
        if (!agreedWithAI)
            score += 0.40m;

        // Same role name is directly applicable
        if (string.Equals(roleName, currentItem.RoleName, StringComparison.OrdinalIgnoreCase))
            score += 0.25m;

        // Similar usage range (±20%)
        var usageDiff = Math.Abs(usage - currentItem.UsagePercentage);
        if (usageDiff <= 20)
            score += 0.15m * (1m - usageDiff / 20m);

        // Matching SoD status
        if (hasSod == currentItem.HasSodViolation)
            score += 0.05m;

        // Matching risk level
        if (string.Equals(riskLevel, EstimateRiskLevel(currentItem), StringComparison.OrdinalIgnoreCase))
            score += 0.05m;

        // Low quality ratings indicate clear AI failures — very informative
        if (qualityRating.HasValue && qualityRating.Value <= 2)
            score += 0.10m;

        return Math.Clamp(score, 0m, 1m);
    }

    /// <summary>
    /// Rough risk level estimate for the current item (for matching purposes only).
    /// </summary>
    private static string EstimateRiskLevel(ReviewItemContext item)
    {
        if (item.HasSodViolation && item.UsagePercentage == 0)
            return "Critical";
        if (item.HasSodViolation || item.UsagePercentage == 0)
            return "High";
        if (item.UsagePercentage < 20 || item.DaysSinceLastUsed > 90)
            return "Medium";
        return "Low";
    }
}
