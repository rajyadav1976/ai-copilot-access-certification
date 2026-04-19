using AI.Copilot.Access.Certification.Components.Certification.AI.Models;

namespace AI.Copilot.Access.Certification.Components.Certification.AI;

/// <summary>
/// Aggregates all data context needed for a review item from various data sources
/// (review items view, SoD results, peer analysis, historical decisions).
/// </summary>
public interface IDataContextAggregator
{
    /// <summary>
    /// Gathers all contextual data for review items in a certification campaign.
    /// </summary>
    /// <param name="certificationProcessId">The certification campaign ID.</param>
    /// <param name="specificStepIds">Optional step IDs to filter. If null, all items are returned.</param>
    /// <returns>Collection of fully enriched review item contexts.</returns>
    Task<IReadOnlyList<ReviewItemContext>> AggregateContextAsync(
        long certificationProcessId,
        long[]? specificStepIds = null);
}
