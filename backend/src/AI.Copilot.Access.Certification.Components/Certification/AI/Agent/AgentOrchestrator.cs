using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using AI.Copilot.Access.Certification.Components.Certification.AI.Agent.Tools;
using AI.Copilot.Access.Certification.Components.Certification.AI.Models;
using AI.Copilot.Access.Certification.Platform.Attributes;

namespace AI.Copilot.Access.Certification.Components.Certification.AI.Agent;

/// <summary>
/// Core agentic AI orchestrator that implements the Plan → Act → Observe → Reflect loop.
/// 
/// — Dynamic Data Gathering: The agent autonomously chooses which tools to call
///        based on the review item's characteristics.
/// — Multi-Step Reasoning with Self-Reflection: After each tool result, the agent
///        reasons about what it learned and what it still needs before making a decision.
/// 
/// The loop terminates when the agent produces a final JSON recommendation or
/// the maximum iteration count is reached.
/// </summary>
[Component(typeof(IAgentOrchestrator), ComponentType.Service)]
public class AgentOrchestrator : IAgentOrchestrator
{
    private readonly ILlmGateway _llmGateway;
    private readonly AIServiceSettings _settings;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly IReadOnlyList<IAgentTool> _tools;
    private readonly IReadOnlyList<AgentToolDefinition> _toolDefinitions;

    private const int DefaultMaxIterations = 10;
    private const int DefaultMaxTokensPerTurn = 1000;

    public AgentOrchestrator(
        ILlmGateway llmGateway,
        IOptions<AIServiceSettings> settings,
        ILogger<AgentOrchestrator> logger)
    {
        _llmGateway = llmGateway;
        _settings = settings.Value;
        _logger = logger;

        // Register all available tools
        _tools = CreateToolSet();
        _toolDefinitions = _tools.Select(t => t.GetDefinition()).ToList();
    }

