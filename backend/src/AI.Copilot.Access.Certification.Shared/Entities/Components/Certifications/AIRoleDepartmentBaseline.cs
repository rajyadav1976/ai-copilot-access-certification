using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using AI.Copilot.Access.Certification.Shared.Attributes;
using AI.Copilot.Access.Certification.Shared.Entities.Platform;

namespace AI.Copilot.Access.Certification.Shared.Entities.Components.Certifications;

/// <summary>
/// Stores embedding vectors and statistical baselines per department/role/system
/// for anomaly detection in AI-powered certification reviews.
/// </summary>
[DbEntity]
[Table("AIRoleDepartmentBaselines")]
[TableApiEntity("AIRoleDepartmentBaselines")]
[TableChangesTrackedEntity]
public class AIRoleDepartmentBaseline : BaseEntity, IFluentApiConfigurer
{
    /// <summary>
    /// Department name for this baseline.
    /// </summary>
    [Required]
    [StringLength(200)]
    [TableApiField]
    [TableChangesTrackedField]
    public string Department { get; set; } = null!;

    /// <summary>
    /// Role ID for this baseline.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public long RoleId { get; set; }

    /// <summary>
    /// Role name for this baseline.
    /// </summary>
    [Required]
    [StringLength(450)]
    [TableApiField]
    [TableChangesTrackedField]
    public string RoleName { get; set; } = null!;

    /// <summary>
    /// System ID for this baseline.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public long SystemId { get; set; }

    /// <summary>
    /// How many users in this department have this role.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public int AssignmentCount { get; set; }

    /// <summary>
    /// Average usage percentage for this role in this department.
    /// </summary>
    [TableApiField]
    [TableChangesTrackedField]
    [Column(TypeName = "decimal(5,2)")]
    public decimal? AvgUsagePercentage { get; set; }

    /// <summary>
    /// Historical approval rate for this role in campaigns (0.00 - 1.00).
    /// </summary>
    [TableApiField]
    [TableChangesTrackedField]
    [Column(TypeName = "decimal(5,2)")]
    public decimal? ApprovalRate { get; set; }

    /// <summary>
    /// Serialized float[] embedding vector from text-embedding-3-small for cosine similarity.
    /// </summary>
    [TableApiField]
    public byte[]? EmbeddingVector { get; set; }

    /// <summary>
    /// Timestamp when this baseline was computed.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public DateTimeOffset ComputedAt { get; set; }

    /// <summary>
    /// Number of samples used to compute this baseline.
    /// </summary>
    [Required]
    [TableApiField]
    [TableChangesTrackedField]
    public int SampleSize { get; set; }

    public void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AIRoleDepartmentBaseline>(entity =>
        {
            entity.HasIndex(e => new { e.Department, e.RoleId, e.SystemId })
                .IsUnique()
                .HasDatabaseName("UQ_AIBaseline_DeptRoleSystem");

            entity.HasIndex(e => e.Department)
                .HasDatabaseName("IX_AIBaseline_Department");
        });
    }
}
