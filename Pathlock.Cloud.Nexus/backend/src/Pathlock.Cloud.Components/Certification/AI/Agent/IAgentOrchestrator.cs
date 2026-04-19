using Pathlock.Cloud.Components.Certification.AI.Models;

namespace Pathlock.Cloud.Components.Certification.AI.Agent;

/// <summary>
/// Orchestrator that runs the agentic AI loop: Plan → Act → Observe → Reflect.
/// Implements UC1 (Dynamic Data Gathering) and UC2 (Multi-Step Reasoning with Self-Reflection).
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Runs the full agentic reasoning loop for a single review item.
    /// The agent autonomously decides which tools to call, investigates data,
    /// reflects on its findings, and produces a final recommendation.
    /// </summary>
    /// <param name="item">The review item context to analyze.</param>
    /// <param name="anomalyScore">The anomaly score from the embedding detector.</param>
    /// <param name="toolContext">Shared context for tool execution (DB, campaign data, etc.).</param>
    /// <param name="campaignInsights">Campaign-level insights from the CampaignAnalysisAgent (UC3), if available.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An agent recommendation with full reasoning trace.</returns>
    Task<AgentRecommendation> RunAsync(
        ReviewItemContext item,
        decimal anomalyScore,
        AgentToolContext toolContext,
        CampaignInsights? campaignInsights = null,
        CancellationToken cancellationToken = default);
}
