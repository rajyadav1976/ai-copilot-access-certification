using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using AI.Copilot.Access.Certification.Components.Certification.AI.Models;
using AI.Copilot.Access.Certification.Platform.Attributes;
using AI.Copilot.Access.Certification.Platform.Session;
using AI.Copilot.Access.Certification.Shared.Entities.Components.Certifications;

namespace AI.Copilot.Access.Certification.Components.Certification.AI;

/// <summary>
/// Interface for the Feedback Analytics Service (Learning Loop Layer 1).
/// Aggregates raw reviewer feedback into actionable metrics that drive the learning pipeline.
/// </summary>
public interface IFeedbackAnalyticsService
{
    /// <summary>
    /// Computes aggregate feedback metrics, optionally filtered by role category and risk level.
    /// </summary>
    Task<FeedbackMetrics> GetMetricsAsync(
        string? riskLevel = null,
        int windowDays = 90,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares performance across model/prompt versions.
    /// </summary>
    Task<List<VersionPerformance>> GetVersionPerformanceAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects concept drift by comparing recent agreement rates to historical baseline.
    /// </summary>
    Task<DriftReport> DetectConceptDriftAsync(
        int recentWindowDays = 30,
        int baselineWindowDays = 90,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Feedback Analytics Service — Learning Loop Layer 1.
/// Provides aggregate metrics over collected reviewer feedback to measure AI accuracy,
/// detect concept drift, and compare prompt/model versions.
/// </summary>
[Component(typeof(IFeedbackAnalyticsService), ComponentType.Service)]
public class FeedbackAnalyticsService : IFeedbackAnalyticsService
{
    private readonly ISessionContext _sessionContext;
    private readonly ILogger<FeedbackAnalyticsService> _logger;

    public FeedbackAnalyticsService(
        ISessionContext sessionContext,
        ILogger<FeedbackAnalyticsService> logger)
    {
        _sessionContext = sessionContext;
        _logger = logger;
    }

    public async Task<FeedbackMetrics> GetMetricsAsync(
        string? riskLevel = null,
        int windowDays = 90,
        CancellationToken cancellationToken = default)
    {
        var db = _sessionContext.DbContext;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-windowDays);

        // Load all data and filter in memory to avoid SQLite translation issues
        var allFeedbacks = await db.Set<AIRecommendationFeedback>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var feedbacks = allFeedbacks.Where(f => f.FeedbackTimestamp >= cutoff).ToList();

        if (feedbacks.Count == 0)
        {
            return new FeedbackMetrics { TotalFeedbacks = 0, WindowDays = windowDays };
        }

        var allRecommendations = await db.Set<AIRecommendation>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var recIds = feedbacks.Select(f => f.AIRecommendationId).Distinct().ToHashSet();
        var recLookup = allRecommendations
            .Where(r => r.SysId.HasValue && recIds.Contains(r.SysId.Value))
            .ToDictionary(r => r.SysId!.Value);

        var data = feedbacks
            .Where(f => recLookup.ContainsKey(f.AIRecommendationId))
            .Select(f => new { Feedback = f, Recommendation = recLookup[f.AIRecommendationId] })
            .ToList();

        if (!string.IsNullOrEmpty(riskLevel))
        {
            data = data.Where(x => x.Recommendation.RiskLevel == riskLevel).ToList();
        }

        if (data.Count == 0)
        {
            return new FeedbackMetrics { TotalFeedbacks = 0, WindowDays = windowDays };
        }

        var agreed = data.Count(x => x.Feedback.AgreedWithAI);
        var overrideReasons = data
            .Where(x => !string.IsNullOrEmpty(x.Feedback.OverrideReason))
            .GroupBy(x => x.Feedback.OverrideReason!)
            .ToDictionary(g => g.Key, g => g.Count());

        var accuracyByDecision = data
            .GroupBy(x => x.Recommendation.Decision)
            .ToDictionary(
                g => g.Key,
                g => g.Count() > 0 ? (decimal)g.Count(x => x.Feedback.AgreedWithAI) / g.Count() : 0m);

        var ratings = data.Where(x => x.Feedback.QualityRating.HasValue)
            .Select(x => x.Feedback.QualityRating!.Value)
            .ToList();

        return new FeedbackMetrics
        {
            TotalFeedbacks = data.Count,
            AgreementRate = (decimal)agreed / data.Count,
            AverageQualityRating = ratings.Count > 0 ? (decimal)ratings.Average() : 0m,
            OverrideReasonCounts = overrideReasons,
            AccuracyByDecision = accuracyByDecision,
            AccuracyByRiskLevel = data
                .GroupBy(x => x.Recommendation.RiskLevel)
                .ToDictionary(
                    g => g.Key,
                    g => g.Count() > 0 ? (decimal)g.Count(x => x.Feedback.AgreedWithAI) / g.Count() : 0m),
            WindowDays = windowDays
        };
    }

    public async Task<List<VersionPerformance>> GetVersionPerformanceAsync(
        CancellationToken cancellationToken = default)
    {
        var db = _sessionContext.DbContext;

        // Load all data and filter in memory to avoid SQLite translation issues
        var allFeedbacks = await db.Set<AIRecommendationFeedback>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var allRecommendations = await db.Set<AIRecommendation>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var recIds = allFeedbacks.Select(f => f.AIRecommendationId).Distinct().ToHashSet();
        var recLookup = allRecommendations
            .Where(r => r.SysId.HasValue && recIds.Contains(r.SysId.Value))
            .ToDictionary(r => r.SysId!.Value);

        var data = allFeedbacks
            .Where(f => recLookup.ContainsKey(f.AIRecommendationId))
            .Select(f => new
            {
                recLookup[f.AIRecommendationId].PromptVersion,
                recLookup[f.AIRecommendationId].ModelVersion,
                f.AgreedWithAI,
                f.QualityRating
            })
            .ToList();

        return data
            .GroupBy(x => new { x.PromptVersion, x.ModelVersion })
            .Select(g => new VersionPerformance
            {
                PromptVersion = g.Key.PromptVersion ?? "unknown",
                ModelVersion = g.Key.ModelVersion ?? "unknown",
                FeedbackCount = g.Count(),
                AgreementRate = (decimal)g.Count(x => x.AgreedWithAI) / g.Count(),
                AverageQualityRating = g.Where(x => x.QualityRating.HasValue)
                    .Select(x => x.QualityRating!.Value)
                    .DefaultIfEmpty(0)
                    .Average()
            })
            .OrderByDescending(v => v.AgreementRate)
            .ToList();
    }

    public async Task<DriftReport> DetectConceptDriftAsync(
        int recentWindowDays = 30,
        int baselineWindowDays = 90,
        CancellationToken cancellationToken = default)
    {
        var db = _sessionContext.DbContext;
        var now = DateTimeOffset.UtcNow;
        var recentCutoff = now.AddDays(-recentWindowDays);
        var baselineCutoff = now.AddDays(-baselineWindowDays);

        var rawFeedback = await db.Set<AIRecommendationFeedback>()
                                .AsNoTracking()
                                .ToListAsync(cancellationToken);

        var allFeedback = rawFeedback
            .Where(f => f.FeedbackTimestamp >= baselineCutoff)
            .Select(f => new { f.AgreedWithAI, f.FeedbackTimestamp })
            .ToList();

        var baseline = allFeedback.Where(f => f.FeedbackTimestamp < recentCutoff).ToList();
        var recent = allFeedback.Where(f => f.FeedbackTimestamp >= recentCutoff).ToList();

        var baselineRate = baseline.Count > 0
            ? (decimal)baseline.Count(f => f.AgreedWithAI) / baseline.Count
            : 0m;
        var recentRate = recent.Count > 0
            ? (decimal)recent.Count(f => f.AgreedWithAI) / recent.Count
            : 0m;

        var drift = recentRate - baselineRate;
        var isDrifting = Math.Abs(drift) > 0.1m && baseline.Count >= 10 && recent.Count >= 5;

        return new DriftReport
        {
            BaselineAgreementRate = baselineRate,
            RecentAgreementRate = recentRate,
            DriftMagnitude = drift,
            IsDrifting = isDrifting,
            BaselineSampleCount = baseline.Count,
            RecentSampleCount = recent.Count,
            RecentWindowDays = recentWindowDays,
            BaselineWindowDays = baselineWindowDays,
            Message = isDrifting
                ? $"Concept drift detected: agreement rate changed from {baselineRate:P0} to {recentRate:P0} " +
                  $"({(drift > 0 ? "improved" : "degraded")} by {Math.Abs(drift):P0}). " +
                  "Consider updating prompt templates."
                : "No significant drift detected."
        };
    }
}
