using Microsoft.Extensions.Logging;

using AI.Copilot.Access.Certification.Components.Certification.AI;
using AI.Copilot.Access.Certification.Shared.Legacy.Messaging;
using AI.Copilot.Access.Certification.Shared.Legacy.Messaging.Messages.WorkerProcess;
using AI.Copilot.Access.Certification.Worker.Attributes;

namespace AI.Copilot.Access.Certification.Worker.App.Certification;

/// <summary>
/// Worker message handler that processes AI recommendation generation requests.
/// Automatically discovered and registered via the [WorkerMessageHandler] attribute.
/// Wrapped by WorkerMessagePreprocessor for common preprocessing,
/// and automatically wrapped with logging and telemetry interceptors.
/// </summary>
[WorkerMessageHandler]
public class AIRecommendationGenerationHandler : IMessageHandler<AIRecommendationMessage>
{
    private readonly IRecommendationEngine _recommendationEngine;
    private readonly ILogger<AIRecommendationGenerationHandler> _logger;

    public AIRecommendationGenerationHandler(
        IRecommendationEngine recommendationEngine,
        ILogger<AIRecommendationGenerationHandler> logger)
    {
        _recommendationEngine = recommendationEngine;
        _logger = logger;
    }

    public async Task<BaseMessage> Handle(AIRecommendationMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Received AI recommendation generation request for campaign {CertificationProcessId}. " +
            "Triggered by: {TriggeredBy}, ForceRegenerate: {ForceRegenerate}",
            message.CertificationProcessId,
            message.TriggeredBy,
            message.ForceRegenerate);

        try
        {
            var summary = await _recommendationEngine.GenerateRecommendationsAsync(
                message.CertificationProcessId,
                message.TriggeredBy,
                message.ForceRegenerate,
                message.SpecificStepIds,
                cancellationToken);

            _logger.LogInformation(
                "AI recommendation generation completed for campaign {CertificationProcessId}. " +
                "Total: {Total}, Approve: {Approve}, Reject: {Reject}, NeedsReview: {NeedsReview}, " +
                "HighRisk: {HighRisk}, Avg Confidence: {AvgConfidence:F4}",
                message.CertificationProcessId,
                summary.TotalItems,
                summary.RecommendedApprove,
                summary.RecommendedReject,
                summary.NeedsReview,
                summary.HighRiskCount,
                summary.AverageConfidence);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "AI recommendation generation was cancelled for campaign {CertificationProcessId}",
                message.CertificationProcessId);
            throw; // Rethrow to allow Worker preprocessor to handle retry/cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error generating AI recommendations for campaign {CertificationProcessId}",
                message.CertificationProcessId);
            throw; // Rethrow to allow Worker preprocessor to update job status to Failed
        }

        return null!;
    }
}
