using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pathlock.Cloud.Components.Certification.AI.Agent;

/// <summary>
/// A single message in the agent conversation (system, user, assistant, or tool result).
/// </summary>
public sealed class AgentMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// Tool calls made by the assistant (only set when Role == "assistant").
    /// </summary>
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AgentToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// The tool_call_id this message responds to (only set when Role == "tool").
    /// </summary>
    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    public AgentMessage() { Role = "user"; }

    public AgentMessage(string role, string? content, string? toolCallId = null)
    {
        Role = role;
        Content = content;
        ToolCallId = toolCallId;
    }

    /// <summary>
    /// Creates an assistant message with tool calls (no text content).
    /// </summary>
    public static AgentMessage AssistantToolCalls(List<AgentToolCall> toolCalls) => new()
    {
        Role = "assistant",
        Content = null,
        ToolCalls = toolCalls
    };
}

/// <summary>
/// A function/tool call requested by the LLM assistant.
/// </summary>
public sealed class AgentToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public AgentToolCallFunction Function { get; set; } = new();
}

public sealed class AgentToolCallFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";

    /// <summary>
    /// Parses the arguments JSON string into a JsonElement for tool execution.
    /// </summary>
    public JsonElement ParseArguments()
    {
        return JsonDocument.Parse(Arguments).RootElement;
    }
}

/// <summary>
/// Definition of a tool that the agent can call, sent to the LLM API.
/// </summary>
public sealed class AgentToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public AgentFunctionDefinition Function { get; set; } = new();
}

public sealed class AgentFunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; set; }
}

/// <summary>
/// Response from the LLM when using function-calling / tool-use mode.
/// </summary>
public sealed class AgentLlmResponse
{
    /// <summary>
    /// Text content if the model produced a final answer (finish_reason == "stop").
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Tool calls if the model wants to invoke tools (finish_reason == "tool_calls").
    /// </summary>
    public List<AgentToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// The finish reason: "stop" (final answer), "tool_calls" (wants to call tools), or "length".
    /// </summary>
    public string FinishReason { get; set; } = "stop";

    /// <summary>
    /// Total tokens used in this turn.
    /// </summary>
    public int TokensUsed { get; set; }

    /// <summary>
    /// Whether the API call succeeded.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if the call failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Shared context passed to agent tools for database and state access.
/// </summary>
public sealed class AgentToolContext
{
    /// <summary>
    /// The EF Core DbContext for database queries.
    /// </summary>
    public required Microsoft.EntityFrameworkCore.DbContext DbContext { get; init; }

    /// <summary>
    /// The campaign being analyzed.
    /// </summary>
    public long CertificationProcessId { get; init; }

    /// <summary>
    /// The current review item being analyzed (null for campaign-level tools).
    /// </summary>
    public Models.ReviewItemContext? CurrentItem { get; init; }

    /// <summary>
    /// All review items in the campaign (for cross-item analysis).
    /// </summary>
    public IReadOnlyList<Models.ReviewItemContext>? AllItems { get; init; }

    /// <summary>
    /// Anomaly scores computed by the embedding detector.
    /// </summary>
    public Dictionary<long, decimal>? AnomalyScores { get; init; }

    /// <summary>
    /// Feedback-based few-shot examples formatted as a prompt section (Learning Loop Layer 2).
    /// Injected into the agent's user prompt so the LLM learns from past reviewer corrections.
    /// </summary>
    public string? FeedbackPromptSection { get; init; }
}

/// <summary>
/// The result of a single agent reasoning step, used for building the audit trail.
/// </summary>
public sealed class AgentReasoningStep
{
    public int StepNumber { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public string? ToolArguments { get; set; }
    public string? ToolResult { get; set; }
    public string? Reasoning { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Complete result from the agent orchestrator including audit trail.
/// </summary>
public sealed class AgentRecommendation
{
    public string Decision { get; set; } = "NeedsReview";
    public decimal ConfidenceScore { get; set; }
    public string RiskLevel { get; set; } = "Medium";
    public string? RiskSummary { get; set; }
    public List<string> RiskFactors { get; set; } = [];
    public int TotalTokensUsed { get; set; }
    public int IterationsUsed { get; set; }
    public List<AgentReasoningStep> ReasoningTrace { get; set; } = [];
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Campaign-level insights produced by the CampaignAnalysisAgent (Use Case 3).
/// Injected into each item's prompt for cross-item awareness.
/// </summary>
public sealed class CampaignInsights
{
    public int TotalItems { get; set; }
    public int TotalUsers { get; set; }
    public int TotalRoles { get; set; }
    public List<BulkPattern> BulkPatterns { get; set; } = [];
    public List<string> HighRiskRoles { get; set; } = [];
    public string? NarrativeSummary { get; set; }
}

/// <summary>
/// A bulk provisioning or pattern detected across multiple users/roles.
/// </summary>
public sealed class BulkPattern
{
    public string PatternType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int AffectedItemCount { get; set; }
    public List<long> AffectedStepIds { get; set; } = [];
}

/// <summary>
/// Escalation classification for a review item (Use Case 6).
/// </summary>
public sealed class EscalationResult
{
    public EscalationTier Tier { get; set; } = EscalationTier.Standard;
    public string Reason { get; set; } = string.Empty;
    public bool RequiresSecurityReview { get; set; }
    public bool ShouldAutoApprove { get; set; }
}

/// <summary>
/// Escalation tiers for routing review items.
/// </summary>
public enum EscalationTier
{
    /// <summary>Low-risk items eligible for auto-approval.</summary>
    AutoApprove,
    /// <summary>Standard items routed to primary reviewer.</summary>
    Standard,
    /// <summary>High-risk items requiring additional scrutiny.</summary>
    Elevated,
    /// <summary>Critical items requiring security team review.</summary>
    Critical
}

/// <summary>
/// Request for the interactive reviewer assistant (Use Case 5).
/// </summary>
public sealed class ReviewerChatRequest
{
    public long CertificationProcessId { get; set; }
    public long ReviewItemStepId { get; set; }
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Previous conversation messages (for multi-turn context).
    /// The frontend sends back the conversation history.
    /// </summary>
    public List<ChatTurn>? ConversationHistory { get; set; }
}

/// <summary>
/// A single turn in the reviewer assistant conversation.
/// </summary>
public sealed class ChatTurn
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Response from the interactive reviewer assistant.
/// </summary>
public sealed class ReviewerChatResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<string>? SuggestedFollowUps { get; set; }
    public List<AgentReasoningStep>? ReasoningSteps { get; set; }
    public int TokensUsed { get; set; }
}
