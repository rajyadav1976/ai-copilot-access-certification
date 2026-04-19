using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using AI.Copilot.Access.Certification.Components.Certification.AI.Agent.Tools;
using AI.Copilot.Access.Certification.Components.Certification.AI.Models;
using AI.Copilot.Access.Certification.Platform.Attributes;

namespace AI.Copilot.Access.Certification.Components.Certification.AI.Agent;

/// <summary>
/// Interface for confidence calibration (UC4: Adaptive Confidence Calibration).
/// </summary>
public interface IConfidenceCalibrator
{
    /// <summary>
    /// Post-processes an agent recommendation by comparing it against historical
    /// ground truth from similar past decisions and adjusting the confidence score.
    /// </summary>
    Task<AgentRecommendation> CalibrateAsync(
        AgentRecommendation recommendation,
        ReviewItemContext item,
        AgentToolContext toolContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adaptive confidence calibration that adjusts agent confidence post-hoc (UC4).
///
/// The calibrator:
/// 1. Looks up similar past decisions from previous campaigns.
/// 2. Compares the agent's recommendation with the historical consensus.
/// 3. Adjusts confidence upward when the recommendation aligns with history,
///    and downward when it deviates, adding an explanatory note.
/// 
/// This prevents overconfident recommendations that disagree with established patterns,
/// while boosting confidence when historical data confirms the agent's analysis.
/// </summary>
[Component(typeof(IConfidenceCalibrator), ComponentType.Service)]
public class ConfidenceCalibrator : IConfidenceCalibrator
{
    private readonly ILogger<ConfidenceCalibrator> _logger;
    private readonly AIServiceSettings _settings;

    /// <summary>
    /// Maximum amount by which confidence can be adjusted up or down.
    /// </summary>
    private const decimal MaxAdjustment = 0.15m;

    /// <summary>
    /// Minimum number of similar historical decisions needed for calibration.
    /// </summary>
    private const int MinHistoricalCount = 3;

    public ConfidenceCalibrator(
        IOptions<AIServiceSettings> settings,
        ILogger<ConfidenceCalibrator> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AgentRecommendation> CalibrateAsync(
        AgentRecommendation recommendation,
        ReviewItemContext item,
        AgentToolContext toolContext,
        CancellationToken cancellationToken = default)
    {
        if (!recommendation.IsSuccess)
            return recommendation;

        try
        {
            // Step 1: Get similar past decisions
            var similarDecisionsTool = new GetSimilarPastDecisionsTool();
            var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                roleName = item.RoleName ?? "",
                usagePercentage = item.UsagePercentage,
                hasSodViolation = item.HasSodViolation
            })).RootElement;

            var similarResult = await similarDecisionsTool.ExecuteAsync(args, toolContext, cancellationToken);

            // Step 2: Parse the historical approval rate from the tool result
            var historicalRate = ParseHistoricalApprovalRate(similarResult);

            if (historicalRate == null)
            {
                _logger.LogDebug(
                    "No sufficient historical data for calibration of step {StepId}", item.StepId);
                return recommendation;
            }

            // Step 3: Calculate alignment between agent recommendation and historical pattern
            var agentApproves = recommendation.Decision == "Approve";
            var historyFavorsApproval = historicalRate.Value > 0.5m;
            var alignmentScore = agentApproves == historyFavorsApproval
                ? historicalRate.Value  // Agreement — higher rate = higher confidence boost 
                : 1m - historicalRate.Value; // Disagreement — lower alignment

            // Step 4: Compute confidence adjustment
            var originalConfidence = recommendation.ConfidenceScore;
            decimal adjustment;

            if (agentApproves == historyFavorsApproval)
            {
                // Agent agrees with historical consensus — boost confidence proportionally
                adjustment = MaxAdjustment * alignmentScore;
                recommendation.ConfidenceScore = Math.Clamp(
                    recommendation.ConfidenceScore + adjustment, 0m, 1m);

                recommendation.RiskFactors.Add(
                    $"Confidence boosted: historical approval rate {historicalRate.Value:P0} aligns with recommendation");
            }
            else
            {
                // Agent disagrees with historical consensus — reduce confidence
                adjustment = -MaxAdjustment * (1m - alignmentScore);
                recommendation.ConfidenceScore = Math.Clamp(
                    recommendation.ConfidenceScore + adjustment, 0m, 1m);

                recommendation.RiskFactors.Add(
                    $"Confidence reduced: historical approval rate {historicalRate.Value:P0} diverges from recommendation");

                // If confidence drops below threshold, suggest human review
                if (recommendation.ConfidenceScore < 0.5m && recommendation.Decision != "NeedsReview")
                {
                    recommendation.RiskSummary += " Historical patterns suggest this item may need additional human review.";
                }
            }

            recommendation.ReasoningTrace.Add(new AgentReasoningStep
            {
                StepNumber = recommendation.ReasoningTrace.Count + 1,
                Action = "confidence_calibration",
                Reasoning = $"Original confidence: {originalConfidence:F2} → Calibrated: {recommendation.ConfidenceScore:F2} " +
                           $"(historical approval rate: {historicalRate.Value:P0}, alignment: {(agentApproves == historyFavorsApproval ? "agrees" : "disagrees")})"
            });

            _logger.LogDebug(
                "Confidence calibrated for step {StepId}: {Original:F2} → {Calibrated:F2} (history: {Rate:P0})",
                item.StepId, originalConfidence, recommendation.ConfidenceScore, historicalRate.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Confidence calibration failed for step {StepId}; keeping original confidence",
                item.StepId);
        }

        return recommendation;
    }

    /// <summary>
    /// Extracts the historical approval rate from the GetSimilarPastDecisions tool output.
    /// Returns null if insufficient data.
    /// </summary>
    private decimal? ParseHistoricalApprovalRate(string toolResult)
    {
        // The tool output format includes "Historical approval rate: X.X%"
        var lines = toolResult.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains("approval rate", StringComparison.OrdinalIgnoreCase))
            {
                // Extract percentage value
                var parts = line.Split(':');
                if (parts.Length >= 2)
                {
                    var valuePart = parts[^1].Trim().TrimEnd('%');
                    if (decimal.TryParse(valuePart,
                        global::System.Globalization.NumberStyles.Any,
                        global::System.Globalization.CultureInfo.InvariantCulture,
                        out var rate))
                    {
                        // Convert from percentage to decimal (e.g., 75.0 → 0.75)
                        return rate > 1m ? rate / 100m : rate;
                    }
                }
            }
        }

        // Check for minimum count
        foreach (var line in lines)
        {
            if (line.Contains("Found", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("similar", StringComparison.OrdinalIgnoreCase))
            {
                // Extract count — "Found N similar past decisions"
                var words = line.Split(' ');
                foreach (var word in words)
                {
                    if (int.TryParse(word, out var count) && count < MinHistoricalCount)
                    {
                        return null; // Not enough data points for reliable calibration
                    }
                }
            }
        }

        return null;
    }
}
