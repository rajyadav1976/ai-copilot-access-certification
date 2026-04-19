using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using AI.Copilot.Access.Certification.Components.Certification.AI.Agent;
using AI.Copilot.Access.Certification.Platform.Attributes;

namespace AI.Copilot.Access.Certification.Components.Certification.AI;

/// <summary>
/// Azure OpenAI implementation of the LLM gateway for generating AI recommendations.
/// Supports chat completions with tool-calling (GPT-4o-mini) and embedding generation (text-embedding-3-small).
/// </summary>
[Component(typeof(ILlmGateway), ComponentType.Service)]
public class AzureOpenAILlmGateway : ILlmGateway
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AIServiceSettings _settings;
    private readonly ILogger<AzureOpenAILlmGateway> _logger;

    private const string HttpClientName = "AzureOpenAI";

    public AzureOpenAILlmGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<AIServiceSettings> settings,
        ILogger<AzureOpenAILlmGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    private const int MaxRetries = 3;
    private static readonly int[] RetryDelaysMs = [1000, 3000, 8000];

    /// <summary>
    /// Determines whether an HTTP status code is transient and should be retried.
    /// </summary>
    private static bool IsTransientError(global::System.Net.HttpStatusCode statusCode)
    {
        return statusCode is
            global::System.Net.HttpStatusCode.TooManyRequests or       // 429
            global::System.Net.HttpStatusCode.InternalServerError or   // 500
            global::System.Net.HttpStatusCode.BadGateway or            // 502
            global::System.Net.HttpStatusCode.ServiceUnavailable or    // 503
            global::System.Net.HttpStatusCode.GatewayTimeout;          // 504
    }

    /// <inheritdoc />
    public async Task<AgentLlmResponse> ChatWithToolsAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<AgentToolDefinition> tools,
        int maxTokens = 1000,
        double temperature = 0.0,
        CancellationToken cancellationToken = default)
    {
        var response = new AgentLlmResponse();

        try
        {
            var endpoint = _settings.Endpoint.TrimEnd('/');
            var apiKey = _settings.ApiKey;
            var deploymentName = !string.IsNullOrEmpty(_settings.ChatDeployment) ? _settings.ChatDeployment : "gpt-4o-mini";
            var apiVersion = !string.IsNullOrEmpty(_settings.ApiVersion) ? _settings.ApiVersion : "2024-08-01-preview";

            var url = $"{endpoint}/openai/deployments/{deploymentName}/chat/completions?api-version={apiVersion}";

            _logger.LogDebug(
                "ChatWithToolsAsync: endpoint={Endpoint}, deployment={Deployment}, url={Url}",
                endpoint, deploymentName, url);

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("Azure OpenAI API key is empty or not configured. Check AzureOpenAI:ApiKey in configuration.");
                response.IsSuccess = false;
                response.ErrorMessage = "API key is empty or not configured";
                return response;
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("api-key", apiKey);

            // Build serializable message list
            var messageList = messages.Select(BuildMessagePayload).ToList();

            // Build the HTTP payload with tools
            var payload = new Dictionary<string, object>
            {
                ["messages"] = messageList,
                ["max_tokens"] = maxTokens,
                ["temperature"] = temperature
            };

            if (tools.Count > 0)
            {
                payload["tools"] = tools.Select(t => new Dictionary<string, object>
                {
                    ["type"] = t.Type,
                    ["function"] = new Dictionary<string, object>
                    {
                        ["name"] = t.Function.Name,
                        ["description"] = t.Function.Description,
                        ["parameters"] = t.Function.Parameters
                    }
                }).ToList();
                payload["tool_choice"] = "auto";
            }
            else
            {
                // When no tools are specified (e.g., ForceFinalAnswer, JSON correction retry),
                // request structured JSON output to prevent the LLM from returning narrative text.
                payload["response_format"] = new Dictionary<string, string>
                {
                    ["type"] = "json_object"
                };
            }

            // Retry loop for transient errors (429, 500, 502, 503, 504)
            HttpResponseMessage? httpResponse = null;
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                httpResponse = await client.PostAsJsonAsync(url, payload, cancellationToken);

                if (httpResponse.IsSuccessStatusCode)
                    break;

                if (attempt < MaxRetries && IsTransientError(httpResponse.StatusCode))
                {
                    // Check for Retry-After header from Azure OpenAI
                    var retryAfter = httpResponse.Headers.RetryAfter;
                    var delayMs = retryAfter?.Delta != null
                        ? (int)retryAfter.Delta.Value.TotalMilliseconds
                        : RetryDelaysMs[attempt];

                    _logger.LogWarning(
                        "Azure OpenAI returned {StatusCode} on attempt {Attempt}/{MaxRetries}. " +
                        "Retrying in {DelayMs}ms...",
                        httpResponse.StatusCode, attempt + 1, MaxRetries + 1, delayMs);

                    await Task.Delay(delayMs, cancellationToken);
                    continue;
                }

                // Non-transient error or max retries exhausted
                break;
            }

            if (httpResponse == null || !httpResponse.IsSuccessStatusCode)
            {
                var errorBody = httpResponse != null
                    ? await httpResponse.Content.ReadAsStringAsync(cancellationToken)
                    : "No response";
                _logger.LogError("Azure OpenAI API error (tool-calling): {StatusCode} - {Body}",
                    httpResponse?.StatusCode, errorBody);
                response.IsSuccess = false;
                response.ErrorMessage = $"HTTP {(int)(httpResponse?.StatusCode ?? 0)} - {errorBody?.Substring(0, Math.Min(errorBody?.Length ?? 0, 500))}";
                return response;
            }

            var resultJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            // Parse usage
            if (root.TryGetProperty("usage", out var usage) &&
                usage.TryGetProperty("total_tokens", out var tokens))
            {
                response.TokensUsed = tokens.GetInt32();
            }

            // Parse choices
            if (root.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var choice = choices[0];

                if (choice.TryGetProperty("finish_reason", out var finishReason))
                {
                    response.FinishReason = finishReason.GetString() ?? "stop";
                }

                if (choice.TryGetProperty("message", out var message))
                {
                    // Parse content (may be null when tool_calls are present)
                    if (message.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String)
                    {
                        response.Content = content.GetString();
                    }

                    // Parse tool_calls array
                    if (message.TryGetProperty("tool_calls", out var toolCalls) &&
                        toolCalls.ValueKind == JsonValueKind.Array)
                    {
                        response.ToolCalls = new List<AgentToolCall>();
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            var toolCall = new AgentToolCall
                            {
                                Id = tc.GetProperty("id").GetString() ?? "",
                                Type = tc.GetProperty("type").GetString() ?? "function"
                            };

                            if (tc.TryGetProperty("function", out var fn))
                            {
                                toolCall.Function = new AgentToolCallFunction
                                {
                                    Name = fn.GetProperty("name").GetString() ?? "",
                                    Arguments = fn.GetProperty("arguments").GetString() ?? "{}"
                                };
                            }

                            response.ToolCalls.Add(toolCall);
                        }
                    }
                }
            }

            response.IsSuccess = true;

            _logger.LogDebug(
                "Tool-calling LLM response: finish_reason={FinishReason}, tool_calls={ToolCallCount}, tokens={Tokens}",
                response.FinishReason, response.ToolCalls?.Count ?? 0, response.TokensUsed);
        }
        catch (TaskCanceledException)
        {
            response.IsSuccess = false;
            response.ErrorMessage = "Request timed out or was cancelled";
            _logger.LogWarning("Azure OpenAI tool-calling request was cancelled");
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorMessage = $"Exception: {ex.Message}";
            _logger.LogError(ex, "Error calling Azure OpenAI for tool-calling chat");
        }

        return response;
    }

    /// <summary>
    /// Converts an AgentMessage into a dictionary suitable for JSON serialization
    /// matching the Azure OpenAI chat completions API message format.
    /// </summary>
    private static Dictionary<string, object?> BuildMessagePayload(AgentMessage msg)
    {
        var payload = new Dictionary<string, object?>
        {
            ["role"] = msg.Role,
            ["content"] = msg.Content
        };

        if (msg.ToolCallId != null)
        {
            payload["tool_call_id"] = msg.ToolCallId;
        }

        if (msg.ToolCalls is { Count: > 0 })
        {
            payload["tool_calls"] = msg.ToolCalls.Select(tc => new Dictionary<string, object>
            {
                ["id"] = tc.Id,
                ["type"] = tc.Type,
                ["function"] = new Dictionary<string, object>
                {
                    ["name"] = tc.Function.Name,
                    ["arguments"] = tc.Function.Arguments
                }
            }).ToList();
        }

        return payload;
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var batch = await GetEmbeddingsBatchAsync([text], cancellationToken);
        return batch.Count > 0 ? batch[0] : [];
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0) return [];

        try
        {
            var endpoint = _settings.Endpoint.TrimEnd('/');
            var apiKey = _settings.ApiKey;
            var deploymentName = !string.IsNullOrEmpty(_settings.EmbeddingDeployment) ? _settings.EmbeddingDeployment : "text-embedding-3-small";
            var apiVersion = !string.IsNullOrEmpty(_settings.ApiVersion) ? _settings.ApiVersion : "2024-08-01-preview";

            var url = $"{endpoint}/openai/deployments/{deploymentName}/embeddings?api-version={apiVersion}";

            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("api-key", apiKey);

            // Azure OpenAI embedding API accepts an array of strings
            var payload = new { input = texts, model = deploymentName };

            var httpResponse = await client.PostAsJsonAsync(url, payload, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            var result = await httpResponse.Content.ReadFromJsonAsync<EmbeddingResponse>(
                cancellationToken: cancellationToken);

            if (result?.Data is { Count: > 0 })
            {
                // Sort by index to maintain input order
                return result.Data
                    .OrderBy(d => d.Index)
                    .Select(d => d.Embedding)
                    .ToList();
            }

            _logger.LogWarning("No embedding data returned from Azure OpenAI for batch of {Count} texts", texts.Count);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting batch embeddings from Azure OpenAI for {Count} texts", texts.Count);
            return [];
        }
    }

    #region Azure OpenAI DTOs

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }
    }

    private sealed class EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }

    #endregion
}
