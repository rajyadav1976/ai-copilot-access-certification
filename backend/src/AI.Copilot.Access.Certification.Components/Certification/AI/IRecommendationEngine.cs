using AI.Copilot.Access.Certification.Components.Certification.AI.Models;

namespace AI.Copilot.Access.Certification.Components.Certification.AI;

/// <summary>
/// Orchestrates the entire recommendation pipeline: context aggregation, anomaly detection,
/// LLM analysis, and result persistence.
/// </summary>
public interface IRecommendationEngine
{
    /// <summary>
    /// Generates AI recommendations for all review items in a certification campaign.
    /// </summary>
    /// <param name="certificationProcessId">The certification campaign ID.</param>
    /// <param name="triggeredBy">User who triggered the generation.</param>
    /// <param name="forceRegenerate">If true, regenerates even if recommendations exist.</param>
    /// <param name="specificStepIds">Optional specific step IDs to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of generated recommendations.</returns>
    Task<AIRecommendationSummary> GenerateRecommendationsAsync(
        long certificationProcessId,
        string triggeredBy,
        bool forceRegenerate = false,
        long[]? specificStepIds = null,
        CancellationToken cancellationToken = default);
}
