using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Pathlock.Cloud.Components.Certification.AI.Models;
using Pathlock.Cloud.Platform.Attributes;
using Pathlock.Cloud.Platform.Session;
using Pathlock.Cloud.Shared.Entities.Components.Certifications;

namespace Pathlock.Cloud.Components.Certification.AI.Agent;

/// <summary>
/// Interface for the interactive reviewer assistant (UC5: Conversational Agent).
/// </summary>
public interface IReviewerAssistant
{
    /// <summary>
    /// Handles a reviewer's question about a specific review item.
    /// The assistant can investigate using tools and maintains multi-turn conversation.
    /// </summary>
    Task<ReviewerChatResponse> ChatAsync(
        ReviewerChatRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interactive reviewer assistant that answers questions about review items (UC5).
/// 
/// When a reviewer has a question about a specific item (e.g., "Why was this flagged?"
/// or "What would happen if I approve this?"), the assistant:
/// 1. Loads the review item context and any existing AI recommendation.
/// 2. Builds a conversation with the reviewer's question and history.
/// 3. Uses the same agent tools to investigate and answer the question.
/// 4. Returns an answer with optional follow-up suggestions.
/// </summary>
[Component(typeof(IReviewerAssistant), ComponentType.Service)]
public class ReviewerAssistant : IReviewerAssistant
{
    private readonly ILlmGateway _llmGateway;
    private readonly IDataContextAggregator _contextAggregator;
    private readonly ISessionContext _sessionContext;
    private readonly AIServiceSettings _settings;
    private readonly ILogger<ReviewerAssistant> _logger;

    /// <summary>
    /// Maximum agent iterations for chat (fewer than full analysis since this is interactive).
    /// </summary>
    private const int MaxChatIterations = 5;

    public ReviewerAssistant(
        ILlmGateway llmGateway,
        IDataContextAggregator contextAggregator,
        ISessionContext sessionContext,
        IOptions<AIServiceSettings> settings,
        ILogger<ReviewerAssistant> logger)
    {
        _llmGateway = llmGateway;
        _contextAggregator = contextAggregator;
        _sessionContext = sessionContext;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ReviewerChatResponse> ChatAsync(
        ReviewerChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new ReviewerChatResponse();
        var totalTokens = 0;

        try
        {
            var db = _sessionContext.DbContext;

            // 1. Load item context
            var contexts = await _contextAggregator.AggregateContextAsync(
                request.CertificationProcessId,
                [request.ReviewItemStepId]);

            var itemContext = contexts.FirstOrDefault();
            if (itemContext == null)
            {
                response.Answer = "I couldn't find the review item you're asking about. Please verify the item exists.";
                return response;
            }

            // 2. Load existing AI recommendation if available
            var existingRec = await db.Set<AIRecommendation>()
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.CertificationProcessId == request.CertificationProcessId &&
                    r.ReviewItemStepId == request.ReviewItemStepId &&
                    r.Status == "Generated",
                    cancellationToken);

            // 3. Build the tool context
            var allContexts = await _contextAggregator.AggregateContextAsync(
                request.CertificationProcessId);

            var toolContext = new AgentToolContext
            {
                DbContext = db,
                CertificationProcessId = request.CertificationProcessId,
                CurrentItem = itemContext,
                AllItems = allContexts
            };

            // 4. Build conversation messages
            var messages = new List<AgentMessage>
            {
                new("system", BuildChatSystemPrompt(itemContext, existingRec))
            };

            // Add conversation history
            if (request.ConversationHistory is { Count: > 0 })
            {
                foreach (var turn in request.ConversationHistory)
                {
                    messages.Add(new AgentMessage(turn.Role, turn.Content));
                }
            }

            // Add current question
            messages.Add(new AgentMessage("user", request.Question));

            // 5. Build tool definitions for chat
            var tools = CreateChatTools();
            var toolDefs = tools.Select(t => t.GetDefinition()).ToList();

            // 6. Agent loop (limited iterations for interactivity)
            var reasoningSteps = new List<AgentReasoningStep>();

            for (int i = 0; i < MaxChatIterations; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var llmResponse = await _llmGateway.ChatWithToolsAsync(
                    messages, toolDefs, 800, 0.2, cancellationToken);

                totalTokens += llmResponse.TokensUsed;

                if (!llmResponse.IsSuccess)
                {
                    response.Answer = "I encountered an error while processing your question. Please try again.";
                    response.TokensUsed = totalTokens;
                    return response;
                }

                // Final answer
                if (llmResponse.FinishReason == "stop" || llmResponse.FinishReason == "length")
                {
                    response.Answer = llmResponse.Content ?? "I wasn't able to formulate an answer.";
                    break;
                }

                // Tool calls
                if (llmResponse.ToolCalls is { Count: > 0 })
                {
                    var assistantMsg = AgentMessage.AssistantToolCalls(llmResponse.ToolCalls);
                    messages.Add(assistantMsg);

                    foreach (var tc in llmResponse.ToolCalls)
                    {
                        var step = new AgentReasoningStep
                        {
                            StepNumber = reasoningSteps.Count + 1,
                            Action = "tool_call",
                            ToolName = tc.Function.Name,
                            ToolArguments = tc.Function.Arguments
                        };

                        try
                        {
                            var tool = tools.FirstOrDefault(t =>
                                string.Equals(t.Name, tc.Function.Name, StringComparison.OrdinalIgnoreCase));

                            if (tool != null)
                            {
                                var args = tc.Function.ParseArguments();
                                var result = await tool.ExecuteAsync(args, toolContext, cancellationToken);
                                step.ToolResult = result;
                                messages.Add(new AgentMessage("tool", result, toolCallId: tc.Id));
                            }
                            else
                            {
                                var err = $"Unknown tool: {tc.Function.Name}";
                                step.ToolResult = err;
                                messages.Add(new AgentMessage("tool", err, toolCallId: tc.Id));
                            }
                        }
                        catch (Exception ex)
                        {
                            var err = $"Tool error: {ex.Message}";
                            step.ToolResult = err;
                            messages.Add(new AgentMessage("tool", err, toolCallId: tc.Id));
                        }

                        reasoningSteps.Add(step);
                    }
                }
            }

            // 7. Generate follow-up suggestions
            response.SuggestedFollowUps = GenerateFollowUpSuggestions(request.Question, itemContext);
            response.ReasoningSteps = reasoningSteps;
            response.TokensUsed = totalTokens;

            _logger.LogInformation(
                "Reviewer chat completed for step {StepId}: {TokensUsed} tokens, {Steps} tool calls",
                request.ReviewItemStepId, totalTokens, reasoningSteps.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in reviewer assistant for step {StepId}", request.ReviewItemStepId);
            response.Answer = "An error occurred while processing your question. Please try again later.";
        }

        return response;
    }

    private static string BuildChatSystemPrompt(ReviewItemContext item, AIRecommendation? existingRec)
    {
        var recContext = existingRec != null
            ? $"""
              
              Existing AI Recommendation:
              - Decision: {existingRec.Decision}
              - Confidence: {existingRec.ConfidenceScore:F2}
              - Risk Level: {existingRec.RiskLevel}
              - Risk Summary: {existingRec.RiskSummary}
              """
            : "\nNo existing AI recommendation available for this item.";

        return $"""
            You are an interactive reviewer assistant helping with an access certification review.
            You are conversational and helpful. Answer the reviewer's questions about the following review item.
            
            You have access to investigation tools — use them to answer questions you can't answer directly.
            
            Item Context:
            - User: {item.EmployeeName ?? "Unknown"} (ID: {item.UserId})
            - Department: {item.EmployeeDepartment ?? "Unknown"}
            - Job: {item.EmployeeJob ?? "Unknown"}
            - Role: {item.RoleName ?? "Unknown"} (ID: {item.RoleId})
            - Usage: {item.UsagePercentage}%
            - Last Used: {(item.LastUsed.HasValue ? item.LastUsed.Value.ToString("yyyy-MM-dd") : "Never")}
            - SoD Violations: {(item.HasSodViolation ? "Yes" : "No")}
            - Peer Usage: {item.PeerUsagePercent?.ToString("F1") ?? "N/A"}%
            {recContext}
            
            Guidelines:
            - Be concise and factual.
            - When you use a tool, explain what you found.
            - If you don't know something, say so rather than guessing.
            - Suggest actionable next steps when appropriate.
            """;
    }

    private static IReadOnlyList<IAgentTool> CreateChatTools()
    {
        return new IAgentTool[]
        {
            new Tools.GetHistoricalDecisionsTool(),
            new Tools.CheckSoDViolationsTool(),
            new Tools.GetPeerGroupDetailsTool(),
            new Tools.GetRoleRiskProfileTool(),
            new Tools.GetSimilarPastDecisionsTool(),
            new Tools.SimulateApprovalImpactTool()
        };
    }

    private static List<string> GenerateFollowUpSuggestions(string currentQuestion, ReviewItemContext item)
    {
        var suggestions = new List<string>();

        // Context-aware follow-up suggestions
        if (item.HasSodViolation)
            suggestions.Add("What are the specific SoD violations for this user?");

        if (item.UsagePercentage == 0)
            suggestions.Add("Do peers in the same department use this role?");

        if (item.PeerUsagePercent is < 20)
            suggestions.Add("Why does this user have a role that most peers don't have?");

        if (!currentQuestion.Contains("history", StringComparison.OrdinalIgnoreCase))
            suggestions.Add("What's the approval history for this user-role combination?");

        if (!currentQuestion.Contains("impact", StringComparison.OrdinalIgnoreCase))
            suggestions.Add("What would be the impact of approving this role?");

        return suggestions.Take(3).ToList();
    }
}
