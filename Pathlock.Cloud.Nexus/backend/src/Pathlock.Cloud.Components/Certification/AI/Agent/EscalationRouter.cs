using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Pathlock.Cloud.Components.Certification.AI.Models;
using Pathlock.Cloud.Platform.Attributes;

namespace Pathlock.Cloud.Components.Certification.AI.Agent;

/// <summary>
/// Interface for escalation routing (UC6: Escalation &amp; Routing Agent).
/// </summary>
public interface IEscalationRouter
{
    /// <summary>
    /// Classifies a review item into an escalation tier based on risk signals,
    /// enabling workflow routing and prioritization.
    /// </summary>
    EscalationResult Classify(
        AgentRecommendation recommendation,
        ReviewItemContext item,
        decimal anomalyScore);
}

/// <summary>
/// Rule-based escalation router that triages review items (UC6).
/// 
/// Tiers:
/// - AutoApprove: Low-risk items with high confidence that can be batch-approved.
/// - Standard: Normal items routed to the primary reviewer.
/// - Elevated: High-risk items flagged for additional scrutiny.
/// - Critical: Items requiring security team review (SoD violations + high anomaly + privileged role).
/// 
/// The classification is deterministic (no LLM call) for speed and predictability.
/// It runs as a post-processing step after the agent recommendation is generated.
/// </summary>
[Component(typeof(IEscalationRouter), ComponentType.Service)]
public class EscalationRouter : IEscalationRouter
{
    private readonly AIServiceSettings _settings;
    private readonly ILogger<EscalationRouter> _logger;

    /// <summary>
    /// Privileged role name patterns that trigger elevated scrutiny.
    /// </summary>
    private static readonly string[] PrivilegedRolePatterns =
    [
        "SAP_ALL", "SAP_NEW", "S_DEVELOP", "ADMIN", "SUPER",
        "ROOT", "DEBUG", "SYS_ADMIN", "FULL_ACCESS", "ALL_ACCESS"
    ];

    public EscalationRouter(
        IOptions<AIServiceSettings> settings,
        ILogger<EscalationRouter> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public EscalationResult Classify(
        AgentRecommendation recommendation,
        ReviewItemContext item,
        decimal anomalyScore)
    {
        var result = new EscalationResult();
        var riskSignals = 0;
        var reasons = new List<string>();

        // Signal 1: SoD violations
        if (item.HasSodViolation)
        {
            riskSignals += 2;
            reasons.Add("SoD violation detected");
        }

        // Signal 2: High anomaly score
        if (anomalyScore >= 0.7m)
        {
            riskSignals += 2;
            reasons.Add($"High anomaly score ({anomalyScore:F2})");
        }
        else if (anomalyScore >= 0.5m)
        {
            riskSignals += 1;
            reasons.Add($"Moderate anomaly score ({anomalyScore:F2})");
        }

        // Signal 3: Privileged role
        var isPrivileged = IsPrivilegedRole(item.RoleName);
        if (isPrivileged)
        {
            riskSignals += 2;
            reasons.Add($"Privileged role: {item.RoleName}");
        }

        // Signal 4: Zero/low usage
        if (item.UsagePercentage == 0 && item.DaysSinceLastUsed is null or > 180)
        {
            riskSignals += 1;
            reasons.Add("Zero usage with no recent activity");
        }

        // Signal 5: Agent recommendation risk level
        if (recommendation.RiskLevel is "Critical")
        {
            riskSignals += 2;
            reasons.Add("Agent assessed Critical risk");
        }
        else if (recommendation.RiskLevel is "High")
        {
            riskSignals += 1;
            reasons.Add("Agent assessed High risk");
        }

        // Signal 6: Low agent confidence
        if (recommendation.ConfidenceScore < 0.5m)
        {
            riskSignals += 1;
            reasons.Add($"Low agent confidence ({recommendation.ConfidenceScore:F2})");
        }

        // Classify into tier based on accumulated risk signals
        if (riskSignals >= 5)
        {
            result.Tier = EscalationTier.Critical;
            result.RequiresSecurityReview = true;
            result.ShouldAutoApprove = false;
        }
        else if (riskSignals >= 3)
        {
            result.Tier = EscalationTier.Elevated;
            result.RequiresSecurityReview = isPrivileged && item.HasSodViolation;
            result.ShouldAutoApprove = false;
        }
        else if (riskSignals == 0 &&
                 recommendation.Decision == "Approve" &&
                 recommendation.ConfidenceScore >= 0.85m &&
                 recommendation.RiskLevel == "Low" &&
                 !item.HasSodViolation &&
                 anomalyScore < 0.3m)
        {
            result.Tier = EscalationTier.AutoApprove;
            result.ShouldAutoApprove = true;
            result.RequiresSecurityReview = false;
        }
        else
        {
            result.Tier = EscalationTier.Standard;
            result.ShouldAutoApprove = false;
            result.RequiresSecurityReview = false;
        }

        result.Reason = reasons.Count > 0
            ? string.Join("; ", reasons)
            : "No elevated risk signals detected";

        _logger.LogDebug(
            "Escalation for step {StepId}: {Tier} ({Reason})",
            item.StepId, result.Tier, result.Reason);

        return result;
    }

    private static bool IsPrivilegedRole(string? roleName)
    {
        if (string.IsNullOrEmpty(roleName)) return false;

        return PrivilegedRolePatterns.Any(pattern =>
            roleName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
