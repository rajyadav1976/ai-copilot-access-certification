namespace AI.Copilot.Access.Certification.Components.Certification.AI.Models;

/// <summary>
/// Aggregate feedback metrics for a given time window and optional filters.
/// Used by the Feedback Analytics Service (Learning Loop Layer 1).
/// </summary>
public sealed class FeedbackMetrics
{
    /// <summary>Total number of feedback entries in the window.</summary>
    public int TotalFeedbacks { get; set; }

    /// <summary>Fraction of feedbacks where the reviewer agreed with the AI (0.0–1.0).</summary>
    public decimal AgreementRate { get; set; }

    /// <summary>Average quality rating (1–5) across feedbacks that include a rating.</summary>
    public decimal AverageQualityRating { get; set; }

    /// <summary>Counts of override reasons provided when reviewers disagreed.</summary>
    public Dictionary<string, int> OverrideReasonCounts { get; set; } = new();

    /// <summary>Agreement rate broken down by AI decision type (Approve/Reject/NeedsReview).</summary>
    public Dictionary<string, decimal> AccuracyByDecision { get; set; } = new();

    /// <summary>Agreement rate broken down by risk level (Low/Medium/High/Critical).</summary>
    public Dictionary<string, decimal> AccuracyByRiskLevel { get; set; } = new();

    /// <summary>Number of days covered by this metrics window.</summary>
    public int WindowDays { get; set; }
}

/// <summary>
/// Performance comparison for a specific model + prompt version combination.
/// </summary>
public sealed class VersionPerformance
{
    public string PromptVersion { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public int FeedbackCount { get; set; }
    public decimal AgreementRate { get; set; }
    public double AverageQualityRating { get; set; }
}

/// <summary>
/// Report on concept drift — whether recent AI accuracy has degraded vs baseline.
/// </summary>
public sealed class DriftReport
{
    public decimal BaselineAgreementRate { get; set; }
    public decimal RecentAgreementRate { get; set; }
    public decimal DriftMagnitude { get; set; }
    public bool IsDrifting { get; set; }
    public int BaselineSampleCount { get; set; }
    public int RecentSampleCount { get; set; }
    public int RecentWindowDays { get; set; }
    public int BaselineWindowDays { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// A single feedback example retrieved for few-shot injection into LLM prompts.
/// </summary>
public sealed class FeedbackExample
{
    /// <summary>The role name from the original review item.</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>The usage percentage of the original review item.</summary>
    public int UsagePercentage { get; set; }

    /// <summary>The risk level assigned by the AI.</summary>
    public string RiskLevel { get; set; } = string.Empty;

    /// <summary>Whether the item had SoD violations.</summary>
    public bool HasSodViolation { get; set; }

    /// <summary>The AI's original decision.</summary>
    public string AiDecision { get; set; } = string.Empty;

    /// <summary>The AI's confidence score.</summary>
    public decimal AiConfidence { get; set; }

    /// <summary>The reviewer's actual decision.</summary>
    public string ReviewerDecision { get; set; } = string.Empty;

    /// <summary>Whether the reviewer agreed with the AI.</summary>
    public bool AgreedWithAI { get; set; }

    /// <summary>The reviewer's override reason (if they disagreed).</summary>
    public string? OverrideReason { get; set; }

    /// <summary>Quality rating given by the reviewer (1-5).</summary>
    public int? QualityRating { get; set; }
}
