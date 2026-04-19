using System.Text.Json;

namespace AI.Copilot.Access.Certification.Components.Certification.AI.Agent;

/// <summary>
/// Interface for tools that the AI agent can invoke during its reasoning loop.
/// Each tool encapsulates a data-retrieval or analysis capability.
/// </summary>
public interface IAgentTool
{
    /// <summary>
    /// Unique name used in the LLM function-calling protocol (e.g., "get_historical_decisions").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description sent to the LLM so it can decide when to call this tool.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON Schema for the tool's parameters (sent to the LLM as the function's parameter schema).
    /// </summary>
    AgentToolDefinition GetDefinition();

    /// <summary>
    /// Executes the tool with the given arguments and returns a string result for the LLM.
    /// </summary>
    /// <param name="arguments">The parsed JSON arguments from the LLM's function call.</param>
    /// <param name="context">Shared context providing database access and current state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A string result to feed back to the LLM in the next turn.</returns>
    Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken cancellationToken);
}
