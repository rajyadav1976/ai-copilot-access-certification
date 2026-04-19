using System.Collections.Concurrent;

namespace AI.Copilot.Access.Certification.Components.Certification.AI;

/// <summary>
/// Thread-safe in-memory tracker for AI recommendation generation status.
/// Used to report "Processing" status to clients while background generation is in progress,
/// since the database records are cleaned before generation and only persisted at the end.
/// Registered as a singleton service.
/// </summary>
public interface IAIGenerationTracker
{
    /// <summary>
    /// Marks a campaign as currently being processed.
    /// </summary>
    void MarkProcessing(long certificationProcessId);

    /// <summary>
    /// Marks a campaign as done processing (success or failure).
    /// </summary>
    void MarkComplete(long certificationProcessId);

    /// <summary>
    /// Checks if a campaign is currently being processed.
    /// </summary>
    bool IsProcessing(long certificationProcessId);
}

/// <summary>
/// Singleton implementation of the generation tracker.
/// </summary>
public class AIGenerationTracker : IAIGenerationTracker
{
    private readonly ConcurrentDictionary<long, DateTimeOffset> _processing = new();

    public void MarkProcessing(long certificationProcessId)
    {
        _processing[certificationProcessId] = DateTimeOffset.UtcNow;
    }

    public void MarkComplete(long certificationProcessId)
    {
        _processing.TryRemove(certificationProcessId, out _);
    }

    public bool IsProcessing(long certificationProcessId)
    {
        if (!_processing.TryGetValue(certificationProcessId, out var startedAt))
            return false;

        // Auto-expire after 30 minutes to prevent stuck states
        if (DateTimeOffset.UtcNow - startedAt > TimeSpan.FromMinutes(30))
        {
            _processing.TryRemove(certificationProcessId, out _);
            return false;
        }

        return true;
    }
}