    public async Task<AgentRecommendation> RunAsync(
        ReviewItemContext item,
        decimal anomalyScore,
        AgentToolContext toolContext,
        CampaignInsights? campaignInsights = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var maxIterations = _settings.MaxAgentIterations > 0
            ? _settings.MaxAgentIterations
            : DefaultMaxIterations;

        var result = new AgentRecommendation();
        var messages = new List<AgentMessage>();
        var totalTokens = 0;

        try
        {
            // 1. Build system prompt — tells the agent about its role and available tools
            messages.Add(new AgentMessage("system", BuildAgentSystemPrompt()));

            // 2. Build user prompt — provides all available context for the review item
            messages.Add(new AgentMessage("user", BuildAgentUserPrompt(item, anomalyScore, campaignInsights, toolContext.FeedbackPromptSection)));

            // 3. Agent loop: Plan → Act → Observe → Reflect
            for (int iteration = 1; iteration <= maxIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug(
                    "Agent iteration {Iteration}/{Max} for step {StepId}",
                    iteration, maxIterations, item.StepId);

                var llmResponse = await _llmGateway.ChatWithToolsAsync(
                    messages,
                    _toolDefinitions,
                    DefaultMaxTokensPerTurn,
                    0.1,
                    cancellationToken);

                totalTokens += llmResponse.TokensUsed;

                if (!llmResponse.IsSuccess)
                {
                    _logger.LogWarning(
                        "Agent LLM call failed at iteration {Iteration} for step {StepId}: {Error}",
                        iteration, item.StepId, llmResponse.ErrorMessage ?? "Unknown");

                    result.IsSuccess = false;
                    result.ErrorMessage = $"LLM call failed at iteration {iteration}: {llmResponse.ErrorMessage ?? "Unknown error"}";
                    result.IterationsUsed = iteration;
                    result.TotalTokensUsed = totalTokens;
                    return result;
                }

                // Check finish reason
                if (llmResponse.FinishReason == "content_filter")
                {
                    _logger.LogWarning(
                        "Agent response blocked by content filter at iteration {Iteration} for step {StepId}",
                        iteration, item.StepId);

                    result.IsSuccess = false;
                    result.ErrorMessage = "Response blocked by Azure content filter";
                    result.IterationsUsed = iteration;
                    result.TotalTokensUsed = totalTokens;
                    return result;
                }

                if (llmResponse.FinishReason == "stop" || llmResponse.FinishReason == "length")
                {
                    // Agent produced a final answer — parse the JSON recommendation
                    // Preserve the reasoning trace collected during prior tool-call iterations
                    var existingTrace = result.ReasoningTrace;
                    result = ParseFinalAnswer(llmResponse.Content, iteration, totalTokens);
                    result.IterationsUsed = iteration;
                    result.ReasoningTrace.InsertRange(0, existingTrace);

                    // If JSON parsing failed but we got content, retry with explicit JSON-only instruction
                    if (!result.IsSuccess && !string.IsNullOrEmpty(llmResponse.Content))
                    {
                        _logger.LogWarning(
                            "JSON parse failed for step {StepId}, attempting JSON correction retry. Original: {Content}",
                            item.StepId, llmResponse.Content[..Math.Min(llmResponse.Content.Length, 200)]);

                        messages.Add(new AgentMessage("assistant", llmResponse.Content));
                        messages.Add(new AgentMessage("user",
                            "Your response could not be parsed as JSON. Respond with ONLY a valid JSON object, no other text before or after it. " +
                            "Required format: {\"decision\": \"Approve|Reject|NeedsReview\", \"confidenceScore\": 0.85, \"riskLevel\": \"Low|Medium|High|Critical\", " +
                            "\"riskSummary\": \"...\", \"riskFactors\": [\"...\", \"...\"]}"));

                        var retryResponse = await _llmGateway.ChatWithToolsAsync(
                            messages, [], DefaultMaxTokensPerTurn, 0.1, cancellationToken);

                        totalTokens += retryResponse.TokensUsed;

                        if (retryResponse.IsSuccess && !string.IsNullOrEmpty(retryResponse.Content))
                        {
                            var retryResult = ParseFinalAnswer(retryResponse.Content, iteration, totalTokens);
                            if (retryResult.IsSuccess)
                            {
                                retryResult.IterationsUsed = iteration;
                                retryResult.ReasoningTrace.InsertRange(0, existingTrace);
                                _logger.LogInformation(
                                    "JSON correction retry succeeded for step {StepId}: {Decision} ({Confidence:P0})",
                                    item.StepId, retryResult.Decision, retryResult.ConfidenceScore);
                                return retryResult;
                            }
                        }

                        _logger.LogWarning("JSON correction retry also failed for step {StepId}", item.StepId);
                    }

                    _logger.LogInformation(
                        "Agent completed for step {StepId}: {Decision} ({Confidence:P0}) in {Iterations} iterations, {Tokens} tokens",
                        item.StepId, result.Decision, result.ConfidenceScore, iteration, totalTokens);

                    return result;
                }

                if (llmResponse.ToolCalls is { Count: > 0 })
                {
                    // Agent requested tool calls — execute them and feed results back
                    var assistantMsg = AgentMessage.AssistantToolCalls(llmResponse.ToolCalls);
                    messages.Add(assistantMsg);

                    foreach (var toolCall in llmResponse.ToolCalls)
                    {
                        var step = new AgentReasoningStep
                        {
                            StepNumber = result.ReasoningTrace.Count + 1,
                            Action = "tool_call",
                            ToolName = toolCall.Function.Name,
                            ToolArguments = toolCall.Function.Arguments
                        };

                        try
                        {
                            var toolResult = await ExecuteToolAsync(toolCall, toolContext, cancellationToken);
                            step.ToolResult = TruncateToolResult(toolResult, 2000);

                            messages.Add(new AgentMessage("tool", toolResult, toolCallId: toolCall.Id));
                        }
                        catch (Exception ex)
                        {
                            var errorMessage = $"Tool execution error: {ex.Message}";
                            step.ToolResult = errorMessage;
                            messages.Add(new AgentMessage("tool", errorMessage, toolCallId: toolCall.Id));

                            _logger.LogWarning(ex,
                                "Tool {ToolName} failed for step {StepId}",
                                toolCall.Function.Name, item.StepId);
                        }

                        result.ReasoningTrace.Add(step);
                    }
                }
                else
                {
                    // No tool calls and no stop — unexpected. Add content as assistant message.
                    if (!string.IsNullOrEmpty(llmResponse.Content))
                    {
                        messages.Add(new AgentMessage("assistant", llmResponse.Content));
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Agent returned empty response at iteration {Iteration} for step {StepId}",
                            iteration, item.StepId);
                        break;
                    }
                }
            }

            // Max iterations reached — force a final answer
            _logger.LogWarning(
                "Agent reached max iterations ({Max}) for step {StepId}. Forcing final answer.",
                maxIterations, item.StepId);

            result = await ForceFinalAnswer(messages, totalTokens, maxIterations, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "Agent operation was cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in agent orchestrator for step {StepId}", item.StepId);
            result.IsSuccess = false;
            result.ErrorMessage = $"Agent error: {ex.Message}";
        }
        finally
        {
            sw.Stop();
            result.TotalTokensUsed = totalTokens;

            _logger.LogDebug(
                "Agent orchestrator finished for step {StepId} in {ElapsedMs}ms",
                item.StepId, sw.ElapsedMilliseconds);
        }

