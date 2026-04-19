using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Pathlock.Cloud.Shared.Attributes;
using Pathlock.Cloud.Shared.Entities.Platform;

namespace Pathlock.Cloud.Shared.Entities.Components.Certifications;

/// <summary>
/// Captures reviewer agreement/disagreement with AI recommendations for model improvement.
/// This table is the primary feedback loop used to refine AI accuracy over time.
/// </summary>
[DbEntity]
[Table("AIAssistantFeedback")]
[TableApiEntity("AIAssistantFeedback")]
[TableChangesTrackedEntity]
public class AIAssistantFeedback : BaseEntity, IFluentApiConfigurer
{
    /// <summary>
    /// Foreign key to the AIRecommendation this feedback relates to.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public Guid AIRecommendationId { get; set; }

    /// <summary>
    /// The certification process ID for this review.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public long CertificationProcessId { get; set; }

    /// <summary>
    /// The workflow instance step ID that was reviewed.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public long WorkflowInstanceStepId { get; set; }

    /// <summary>
    /// AI recommendation at time of review (Approve/Reject/NeedsReview).
    /// </summary>
    [Required]
    [StringLength(50)]
    [TableApiField]
    [TableChangesTrackedField]
    public string AiRecommendation { get; set; } = null!;

    /// <summary>
    /// AI confidence score at time of review (0.00 - 1.00).
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    [Column(TypeName = "decimal(5,2)")]
    public decimal AiConfidenceScore { get; set; }

    /// <summary>
    /// AI risk score at time of review.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public int AiRiskScore { get; set; }

    /// <summary>
    /// The actual decision the reviewer made: 'Approve' or 'Reject'.
    /// </summary>
    [Required]
    [StringLength(50)]
    [TableApiField]
    [TableChangesTrackedField]
    public string ReviewerDecision { get; set; } = null!;

    /// <summary>
    /// Whether the reviewer agreed with the AI suggestion.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public bool ReviewerAgreedWithAI { get; set; }

    /// <summary>
    /// Optional comment from the reviewer explaining their decision.
    /// </summary>
    [TableApiField]
    [TableChangesTrackedField]
    public string? ReviewerComment { get; set; }

    /// <summary>
    /// The user ID of the reviewer who made the decision.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public Guid ReviewerUserId { get; set; }

    /// <summary>
    /// Display name of the reviewer.
    /// </summary>
    [StringLength(200)]
    [TableApiField]
    [TableChangesTrackedField]
    public string? ReviewerUserName { get; set; }

    /// <summary>
    /// Timestamp when the decision was made.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public DateTimeOffset DecisionTimestamp { get; set; }

    /// <summary>
    /// Time from page load to decision click in milliseconds.
    /// </summary>
    [TableApiField]
    [TableChangesTrackedField]
    public int? TimeToDecisionMs { get; set; }

    /// <summary>
    /// Navigation property to the parent AI recommendation.
    /// </summary>
    [ForeignKey(nameof(AIRecommendationId))]
    [TableApiNavigation]
    public AIRecommendation? Recommendation { get; set; }

    public void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AIAssistantFeedback>(entity =>
        {
            entity.HasIndex(e => new { e.AiRecommendation, e.ReviewerAgreedWithAI })
                .HasDatabaseName("IX_AIFeedback_Agreement");

            entity.HasIndex(e => e.AIRecommendationId)
                .HasDatabaseName("IX_AIFeedback_Recommendation");

            entity.HasIndex(e => e.CertificationProcessId)
                .HasDatabaseName("IX_AIFeedback_Certification");

            entity.HasOne(e => e.Recommendation)
                .WithMany()
                .HasForeignKey(e => e.AIRecommendationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
