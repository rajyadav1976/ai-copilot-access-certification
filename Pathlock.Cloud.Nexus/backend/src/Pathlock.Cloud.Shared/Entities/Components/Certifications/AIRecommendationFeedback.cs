using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Pathlock.Cloud.Shared.Attributes;
using Pathlock.Cloud.Shared.Entities.Platform;

namespace Pathlock.Cloud.Shared.Entities.Components.Certifications;

/// <summary>
/// Captures reviewer feedback on AI recommendations to enable continuous learning and accuracy improvement.
/// </summary>
[DbEntity]
[Table("AIRecommendationFeedbacks")]
[TableApiEntity("AIRecommendationFeedbacks")]
[TableChangesTrackedEntity]
public class AIRecommendationFeedback : BaseEntity, IFluentApiConfigurer
{
    /// <summary>
    /// Foreign key to the AIRecommendation this feedback relates to.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public Guid AIRecommendationId { get; set; }

    /// <summary>
    /// The user ID of the reviewer who provided feedback.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public Guid ReviewerUserId { get; set; }

    /// <summary>
    /// The actual decision the reviewer made: Approved or Rejected.
    /// </summary>
    [Required]
    [StringLength(20)]
    [TableApiField]
    [TableChangesTrackedField]
    public string ActualDecision { get; set; } = null!;

    /// <summary>
    /// Whether the reviewer agreed with the AI recommendation.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public bool AgreedWithAI { get; set; }

    /// <summary>
    /// Optional reason the reviewer overrode the AI recommendation.
    /// </summary>
    [StringLength(1000)]
    [TableApiField]
    [TableChangesTrackedField]
    public string? OverrideReason { get; set; }

    /// <summary>
    /// Reviewer's comments on the recommendation quality.
    /// </summary>
    [StringLength(2000)]
    [TableApiField]
    [TableChangesTrackedField]
    public string? FeedbackComments { get; set; }

    /// <summary>
    /// Rating given by the reviewer for the recommendation quality (1-5).
    /// </summary>
    [Range(1, 5)]
    [TableApiField]
    [TableChangesTrackedField]
    public int? QualityRating { get; set; }

    /// <summary>
    /// Timestamp when the feedback was submitted.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public DateTimeOffset FeedbackTimestamp { get; set; }

    /// <summary>
    /// Navigation property to the parent AI recommendation.
    /// </summary>
    [ForeignKey(nameof(AIRecommendationId))]
    [TableApiNavigation]
    public AIRecommendation? AIRecommendation { get; set; }

    public void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AIRecommendationFeedback>(entity =>
        {
            entity.HasIndex(e => e.AIRecommendationId)
                .HasDatabaseName("IX_AIRecommendationFeedbacks_Recommendation");

            entity.HasIndex(e => new { e.AIRecommendationId, e.ReviewerUserId })
                .IsUnique()
                .HasDatabaseName("IX_AIRecommendationFeedbacks_Recommendation_Reviewer");

            entity.HasOne(e => e.AIRecommendation)
                .WithMany(r => r.Feedbacks)
                .HasForeignKey(e => e.AIRecommendationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
