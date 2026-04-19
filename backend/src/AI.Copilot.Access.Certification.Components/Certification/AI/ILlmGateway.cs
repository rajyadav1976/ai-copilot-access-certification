using AI.Copilot.Access.Certification.Components.Certification.AI.Agent;

namespace AI.Copilot.Access.Certification.Components.Certification.AI;

/// <summary>
/// Gateway to the LLM service (Azure OpenAI) for agentic AI recommendations.
/// </summary>
public interface ILlmGateway
{
    /// <summary>
    /// Sends a multi-turn conversation with tool definitions to the LLM (function-calling mode).
    /// The LLM can either respond with text (final answer) or request tool calls.
    /// </summary>
    /// <param name="messages">The conversation history (system, user, assistant, tool messages).</param>
    /// <param name="tools">The tool definitions the LLM may invoke.</param>
    /// <param name="maxTokens">Maximum tokens for the response.</param>
    /// <param name="temperature">Temperature for randomness control.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The LLM response, which may contain tool calls or a final text answer.</returns>
    Task<AgentLlmResponse> ChatWithToolsAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<AgentToolDefinition> tools,
        int maxTokens = 1000,
        double temperature = 0.1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">Text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vector as a float array.</returns>
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple texts in a single API call.
    /// More efficient than calling GetEmbeddingAsync individually for each text.
    /// </summary>
    /// <param name="texts">Texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of embedding vectors in the same order as the input texts.</returns>
    Task<IReadOnlyList<float[]>> GetEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}