        return result;
    }

    /// <summary>
    /// Executes a single tool call by dispatching to the matching registered tool.
    /// </summary>
    private async Task<string> ExecuteToolAsync(
        AgentToolCall toolCall,
        AgentToolContext context,
        CancellationToken ct)
    {
        var tool = _tools.FirstOrDefault(t =>
            string.Equals(t.Name, toolCall.Function.Name, StringComparison.OrdinalIgnoreCase));

        if (tool == null)
        {
            _logger.LogWarning("Agent requested unknown tool: {ToolName}", toolCall.Function.Name);
            return $"Error: Unknown tool '{toolCall.Function.Name}'. Available tools: {string.Join(", ", _tools.Select(t => t.Name))}";
        }

        var args = toolCall.Function.ParseArguments();
        return await tool.ExecuteAsync(args, context, ct);
    }

    /// <summary>
    /// Parses the agent's final text response into an AgentRecommendation.
    /// Expects the same JSON schema as the non-agent flow.
    /// </summary>
    private AgentRecommendation ParseFinalAnswer(string? content, int iteration, int totalTokens)
    {
        var result = new AgentRecommendation
        {
            IterationsUsed = iteration,
            TotalTokensUsed = totalTokens
        };

        if (string.IsNullOrWhiteSpace(content))
        {
            result.Decision = "NeedsReview";
            result.ConfidenceScore = 0m;
            result.RiskLevel = "Medium";
            result.RiskSummary = "Agent did not produce a final answer.";
            result.ErrorMessage = "Agent returned empty or whitespace-only content";
            result.IsSuccess = false;
            return result;
        }

        try
        {
            // Extract JSON from the response — the agent may wrap it in markdown code fences
            var json = ExtractJson(content);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            result.Decision = NormalizeDecision(GetJsonString(root, "decision"));
            result.ConfidenceScore = Math.Clamp(
                GetJsonDecimal(root, "confidenceScore", "confidence_score", "confidence"), 0m, 1m);
            result.RiskLevel = NormalizeRiskLevel(GetJsonString(root, "riskLevel", "risk_level"));
            result.RiskSummary = GetJsonString(root, "riskSummary", "risk_summary");
            result.RiskFactors = GetJsonStringArray(root, "riskFactors", "risk_factors");
            result.IsSuccess = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse agent final answer: {Content}", content);
            result.Decision = "NeedsReview";
            result.ConfidenceScore = 0m;
            result.RiskLevel = "Medium";
            result.RiskSummary = content;
            result.ErrorMessage = $"Failed to parse agent response as JSON: {ex.Message}";
            result.IsSuccess = false;
        }

        return result;
    }

    /// <summary>
    /// Forces the agent to produce a final answer by sending a directive message
    /// when the max iteration limit has been reached.
    /// </summary>
    private async Task<AgentRecommendation> ForceFinalAnswer(
        List<AgentMessage> messages,
        int totalTokens,
        int maxIterations,
        CancellationToken ct)
    {
        messages.Add(new AgentMessage("user",
            "You have reached the maximum number of investigation steps. " +
            "Based on everything you have gathered so far, provide your final recommendation NOW as a JSON object. " +
            "Do not call any more tools."));

        var response = await _llmGateway.ChatWithToolsAsync(
            messages,
            [], // No tools — force a text answer
            DefaultMaxTokensPerTurn,
            0.1,
            ct);

        totalTokens += response.TokensUsed;

        if (response.IsSuccess && !string.IsNullOrEmpty(response.Content))
        {
            return ParseFinalAnswer(response.Content, maxIterations, totalTokens);
        }

        return new AgentRecommendation
        {
            Decision = "NeedsReview",
            ConfidenceScore = 0m,
            RiskLevel = "Medium",
            RiskSummary = "Agent failed to produce a final recommendation within the iteration limit.",
            ErrorMessage = "Force-final-answer LLM call failed after max iterations",
            IsSuccess = false,
            IterationsUsed = maxIterations,
            TotalTokensUsed = totalTokens
        };
    }

    /// <summary>
    /// Creates the full set of tools available to the agent.
    /// </summary>
    private static IReadOnlyList<IAgentTool> CreateToolSet()
    {
        return new IAgentTool[]
        {
            new GetHistoricalDecisionsTool(),
            new CheckSoDViolationsTool(),
            new GetPeerGroupDetailsTool(),
            new GetRoleRiskProfileTool(),
            new GetCampaignOverviewTool(),
            new DetectBulkPatternsTool(),
            new GetSimilarPastDecisionsTool(),
            new SimulateApprovalImpactTool()
        };
    }

    #region System & User Prompts

    private static string BuildAgentSystemPrompt()
    {
        return """
            You are an expert Identity Governance and Administration (IGA) analyst agent.
            Your task is to analyze user access review items and provide risk-based recommendations.
            
            ## How You Work
            You have access to tools for investigation. You should:
            1. PLAN: Assess the review item and determine what additional data would help your analysis.
            2. ACT: Call one or more tools to gather the data you need.
            3. OBSERVE: Examine the tool results and reason about what they mean.
            4. REFLECT: Decide if you have enough information to make a confident recommendation, or if you need more data.
            5. REPEAT steps 2-4 until you have high confidence, then produce your final answer.
            
            ## Investigation Strategy
            - Start by checking historical decisions for this user-role combination.
            - If usage is low or zero, check peer group patterns to see if this is normal.
            - If SoD violations are flagged, use the SoD tool to get details.
            - For privileged-looking roles (ADMIN, SAP_ALL, etc.), check the role risk profile.
            - If the anomaly score is high (>0.5), investigate more deeply with simulate_approval_impact.
            - You do NOT need to call every tool — only call tools that will help your analysis.
            
            ## Final Answer Format
            When you have gathered enough information, respond with a JSON object (no markdown fences):
            {
              "decision": "Approve" | "Reject" | "NeedsReview",
              "confidenceScore": 0.85,
              "riskLevel": "Low" | "Medium" | "High" | "Critical",
              "riskSummary": "2-3 sentence explanation incorporating evidence from your investigation",
              "riskFactors": ["factor 1", "factor 2"]
            }
            
            ## Rules
            - "confidenceScore" MUST be a number between 0.0 and 1.0.
            - Confidence should reflect how much evidence you gathered.
            - Be concise and evidence-based — cite specific data from tool results.
            - If data is insufficient even after investigation, use "NeedsReview" with moderate confidence.
            - Do NOT invent data — only reference information from the context or tool results.
            """;
    }

    private static string BuildAgentUserPrompt(
        ReviewItemContext item,
        decimal anomalyScore,
        CampaignInsights? campaignInsights,
        string? feedbackPromptSection = null)
    {
        var sodSection = item.HasSodViolation && item.SodViolationDetails.Count > 0
            ? $"\nSoD Violations: {string.Join("; ", item.SodViolationDetails)}"
            : item.HasSodViolation ? "\nSoD Violations: Yes (details not available)" : "\nSoD Violations: None";

        var campaignSection = campaignInsights != null
            ? $"""
              
              Campaign Context (from pre-analysis):
              - Total items in campaign: {campaignInsights.TotalItems}
              - Total unique users: {campaignInsights.TotalUsers}
              - Total unique roles: {campaignInsights.TotalRoles}
              - Known high-risk roles: {(campaignInsights.HighRiskRoles.Count > 0 ? string.Join(", ", campaignInsights.HighRiskRoles) : "None identified")}
              - Bulk patterns detected: {campaignInsights.BulkPatterns.Count}
              {(campaignInsights.NarrativeSummary != null ? $"- Campaign summary: {campaignInsights.NarrativeSummary}" : "")}
              """
            : "";

        return $"""
            Analyze this access review item. You may call tools to investigate before making your recommendation.
            
            Employee Information:
            - User ID: {item.UserId}
            - Name: {item.EmployeeName ?? "Unknown"}
            - Job Title: {item.EmployeeJob ?? "Unknown"}
            - Department: {item.EmployeeDepartment ?? "Unknown"}
            - Manager: {item.EmployeeManagerName ?? "Unknown"}
            - Account: {item.Account ?? "Unknown"}
            
            Role Under Review:
            - Role ID: {item.RoleId}
            - Role Name: {item.RoleName ?? "Unknown"}
            - Role Description: {item.RoleDescription ?? "No description available"}
            - Step ID: {item.StepId}
            
            Usage Data:
            - Usage Percentage: {item.UsagePercentage}%
            - Used Activities: {item.UsedActivities}
            - Last Used: {(item.LastUsed.HasValue ? item.LastUsed.Value.ToString("yyyy-MM-dd") : "Never")}
            - Days Since Last Use: {item.DaysSinceLastUsed?.ToString() ?? "Never used"}
            
            Peer Analysis:
            - Peer Usage: {item.PeerUsagePercent?.ToString("F1") ?? "N/A"}% of peers in same dept/job have this role
            - Anomaly Score: {anomalyScore:F4} (0=normal, 1=highly anomalous)
            {sodSection}
            {campaignSection}
            {(string.IsNullOrEmpty(feedbackPromptSection) ? "" : feedbackPromptSection)}
            
            Investigate as needed and provide your final recommendation as a JSON object.
            """;
    }

    #endregion

    #region JSON Parsing Helpers

    private static string ExtractJson(string content)
    {
        // Strip markdown code fences if present
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["```json".Length..];
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed[3..];
        }

        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed[..^3];
        }

        trimmed = trimmed.Trim();

        // If it already starts with '{', it's JSON
        if (trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        // LLM sometimes wraps JSON in narrative text like "Based on my analysis... {json}"
        // Search for a JSON object embedded in the text using brace matching
        var startIdx = trimmed.IndexOf('{');
        if (startIdx >= 0)
        {
            var json = ExtractJsonByBraceMatching(trimmed, startIdx);
            if (json != null)
            {
                return json;
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Extracts a balanced JSON object from text starting at the given '{' index.
    /// Handles nested braces, string literals, and escape sequences.
    /// </summary>
    private static string? ExtractJsonByBraceMatching(string text, int startIndex)
    {
        var depth = 0;
        var inString = false;
        var escape = false;

        for (int i = startIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[startIndex..(i + 1)];
                }
            }
        }

        return null; // Unbalanced braces
    }

    private static string? GetJsonString(JsonElement root, params string[] keys)
    {
        foreach (var prop in root.EnumerateObject())
        {
            foreach (var key in keys)
            {
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.GetRawText();
                }
            }
        }
        return null;
    }

    private static decimal GetJsonDecimal(JsonElement root, params string[] keys)
    {
        foreach (var prop in root.EnumerateObject())
        {
            foreach (var key in keys)
            {
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out var num))
                        return num;
                    if (prop.Value.ValueKind == JsonValueKind.String &&
                        decimal.TryParse(prop.Value.GetString(),
                            global::System.Globalization.NumberStyles.Any,
                            global::System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                }
            }
        }
        return 0m;
    }

    private static List<string> GetJsonStringArray(JsonElement root, params string[] keys)
    {
        foreach (var prop in root.EnumerateObject())
        {
            foreach (var key in keys)
            {
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    return prop.Value.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .ToList();
                }
            }
        }
        return [];
    }

    private static string NormalizeDecision(string? decision) => decision?.Trim().ToLowerInvariant() switch
    {
        "approve" or "approved" or "accept" => "Approve",
        "reject" or "rejected" or "revoke" or "remove" => "Reject",
        _ => "NeedsReview"
    };

    private static string NormalizeRiskLevel(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "low" => "Low",
        "medium" => "Medium",
        "high" => "High",
        "critical" => "Critical",
        _ => "Medium"
    };

    private static string TruncateToolResult(string result, int maxLength)
    {
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "\n... (truncated)";
    }

    #endregion
}
