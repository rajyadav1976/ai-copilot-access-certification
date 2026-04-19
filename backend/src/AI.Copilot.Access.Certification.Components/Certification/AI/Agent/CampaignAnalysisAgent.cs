using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using AI.Copilot.Access.Certification.Components.Certification.AI.Agent.Tools;
using AI.Copilot.Access.Certification.Components.Certification.AI.Models;
using AI.Copilot.Access.Certification.Platform.Attributes;

namespace AI.Copilot.Access.Certification.Components.Certification.AI.Agent;

/// <summary>
/// Interface for the campaign-level analysis agent (UC3: Cross-Item Campaign-Level Analysis).
/// </summary>
public interface ICampaignAnalysisAgent
{
    /// <summary>
    /// Runs campaign-level pre-analysis to identify bulk patterns, high-risk roles,
    /// and cross-item correlations before individual item analysis begins.
    /// </summary>
    Task<CampaignInsights> AnalyzeCampaignAsync(
        AgentToolContext toolContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs once before per-item analysis to produce campaign-wide insights (UC3).
/// 
/// The agent:
/// 1. Calls GetCampaignOverview to get campaign statistics.
/// 2. Calls DetectBulkPatterns to find systemic provisioning patterns.
/// 3. Synthesizes findings into a CampaignInsights object injected into each item's context.
/// 
/// This enables per-item agents to consider campaign-wide context (e.g., "this role
/// is assigned to 80% of users in this campaign, suggesting it's a baseline role").
/// </summary>
[Component(typeof(ICampaignAnalysisAgent), ComponentType.Service)]
public class CampaignAnalysisAgent : ICampaignAnalysisAgent
{
    private readonly ILlmGateway _llmGateway;
    private readonly AIServiceSettings _settings;
    private readonly ILogger<CampaignAnalysisAgent> _logger;

    public CampaignAnalysisAgent(
        ILlmGateway llmGateway,
        IOptions<AIServiceSettings> settings,
        ILogger<CampaignAnalysisAgent> logger)
    {
        _llmGateway = llmGateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<CampaignInsights> AnalyzeCampaignAsync(
        AgentToolContext toolContext,
        CancellationToken cancellationToken = default)
    {
        var insights = new CampaignInsights();

        try
        {
            _logger.LogInformation(
                "Starting campaign-level analysis for certification process {Id}",
                toolContext.CertificationProcessId);

            // Step 1: Get campaign overview using the tool
            var overviewTool = new GetCampaignOverviewTool();
            var overviewResult = await overviewTool.ExecuteAsync(
                JsonDocument.Parse("{}").RootElement,
                toolContext,
                cancellationToken);

            // Step 2: Detect bulk patterns using the tool
            var patternTool = new DetectBulkPatternsTool();
            var patternResult = await patternTool.ExecuteAsync(
                JsonDocument.Parse("{}").RootElement,
                toolContext,
                cancellationToken);

            // Step 3: Extract basic stats from campaign items
            if (toolContext.AllItems != null)
            {
                insights.TotalItems = toolContext.AllItems.Count;
                insights.TotalUsers = toolContext.AllItems.Select(i => i.UserId).Distinct().Count();
                insights.TotalRoles = toolContext.AllItems.Select(i => i.RoleName).Distinct().Count();

                // Identify roles that appear to be high-risk based on naming patterns
                var privilegedKeywords = new[] { "ADMIN", "SAP_ALL", "SUPER", "ROOT", "S_DEVELOP", "DEBUG" };
                insights.HighRiskRoles = toolContext.AllItems
                    .Select(i => i.RoleName)
                    .Where(r => r != null && privilegedKeywords.Any(k =>
                        r.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    .Distinct()
                    .ToList()!;
            }

            // Step 4: Use LLM to produce a narrative summary of campaign patterns
            var narrativePrompt = BuildNarrativePrompt(overviewResult, patternResult);
            var messages = new List<AgentMessage>
            {
                new("system",
                    "You are an IGA analyst. Produce a 2-3 sentence executive summary of this certification campaign's risk posture. " +
                    "Focus on actionable patterns that will help reviewers prioritize."),
                new("user", narrativePrompt)
            };

            var llmResponse = await _llmGateway.ChatWithToolsAsync(
                messages, [], 300, 0.3, cancellationToken);

            if (llmResponse.IsSuccess && !string.IsNullOrEmpty(llmResponse.Content))
            {
                insights.NarrativeSummary = llmResponse.Content.Trim();
            }

            // Step 5: Parse bulk patterns from the tool result
            insights.BulkPatterns = ParseBulkPatterns(patternResult);

            _logger.LogInformation(
                "Campaign analysis complete: {Items} items, {Users} users, {Roles} roles, {Patterns} patterns, {HighRisk} high-risk roles",
                insights.TotalItems, insights.TotalUsers, insights.TotalRoles,
                insights.BulkPatterns.Count, insights.HighRiskRoles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Campaign-level analysis failed for {Id}; proceeding without campaign insights",
                toolContext.CertificationProcessId);

            // Return partial insights — this is a non-critical enhancement
            if (toolContext.AllItems != null)
            {
                insights.TotalItems = toolContext.AllItems.Count;
                insights.TotalUsers = toolContext.AllItems.Select(i => i.UserId).Distinct().Count();
                insights.TotalRoles = toolContext.AllItems.Select(i => i.RoleName).Distinct().Count();
            }
        }

        return insights;
    }

    private static string BuildNarrativePrompt(string overviewResult, string patternResult)
    {
        return $"""
            Campaign Overview:
            {overviewResult}
            
            Detected Patterns:
            {patternResult}
            
            Summarize the campaign risk posture in 2-3 sentences.
            """;
    }

    private static List<BulkPattern> ParseBulkPatterns(string patternResult)
    {
        var patterns = new List<BulkPattern>();

        // Parse the lines from the pattern tool's output
        var lines = patternResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string? currentType = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("##"))
            {
                // Section header — e.g., "## Unused Bulk Roles"
                currentType = trimmed.TrimStart('#').Trim();
            }
            else if (trimmed.StartsWith("- ") && currentType != null)
            {
                patterns.Add(new BulkPattern
                {
                    PatternType = currentType,
                    Description = trimmed[2..].Trim(),
                    AffectedItemCount = 1 // Individual patterns counted as 1
                });
            }
        }

        return patterns;
    }
}
