using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Copilot.Access.Certification.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAITables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // -- AIRecommendations --
            migrationBuilder.CreateTable(
                name: "AIRecommendations",
                columns: table => new
                {
                    sys_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificationProcessId = table.Column<long>(type: "bigint", nullable: false),
                    ReviewItemStepId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    RoleId = table.Column<long>(type: "bigint", nullable: true),
                    SystemId = table.Column<long>(type: "bigint", nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    RiskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RiskSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RiskFactors = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UsagePercentage = table.Column<long>(type: "bigint", nullable: true),
                    DaysSinceLastUsed = table.Column<int>(type: "int", nullable: true),
                    HasSodViolation = table.Column<bool>(type: "bit", nullable: false),
                    PeerUsagePercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    IsAnomaly = table.Column<bool>(type: "bit", nullable: false),
                    AnomalyScore = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    ModelVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TokensUsed = table.Column<int>(type: "int", nullable: true),
                    ProcessingTimeMs = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIRecommendations", x => x.sys_id);
                });

            // -- AIRecommendationFeedbacks --
            migrationBuilder.CreateTable(
                name: "AIRecommendationFeedbacks",
                columns: table => new
                {
                    sys_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AIRecommendationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActualDecision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AgreedWithAI = table.Column<bool>(type: "bit", nullable: false),
                    OverrideReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FeedbackComments = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    QualityRating = table.Column<int>(type: "int", nullable: true),
                    FeedbackTimestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIRecommendationFeedbacks", x => x.sys_id);
                    table.ForeignKey(
                        name: "FK_AIRecommendationFeedbacks_AIRecommendations_AIRecommendationId",
                        column: x => x.AIRecommendationId,
                        principalTable: "AIRecommendations",
                        principalColumn: "sys_id",
                        onDelete: ReferentialAction.Cascade);
                });

            // -- AIAssistantFeedback --
            migrationBuilder.CreateTable(
                name: "AIAssistantFeedback",
                columns: table => new
                {
                    sys_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AIRecommendationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificationProcessId = table.Column<long>(type: "bigint", nullable: false),
                    WorkflowInstanceStepId = table.Column<long>(type: "bigint", nullable: false),
                    AiRecommendation = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AiConfidenceScore = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    AiRiskScore = table.Column<int>(type: "int", nullable: false),
                    ReviewerDecision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReviewerAgreedWithAI = table.Column<bool>(type: "bit", nullable: false),
                    ReviewerComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecisionTimestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TimeToDecisionMs = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIAssistantFeedback", x => x.sys_id);
                    table.ForeignKey(
                        name: "FK_AIAssistantFeedback_AIRecommendations_AIRecommendationId",
                        column: x => x.AIRecommendationId,
                        principalTable: "AIRecommendations",
                        principalColumn: "sys_id",
                        onDelete: ReferentialAction.Cascade);
                });

            // -- AIRoleDepartmentBaselines --
            migrationBuilder.CreateTable(
                name: "AIRoleDepartmentBaselines",
                columns: table => new
                {
                    sys_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Department = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SystemId = table.Column<long>(type: "bigint", nullable: false),
                    AssignmentCount = table.Column<int>(type: "int", nullable: false),
                    AvgUsagePercentage = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    ApprovalRate = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    EmbeddingVector = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SampleSize = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIRoleDepartmentBaselines", x => x.sys_id);
                });

            // -- AIProcessingJobs --
            migrationBuilder.CreateTable(
                name: "AIProcessingJobs",
                columns: table => new
                {
                    sys_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificationProcessId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TotalItems = table.Column<int>(type: "int", nullable: false),
                    ProcessedItems = table.Column<int>(type: "int", nullable: false),
                    FailedItems = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModelVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIProcessingJobs", x => x.sys_id);
                });

            // -- Indexes for AIRecommendations --
            migrationBuilder.CreateIndex(
                name: "IX_AIRecommendations_Campaign_Step",
                table: "AIRecommendations",
                columns: new[] { "CertificationProcessId", "ReviewItemStepId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIRecommendations_Campaign",
                table: "AIRecommendations",
                column: "CertificationProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_AIRecommendations_Campaign_Decision",
                table: "AIRecommendations",
                columns: new[] { "CertificationProcessId", "Decision" });

            migrationBuilder.CreateIndex(
                name: "IX_AIRecommendations_Status",
                table: "AIRecommendations",
                column: "Status");

            // -- Indexes for AIRecommendationFeedbacks --
            migrationBuilder.CreateIndex(
                name: "IX_AIRecommendationFeedbacks_Recommendation",
                table: "AIRecommendationFeedbacks",
                column: "AIRecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_AIRecommendationFeedbacks_Recommendation_Reviewer",
                table: "AIRecommendationFeedbacks",
                columns: new[] { "AIRecommendationId", "ReviewerUserId" },
                unique: true);

            // -- Indexes for AIAssistantFeedback --
            migrationBuilder.CreateIndex(
                name: "IX_AIFeedback_Agreement",
                table: "AIAssistantFeedback",
                columns: new[] { "AiRecommendation", "ReviewerAgreedWithAI" });

            migrationBuilder.CreateIndex(
                name: "IX_AIFeedback_Certification",
                table: "AIAssistantFeedback",
                column: "CertificationProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_AIFeedback_Recommendation",
                table: "AIAssistantFeedback",
                column: "AIRecommendationId");

            // -- Indexes for AIRoleDepartmentBaselines --
            migrationBuilder.CreateIndex(
                name: "UQ_AIBaseline_DeptRoleSystem",
                table: "AIRoleDepartmentBaselines",
                columns: new[] { "Department", "RoleId", "SystemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIBaseline_Department",
                table: "AIRoleDepartmentBaselines",
                column: "Department");

            // -- Indexes for AIProcessingJobs --
            migrationBuilder.CreateIndex(
                name: "IX_AIProcessingJobs_Certification",
                table: "AIProcessingJobs",
                column: "CertificationProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_AIProcessingJobs_Status",
                table: "AIProcessingJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AIProcessingJobs_Certification_Status",
                table: "AIProcessingJobs",
                columns: new[] { "CertificationProcessId", "Status" });

            // -- V_AIReviewItemContext view --
            // Only create the view if all referenced tables/views exist in this database.
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.V_CertificationReviewItems', 'V') IS NOT NULL
   AND OBJECT_ID('dbo.MetaDataForRoles') IS NOT NULL
   AND OBJECT_ID('dbo.L_RiskLevels') IS NOT NULL
   AND OBJECT_ID('dbo.SoxUserViolations', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.WorkflowInstances', 'U') IS NOT NULL
BEGIN
    EXEC('
    CREATE OR ALTER VIEW dbo.V_AIReviewItemContext AS
    WITH ViolationCounts AS (
        SELECT
            suv.UserId,
            COUNT(*) AS ActiveViolationCount,
            MAX(sfc.RiskLevel) AS MaxViolationRisk,
            STRING_AGG(sfc.Name, ''; '') AS ViolationNames
        FROM dbo.SoxUserViolations suv
        JOIN dbo.SoxForbiddenCombinations sfc ON sfc.Id = suv.ForbiddenCombinationId
        GROUP BY suv.UserId
    ),
    CertHistory AS (
        SELECT
            wi.UserId,
            war.RoleId,
            COUNT(*) AS PastReviewCount,
            SUM(CASE WHEN wis.IsApproved = 1 THEN 1 ELSE 0 END) AS PastApprovals,
            SUM(CASE WHEN wis.IsApproved = 0 THEN 1 ELSE 0 END) AS PastRejections,
            MAX(wis.StepEnd) AS LastReviewedOn,
            MAX(wis.HandledBy) AS LastReviewedBy
        FROM dbo.WorkflowInstances wi
        JOIN dbo.WorkflowInstanceSteps wis ON wis.WorkflowInstanceId = wi.Id
        JOIN dbo.WorkflowAuthorizationRequests war ON war.WorkflowInstanceId = wi.Id
        WHERE wis.StepEnd IS NOT NULL
        GROUP BY wi.UserId, war.RoleId
    )
    SELECT
        ri.Id                       AS ReviewItemId,
        ri.CertificationProcessId,
        ri.ApproverUserId,
        ri.UserId,
        ri.RoleId,
        ri.EmployeeName,
        ri.EmployeeDepartment,
        ri.EmployeeJob,
        ri.EmployeeManagerName,
        ri.RoleName,
        ri.RoleDescription,
        ri.Account,
        rl.Level                    AS RoleRiskLevel,
        ri.UsagePercentage,
        ri.UsedActivities,
        ri.LastUsed,
        uru.TotalActions,
        uru.LastUse                 AS RoleLastUse,
        ISNULL(vc.ActiveViolationCount, 0) AS ActiveViolationCount,
        vc.MaxViolationRisk,
        vc.ViolationNames,
        ISNULL(ch.PastReviewCount, 0)   AS PastReviewCount,
        ISNULL(ch.PastApprovals, 0)     AS PastApprovals,
        ISNULL(ch.PastRejections, 0)    AS PastRejections,
        ch.LastReviewedOn,
        ch.LastReviewedBy,
        ISNULL(bl.AssignmentCount, 0)   AS DeptRoleAssignmentCount,
        bl.AvgUsagePercentage           AS DeptAvgUsagePercentage,
        bl.ApprovalRate                 AS DeptHistoricalApprovalRate,
        ri.IsApproved,
        ri.Comments
    FROM dbo.V_CertificationReviewItems ri
    LEFT JOIN dbo.UserRoleUsages uru
        ON uru.UserId = ri.UserId AND uru.RoleName = ri.RoleName
    LEFT JOIN dbo.MetaDataForRoles mdr
        ON mdr.Id = ri.RoleId
    LEFT JOIN dbo.L_RiskLevels rl
        ON rl.Id = mdr.RiskLevelId
    LEFT JOIN ViolationCounts vc
        ON vc.UserId = ri.UserId
    LEFT JOIN CertHistory ch
        ON ch.UserId = ri.UserId AND ch.RoleId = ri.RoleId
    LEFT JOIN dbo.AIRoleDepartmentBaselines bl
        ON bl.Department = ri.EmployeeDepartment
        AND bl.RoleId = ri.RoleId
        AND bl.SystemId = ri.SystemId;
    ');
END
");

            // -- Fix Identity column: show Account when EmployeeName is null --
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.V_CertificationReviewItems', 'V') IS NOT NULL
BEGIN
    DECLARE @viewDef nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID('dbo.V_CertificationReviewItems'));
    IF @viewDef LIKE '%CE.FullName AS EmployeeName%'
    BEGIN
        SET @viewDef = REPLACE(@viewDef,
            ', CE.FullName AS EmployeeName',
            ', COALESCE(CE.FullName, VU.SapUserName) AS EmployeeName');
        SET @viewDef = REPLACE(@viewDef, 'CREATE VIEW', 'ALTER VIEW');
        EXEC sp_executesql @viewDef;
    END
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.V_AIReviewItemContext;");

            migrationBuilder.DropTable(name: "AIAssistantFeedback");
            migrationBuilder.DropTable(name: "AIRecommendationFeedbacks");
            migrationBuilder.DropTable(name: "AIProcessingJobs");
            migrationBuilder.DropTable(name: "AIRoleDepartmentBaselines");
            migrationBuilder.DropTable(name: "AIRecommendations");
        }
    }
}
