using Pathlock.Cloud.Components.Certification.AI.Models;

namespace Pathlock.Cloud.Components.Certification.AI;

/// <summary>
/// High-level service for managing AI recommendations, including retrieval, feedback, and summary.
/// </summary>
public interface IAIRecommendationService
{
    /// <summary>
    /// Retrieves AI recommendations for a given certification campaign.
    /// </summary>
    Task<IReadOnlyList<AIRecommendationDto>> GetRecommendationsAsync(
        long certificationProcessId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the AI recommendation for a specific review item.
    /// </summary>
    Task<AIRecommendationDto?> GetRecommendationByStepIdAsync(
        long certificationProcessId,
        long reviewItemStepId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a summary of AI recommendation statistics for a campaign.
    /// </summary>
    Task<AIRecommendationSummary> GetRecommendationSummaryAsync(
        long certificationProcessId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records reviewer feedback on an AI recommendation.
    /// </summary>
    Task RecordFeedbackAsync(
        Guid recommendationId,
        Guid reviewerUserId,
        AIRecommendationFeedbackRequest feedback,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if recommendations exist for a campaign.
    /// </summary>
    Task<bool> HasRecommendationsAsync(
        long certificationProcessId,
        CancellationToken cancellationToken = default);
}
