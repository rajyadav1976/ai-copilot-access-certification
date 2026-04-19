using Microsoft.Extensions.Logging;

using AI.Copilot.Access.Certification.Components.Certification.AI.Models;
using AI.Copilot.Access.Certification.Platform.Attributes;

namespace AI.Copilot.Access.Certification.Components.Certification.AI;

/// <summary>
/// Embedding-based anomaly detector that identifies unusual role assignments
/// by comparing each user's role profile against their peer group.
/// Uses cosine similarity on embeddings from the LLM gateway.
/// </summary>
[Component(typeof(IAnomalyDetector), ComponentType.Service)]
public class EmbeddingAnomalyDetector : IAnomalyDetector
{
    private readonly ILlmGateway _llmGateway;
    private readonly ILogger<EmbeddingAnomalyDetector> _logger;

    /// <summary>
    /// Threshold below which a role assignment is considered anomalous.
    /// Values below this cosine similarity to the peer centroid are flagged.
    /// </summary>
    private const decimal AnomalyThreshold = 0.65m;

    public EmbeddingAnomalyDetector(ILlmGateway llmGateway, ILogger<EmbeddingAnomalyDetector> logger)
    {
        _llmGateway = llmGateway;
        _logger = logger;
    }

    public async Task<Dictionary<long, decimal>> DetectAnomaliesAsync(
        IReadOnlyList<ReviewItemContext> items,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<long, decimal>();

        if (items.Count == 0) return result;

        try
        {
            // Group items by department+job to form peer groups
            var peerGroups = items
                .Where(i => !string.IsNullOrEmpty(i.EmployeeDepartment) && !string.IsNullOrEmpty(i.EmployeeJob))
                .GroupBy(i => $"{i.EmployeeDepartment}|{i.EmployeeJob}")
                .Where(g => g.Count() >= 3) // Need at least 3 peers for meaningful comparison
                .ToList();

            _logger.LogInformation("Analyzing {GroupCount} peer groups for anomalies", peerGroups.Count);

            foreach (var peerGroup in peerGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await AnalyzePeerGroupAsync(peerGroup.ToList(), result, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Error analyzing peer group {Group}, skipping", peerGroup.Key);
                }
            }

            // Items not in any peer group get a neutral anomaly score (no embedding data available)
            foreach (var item in items.Where(i => !result.ContainsKey(i.StepId)))
            {
                result[item.StepId] = 0m;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Anomaly detection was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during anomaly detection; assigning neutral scores");
            // Assign neutral anomaly score for items without results
            foreach (var item in items.Where(i => !result.ContainsKey(i.StepId)))
            {
                result[item.StepId] = 0m;
            }
        }

        return result;
    }

    private async Task AnalyzePeerGroupAsync(
        List<ReviewItemContext> peerItems,
        Dictionary<long, decimal> result,
        CancellationToken cancellationToken)
    {
        // Build text representations and request embeddings in a single batch call
        var texts = peerItems.Select(BuildRoleProfileText).ToList();
        var batchEmbeddings = await _llmGateway.GetEmbeddingsBatchAsync(texts, cancellationToken);

        if (batchEmbeddings.Count < 3)
        {
            // Not enough embeddings for meaningful comparison — assign neutral score
            foreach (var item in peerItems)
            {
                result[item.StepId] = 0m;
            }
            return;
        }

        var embeddings = new Dictionary<long, float[]>();
        for (int i = 0; i < Math.Min(peerItems.Count, batchEmbeddings.Count); i++)
        {
            if (batchEmbeddings[i].Length > 0)
            {
                embeddings[peerItems[i].StepId] = batchEmbeddings[i];
            }
        }

        if (embeddings.Count < 3)
        {
            // Not enough valid embeddings — assign neutral score
            foreach (var item in peerItems)
            {
                result[item.StepId] = 0m;
            }
            return;
        }

        // Compute centroid of all embeddings in the peer group
        var centroid = ComputeCentroid(embeddings.Values.ToList());

        // Compute each item's similarity to the centroid
        foreach (var (stepId, embedding) in embeddings)
        {
            var similarity = CosineSimilarity(embedding, centroid);
            // Convert similarity (0..1 range) to anomaly score (0..1 where 1 is most anomalous)
            var anomalyScore = Math.Clamp(1.0m - (decimal)similarity, 0m, 1m);
            result[stepId] = Math.Round(anomalyScore, 4);
        }
    }

    private static string BuildRoleProfileText(ReviewItemContext item)
    {
        var parts = new List<string>
        {
            $"Department: {item.EmployeeDepartment ?? "Unknown"}",
            $"Job: {item.EmployeeJob ?? "Unknown"}",
            $"Role: {item.RoleName ?? "Unknown"}",
            $"Description: {item.RoleDescription ?? "No description"}",
            $"Usage: {item.UsagePercentage}%",
            $"Activities Used: {item.UsedActivities}",
            $"Days Since Last Use: {item.DaysSinceLastUsed?.ToString() ?? "Never used"}"
        };

        return string.Join("; ", parts);
    }

    private static float[] ComputeCentroid(List<float[]> embeddings)
    {
        if (embeddings.Count == 0) return [];

        var dimensions = embeddings[0].Length;
        var centroid = new float[dimensions];

        foreach (var embedding in embeddings)
        {
            for (int i = 0; i < dimensions && i < embedding.Length; i++)
            {
                centroid[i] += embedding[i];
            }
        }

        var count = (float)embeddings.Count;
        for (int i = 0; i < dimensions; i++)
        {
            centroid[i] /= count;
        }

        return centroid;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0.0;

        double dotProduct = 0.0;
        double magnitudeA = 0.0;
        double magnitudeB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB);
        return denominator == 0 ? 0.0 : dotProduct / denominator;
    }

}
