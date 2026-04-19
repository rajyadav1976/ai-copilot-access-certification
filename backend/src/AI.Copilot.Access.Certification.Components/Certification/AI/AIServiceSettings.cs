namespace AI.Copilot.Access.Certification.Components.Certification.AI;

/// <summary>
/// Configuration settings for the AI Recommendation service.
/// Bound from the "AIService" section in appsettings.json via IOptions&lt;AIServiceSettings&gt;.
/// </summary>
public class AIServiceSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "AIService";

    /// <summary>
    /// Whether AI recommendations feature is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Azure OpenAI endpoint URL (e.g., "https://my-instance.openai.azure.com/").
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure OpenAI API key. In production, should be sourced from Azure Key Vault.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Deployment name for the chat completion model (e.g., "gpt-4o-mini").
    /// </summary>
    public string ChatDeployment { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Deployment name for the embedding model (e.g., "text-embedding-3-small").
    /// </summary>
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Azure OpenAI API version (e.g., "2024-08-01-preview").
    /// </summary>
    public string ApiVersion { get; set; } = "2024-08-01-preview";

    /// <summary>
    /// Minimum confidence threshold (0.0 – 1.0) below which recommendations are marked as "Review".
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>
    /// Maximum number of review items per LLM request batch.
    /// </summary>
    public int MaxBatchSize { get; set; } = 50;

    /// <summary>
    /// Whether to anonymize employee names in LLM prompts (replace with IDs).
    /// </summary>
    public bool AnonymizePrompts { get; set; }

    /// <summary>
    /// Maximum number of historical decisions to include per user-role pair in the prompt.
    /// </summary>
    public int MaxHistoricalDecisions { get; set; } = 5;

    /// <summary>
    /// Time-to-live in hours for cached recommendations before they expire.
    /// Zero means no expiry.
    /// </summary>
    public int RecommendationTtlHours { get; set; }

    /// <summary>
    /// Maximum number of review items to process per campaign.
    /// For campaigns exceeding this cap, stratified sampling selects a representative subset.
    /// Set to 0 for no limit (not recommended for large campaigns).
    /// </summary>
    public int MaxItemsCap { get; set; } = 50;

    /// <summary>
    /// Maximum number of concurrent LLM chat completion calls.
    /// Controls parallelism to avoid overwhelming Azure OpenAI rate limits.
    /// </summary>
    public int MaxConcurrentLlmCalls { get; set; } = 5;

    /// <summary>
    /// Maximum number of texts to send in a single embedding API batch request.
    /// Azure OpenAI supports up to 2048 texts per batch.
    /// </summary>
    public int EmbeddingBatchSize { get; set; } = 50;

    // ───────────────────── Agentic AI Settings (UC1-UC6) ─────────────────────

    /// <summary>
    /// Maximum number of Plan→Act→Observe→Reflect iterations per review item.
    /// Prevents runaway agent loops. Typical values: 3-5.
    /// </summary>
    public int MaxAgentIterations { get; set; } = 3;

    /// <summary>
    /// Maximum tokens per agent turn (each LLM call in the loop).
    /// </summary>
    public int AgentMaxTokensPerTurn { get; set; } = 1000;

    /// <summary>
    /// Whether to run campaign-level pre-analysis (UC3) before per-item analysis.
    /// Produces CampaignInsights injected into each item's agent context.
    /// </summary>
    public bool EnableCampaignAnalysis { get; set; } = true;

    /// <summary>
    /// Whether to run post-hoc confidence calibration (UC4) against historical data.
    /// </summary>
    public bool EnableConfidenceCalibration { get; set; } = true;

    /// <summary>
    /// Whether to classify items into escalation tiers (UC6) for workflow routing.
    /// </summary>
    public bool EnableEscalation { get; set; } = true;

    /// <summary>
    /// Whether the interactive reviewer assistant (UC5) is enabled.
    /// </summary>
    public bool EnableReviewerAssistant { get; set; } = true;
}
