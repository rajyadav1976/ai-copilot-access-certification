using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using AI.Copilot.Access.Certification.Shared.Attributes;
using AI.Copilot.Access.Certification.Shared.Entities.Platform;

namespace AI.Copilot.Access.Certification.Shared.Entities.Components.Certifications;

/// <summary>
/// Tracks async AI batch processing status per campaign.
/// Each record represents a single processing job for generating AI recommendations.
/// </summary>
[DbEntity]
[Table("AIProcessingJobs")]
[TableApiEntity("AIProcessingJobs")]
[TableChangesTrackedEntity]
public class AIProcessingJob : BaseEntity, IFluentApiConfigurer
{
    /// <summary>
    /// The certification process ID that this job processes.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public long CertificationProcessId { get; set; }

    /// <summary>
    /// Current job status: Queued, Processing, Completed, or Failed.
    /// </summary>
    [Required]
    [StringLength(50)]
    [TableApiField]
    [TableChangesTrackedField]
    public string Status { get; set; } = "Queued";

    /// <summary>
    /// Total number of review items to process.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public int TotalItems { get; set; }

    /// <summary>
    /// Number of items successfully processed so far.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public int ProcessedItems { get; set; }

    /// <summary>
    /// Number of items that failed processing.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public int FailedItems { get; set; }

    /// <summary>
    /// Timestamp when processing started.
    /// </summary>
    [TableApiField]
    [TableChangesTrackedField]
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Timestamp when processing completed (success or failure).
    /// </summary>
    [TableApiField]
    [TableChangesTrackedField]
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Error message if the job failed.
    /// </summary>
    [TableApiField]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The AI model version used for this processing job.
    /// </summary>
    [StringLength(100)]
    [TableApiField]
    [TableChangesTrackedField]
    public string? ModelVersion { get; set; }

    public void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AIProcessingJob>(entity =>
        {
            entity.HasIndex(e => e.CertificationProcessId)
                .HasDatabaseName("IX_AIProcessingJobs_Certification");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_AIProcessingJobs_Status");

            entity.HasIndex(e => new { e.CertificationProcessId, e.Status })
                .HasDatabaseName("IX_AIProcessingJobs_Certification_Status");
        });
    }
}
