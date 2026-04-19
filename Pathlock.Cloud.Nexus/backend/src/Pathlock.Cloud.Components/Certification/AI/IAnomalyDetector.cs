using Pathlock.Cloud.Components.Certification.AI.Models;

namespace Pathlock.Cloud.Components.Certification.AI;

/// <summary>
/// Detects anomalous role assignments by comparing against peer group embeddings.
/// </summary>
public interface IAnomalyDetector
{
    /// <summary>
    /// Analyzes a collection of review item contexts to detect anomalous role assignments
    /// within their peer groups (same department + same job title).
    /// </summary>
    /// <param name="items">The review item contexts to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Dictionary mapping step IDs to their anomaly scores (0.0 = normal, 1.0 = highly anomalous).
    /// </returns>
    Task<Dictionary<long, decimal>> DetectAnomaliesAsync(
        IReadOnlyList<ReviewItemContext> items,
        CancellationToken cancellationToken = default);
}
