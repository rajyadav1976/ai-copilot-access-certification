namespace AI.Copilot.Access.Certification.Components.Certification.AI.Models;

/// <summary>
/// Aggregated context data for a single review item, consumed by the AI recommendation engine.
/// </summary>
public sealed class ReviewItemContext
{
    /// <summary>
    /// The workflow instance step ID (maps to ReviewItems.Id / V_CertificationReviewItems.StepId).
    /// </summary>
    public long StepId { get; set; }

    /// <summary>
    /// The certification process ID.
    /// </summary>
    public long CertificationProcessId { get; set; }

    /// <summary>
    /// User ID of the employee being reviewed.
    /// </summary>
    public long? UserId { get; set; }

    /// <summary>
    /// Role ID under review.
    /// </summary>
    public long? RoleId { get; set; }

    /// <summary>
    /// System ID associated with the role.
    /// </summary>
    public long? SystemId { get; set; }

    /// <summary>
    /// Employee full name.
    /// </summary>
    public string? EmployeeName { get; set; }

    /// <summary>
    /// Employee job title.
    /// </summary>
    public string? EmployeeJob { get; set; }

    /// <summary>
    /// Employee department.
    /// </summary>
    public string? EmployeeDepartment { get; set; }

    /// <summary>
    /// Employee's direct manager name.
    /// </summary>
    public string? EmployeeManagerName { get; set; }

    /// <summary>
    /// Name of the role being reviewed.
    /// </summary>
    public string? RoleName { get; set; }

    /// <summary>
    /// Description of the role.
    /// </summary>
    public string? RoleDescription { get; set; }

    /// <summary>
    /// SAP account / username.
    /// </summary>
    public string? Account { get; set; }

    /// <summary>
    /// Usage percentage of the role (0-100).
    /// </summary>
    public long UsagePercentage { get; set; }

    /// <summary>
    /// Number of used activities.
    /// </summary>
    public int UsedActivities { get; set; }

    /// <summary>
    /// Last date the role was used.
    /// </summary>
    public DateTimeOffset? LastUsed { get; set; }

    /// <summary>
    /// Number of days since the role was last used. Null if never used.
    /// </summary>
    public int? DaysSinceLastUsed { get; set; }

    /// <summary>
    /// Whether an SoD violation was detected.
    /// </summary>
    public bool HasSodViolation { get; set; }

    /// <summary>
    /// Details of SoD violations, if any.
    /// </summary>
    public List<string> SodViolationDetails { get; set; } = [];

    /// <summary>
    /// Percentage of peers with the same role.
    /// </summary>
    public decimal? PeerUsagePercent { get; set; }

    /// <summary>
    /// Historical approval decisions for this user-role combination in past campaigns.
    /// </summary>
    public List<HistoricalDecision> History { get; set; } = [];
}

/// <summary>
/// A historical approval/rejection decision from a previous campaign.
/// </summary>
public sealed class HistoricalDecision
{
    public long CertificationProcessId { get; set; }
    public string? Decision { get; set; }
    public DateTimeOffset? DecisionDate { get; set; }
    public string? Comments { get; set; }
}

/// <summary>
/// The result of an AI recommendation for a single review item.
/// </summary>
public sealed class AIRecommendationResult
{
    /// <summary>
    /// The step ID of the review item.
    /// </summary>
    public long StepId { get; set; }

    /// <summary>
    /// AI decision: "Approve", "Reject", or "NeedsReview".
    /// </summary>
    public string Decision { get; set; } = "NeedsReview";

    /// <summary>
    /// Confidence score 0.0 to 1.0.
    /// </summary>
    public decimal ConfidenceScore { get; set; }

    /// <summary>
    /// Risk level: "Low", "Medium", "High", "Critical".
    /// </summary>
    public string RiskLevel { get; set; } = "Medium";

    /// <summary>
    /// LLM-generated summary explaining the recommendation.
    /// </summary>
    public string? RiskSummary { get; set; }

    /// <summary>
    /// List of risk factors identified.
    /// </summary>
    public List<string> RiskFactors { get; set; } = [];

    /// <summary>
    /// Whether the role assignment is anomalous compared to peers.
    /// </summary>
    public bool IsAnomaly { get; set; }

    /// <summary>
    /// Anomaly score from embedding analysis.
    /// </summary>
    public decimal? AnomalyScore { get; set; }

    /// <summary>
    /// Tokens used in the LLM call.
    /// </summary>
    public int? TokensUsed { get; set; }

    /// <summary>
    /// Processing time in milliseconds.
    /// </summary>
    public int? ProcessingTimeMs { get; set; }
}

/// <summary>
/// Campaign-level statistics for AI recommendations.
/// </summary>
public sealed class AIRecommendationSummary
{
    public long CertificationProcessId { get; set; }
    public int TotalItems { get; set; }
    public int RecommendedApprove { get; set; }
    public int RecommendedReject { get; set; }
    public int NeedsReview { get; set; }
    public int HighRiskCount { get; set; }
    public int AnomalyCount { get; set; }
    public int SodViolationCount { get; set; }
    public decimal AverageConfidence { get; set; }
    public int FailedCount { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset? GeneratedAt { get; set; }
}

/// <summary>
/// API response DTO for a single AI recommendation.
/// </summary>
public sealed class AIRecommendationDto
{
    public Guid Id { get; set; }
    public long CertificationProcessId { get; set; }
    public long ReviewItemStepId { get; set; }
    public long? UserId { get; set; }
    public long? RoleId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public decimal ConfidenceScore { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public string? RiskSummary { get; set; }
    public List<string> RiskFactors { get; set; } = [];
    public long? UsagePercentage { get; set; }
    public int? DaysSinceLastUsed { get; set; }
    public bool HasSodViolation { get; set; }
    public decimal? PeerUsagePercent { get; set; }
    public bool IsAnomaly { get; set; }
    public decimal? AnomalyScore { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }

    // Display fields enriched from V_CertificationReviewItems
    public string? RoleName { get; set; }
    public string? RoleDescription { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeJob { get; set; }
    public string? EmployeeDepartment { get; set; }
    public string? Account { get; set; }
    public string? SystemName { get; set; }
}

/// <summary>
/// Request body for submitting feedback on an AI recommendation.
/// </summary>
public sealed class AIRecommendationFeedbackRequest
{
    public string ActualDecision { get; set; } = string.Empty;
    public bool AgreedWithAI { get; set; }
    public string? OverrideReason { get; set; }
    public string? FeedbackComments { get; set; }
    public int? QualityRating { get; set; }
}

/// <summary>
/// Request body for triggering AI recommendation generation.
/// </summary>
public sealed class GenerateRecommendationsRequest
{
    public bool ForceRegenerate { get; set; }
    public long[]? SpecificStepIds { get; set; }
}
