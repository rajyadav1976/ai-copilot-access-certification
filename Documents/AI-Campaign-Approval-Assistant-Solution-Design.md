# AI-Powered Campaign Approval Assistant — Solution Design Document

| **Document** | Solution Architecture & Design |
|---|---|
| **Project** | AI-Powered Campaign Approval Assistant |
| **Version** | 1.0 |
| **Date** | February 25, 2026 |
| **Status** | Draft |
| **Repository** | `Pathlock/pathlock-plc` |

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Problem Statement](#2-problem-statement)
3. [Goals & Success Metrics](#3-goals--success-metrics)
4. [High-Level Architecture](#4-high-level-architecture)
5. [Data Layer Design](#5-data-layer-design)
6. [AI Service Design](#6-ai-service-design)
7. [Backend Integration Design](#7-backend-integration-design)
8. [Frontend Integration Design](#8-frontend-integration-design)
9. [End-to-End Flow](#9-end-to-end-flow)
10. [Security & Privacy](#10-security--privacy)
11. [Implementation Phases](#11-implementation-phases)
12. [Risk Assessment & Mitigations](#12-risk-assessment--mitigations)
13. [Appendix A — Existing Data Model Reference](#appendix-a--existing-data-model-reference)
14. [Appendix B — Prompt Templates](#appendix-b--prompt-templates)

---

## 1. Executive Summary

This document describes the solution architecture for embedding an **LLM-based AI assistant** into Pathlock's authorization review (Campaign Review) workflow. The assistant will:

- **Ingest** campaign data (users, roles, SoD violations, historical decisions, usage patterns)
- **Generate** contextualized risk summaries in plain English
- **Recommend** Approve / Reject / Flag-for-Review decisions with confidence scores
- **Detect** anomalies (e.g., role assignments outside departmental norms)
- **Learn** from reviewer feedback to improve accuracy over time

The system is designed as an **advisory layer** — all final decisions remain with human reviewers. The AI operates as a pre-computation service that populates recommendations before the reviewer opens the campaign, reducing per-item cognitive load.

**Target impact:** 40–60% reduction in reviewer decision time, measurable improvement in approval accuracy, full auditability of AI vs. human decisions.

---

## 2. Problem Statement

### Current State

Campaign Review requires human reviewers to evaluate each user-role combination individually:

| Step | Per-item effort |
|---|---|
| Read employee profile (name, dept, job, manager) | Visual scan |
| Read role details (name, description, risk level) | Visual scan + comprehension |
| Check usage data (usage %, last used date) | Data interpretation |
| Expand SoD / UAR detail tabs (violations, history, peer analysis) | 2–4 clicks + reading |
| Recall historical decisions for similar items | Mental recall / no tool support |
| Click Approve or Reject radio | 1 click |
| Type rejection comment (mandatory in some configs) | Keystrokes |

For a campaign with **5,000 items across 200 groups**: ~1,200 clicks + hours of reading. No decision support beyond raw data display.

### Key Pain Points

1. **Every item starts as a blank slate** — no pre-suggested decision
2. **No risk summarization** — reviewer must mentally assemble risk picture from multiple tabs
3. **No anomaly surfacing** — unusual patterns are invisible unless reviewer notices them
4. **No cross-campaign learning** — past decisions on identical user-role pairs are buried in history tables
5. **Repetitive cognitive load** — appraising the same risk factors item after item

---

## 3. Goals & Success Metrics

| Goal | Metric | Target |
|---|---|---|
| Reduce decision time | Avg. seconds per review item (before vs. after) | 40–60% reduction |
| Improve accuracy | Rejection rate consistency (cross-reviewer variance) | ≤15% variance |
| Surface anomalies | % of flagged items that receive different decision than AI-absent baseline | ≥10% decision change rate on flagged items |
| Auditability | 100% of AI recommendations stored with reviewer response | 100% coverage |
| Reviewer trust | AI suggestion acceptance rate | ≥70% after 3 months |
| Feedback volume | Records in AIAssistantFeedback table | ≥1,000 labeled decisions within 6 months |

---

## 4. High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                            PATHLOCK PLATFORM                                    │
│                                                                                 │
│  ┌─────────────┐     ┌──────────────────┐     ┌─────────────────────────────┐  │
│  │  NEXUS UI    │     │  NEXUS API       │     │  NEXUS WORKER               │  │
│  │  (React SPA) │────▶│  (ASP.NET Core)  │────▶│  (Background Processor)     │  │
│  │              │     │                  │     │                             │  │
│  │  Components: │     │  Endpoints:      │     │  Handlers:                  │  │
│  │  • AiInsights│     │  • Table API     │     │  • AIGenerateHandler        │  │
│  │    Panel     │     │    /ReviewItems   │     │    [WorkerMessageHandler]   │  │
│  │  • AiRisk    │     │    /AiRecommend.  │     │                             │  │
│  │    Badge     │     │  • AI API        │     │  • CertTailOpHandler        │  │
│  │  • AiSummary │     │    /ai/recommend  │     │    (existing - extended)    │  │
│  │    Popover   │     │    /ai/feedback   │     │                             │  │
│  └──────┬──────┘     └────────┬─────────┘     └──────────┬──────────────────┘  │
│         │                     │                           │                     │
│  ┌──────┴──────┐     ┌───────┴──────────┐     ┌──────────┴──────────────────┐  │
│  │  PLC UI      │     │  PLC SERVICES    │     │  AI RECOMMENDATION SERVICE  │  │
│  │  (WebForms)  │     │  (C# BL)         │     │  (New - C# Service)         │  │
│  │              │     │                  │     │                             │  │
│  │  Screens:    │     │  Services:       │     │  Components:                │  │
│  │  • Review    │     │  • CampaignSvc   │     │  • DataContextAggregator    │  │
│  │    Screen    │     │  • IAIRecommSvc  │     │  • LlmGateway               │  │
│  │  • RoleDetail│     │  • CertificBO    │     │  • AnomalyDetector          │  │
│  │    V5 (tab)  │     │                  │     │  • RecommendationEngine     │  │
│  └─────────────┘     └──────────────────┘     │  • FeedbackCollector        │  │
│                                                └──────────┬──────────────────┘  │
│                                                           │                     │
│  ┌────────────────────────────────────────────────────────┴──────────────────┐  │
│  │                        SHARED SQL DATABASE                                │  │
│  │                                                                           │  │
│  │  Existing:                              New:                              │  │
│  │  • AuthoirizationCertifications         • AIRecommendations               │  │
│  │  • WorkflowInstances / Steps            • AIAssistantFeedback             │  │
│  │  • V_CertificationReviewItems           • AIRoleDepartmentBaselines       │  │
│  │  • CompanyEmployees                     • AIProcessingJobs                │  │
│  │  • SoxForbiddenCombinations             • V_AIReviewItemContext (view)    │  │
│  │  • SoxEntityViolations                                                    │  │
│  │  • UserRoleUsages / AllowedTransactions                                   │  │
│  │  • ApprovalRollupByGroup                                                  │  │
│  └───────────────────────────────────────────────────────────────────────────┘  │
│                                                           │                     │
│                                                           ▼                     │
│                                              ┌────────────────────────┐         │
│                                              │  AZURE OPENAI SERVICE  │         │
│                                              │  (External - Managed)  │         │
│                                              │                        │         │
│                                              │  • GPT-4o / GPT-4o-mini│         │
│                                              │  • text-embedding-3    │         │
│                                              │    -small              │         │
│                                              │  • Tenant-isolated     │         │
│                                              └────────────────────────┘         │
└─────────────────────────────────────────────────────────────────────────────────┘
```

### Architecture Principles

| Principle | Application |
|---|---|
| **Advisory, not autonomous** | AI never writes to `IsApproved`. Recommendations are stored in a separate table (`AIRecommendations`) |
| **Pre-computed, not on-demand** | AI processing runs asynchronously via Worker queue at campaign launch. Reviewers see cached results instantly |
| **Existing patterns only** | Uses `IHttpClientFactory`, `[WorkerMessageHandler]`, `ApproverFilteredRepository<T>`, YAML metadata, `FormTab type: coded` — all proven patterns in the codebase |
| **Tenant isolation** | Azure OpenAI with tenant-scoped data. No cross-tenant prompt leakage. `SessionContextMiddleware` enforced on all endpoints |
| **Full auditability** | Every AI recommendation + confidence + model version stored. Every reviewer response (agree/disagree) recorded. Immutable audit trail |

---

## 5. Data Layer Design

### 5.1 New Database Tables

#### 5.1.1 `AIRecommendations`

Stores pre-computed AI recommendations per review item.

```sql
CREATE TABLE dbo.AIRecommendations (
    Id                      BIGINT IDENTITY(1,1) PRIMARY KEY,
    CertificationProcessId  BIGINT NOT NULL,
    WorkflowInstanceStepId  BIGINT NOT NULL,
    UserId                  BIGINT NULL,
    RoleId                  BIGINT NULL,

    -- AI Outputs
    Recommendation          NVARCHAR(50)    NOT NULL,    -- 'Approve' | 'Reject' | 'Review'
    ConfidenceScore         DECIMAL(5,2)    NOT NULL,    -- 0.00 to 100.00
    RiskScore               INT             NOT NULL,    -- 0 to 100
    RiskSummary             NVARCHAR(MAX)   NOT NULL,    -- Plain-English risk summary
    AnomalyFlags            NVARCHAR(MAX)   NULL,        -- JSON array of anomaly descriptions
    AnomalyScore            DECIMAL(5,2)    NULL,        -- 0.00 to 100.00
    KeyFactors              NVARCHAR(MAX)   NULL,        -- JSON array of contributing factors

    -- Metadata
    ModelVersion            NVARCHAR(100)   NOT NULL,    -- e.g., 'gpt-4o-2024-08-06'
    PromptVersion           NVARCHAR(50)    NOT NULL,    -- e.g., 'v1.2'
    ProcessingTimeMs        INT             NULL,        -- LLM response time
    GeneratedAt             DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    ExpiresAt               DATETIME2       NULL,        -- Optional TTL

    -- Tenant
    CustomerId              INT             NOT NULL DEFAULT 1,

    CONSTRAINT FK_AIRecommendations_Certification
        FOREIGN KEY (CertificationProcessId)
        REFERENCES dbo.AuthoirizationCertifications(Id),

    CONSTRAINT FK_AIRecommendations_Step
        FOREIGN KEY (WorkflowInstanceStepId)
        REFERENCES dbo.WorkflowInstanceSteps(Id),

    CONSTRAINT CK_AIRecommendations_Recommendation
        CHECK (Recommendation IN ('Approve', 'Reject', 'Review'))
);

CREATE NONCLUSTERED INDEX IX_AIRecommendations_CertStep
    ON dbo.AIRecommendations (CertificationProcessId, WorkflowInstanceStepId);

CREATE NONCLUSTERED INDEX IX_AIRecommendations_Generated
    ON dbo.AIRecommendations (GeneratedAt DESC);
```

#### 5.1.2 `AIAssistantFeedback`

Captures reviewer agreement/disagreement with AI for model improvement.

```sql
CREATE TABLE dbo.AIAssistantFeedback (
    Id                      BIGINT IDENTITY(1,1) PRIMARY KEY,
    AIRecommendationId      BIGINT NOT NULL,
    CertificationProcessId  BIGINT NOT NULL,
    WorkflowInstanceStepId  BIGINT NOT NULL,

    -- AI suggestion at time of review
    AiRecommendation        NVARCHAR(50)    NOT NULL,
    AiConfidenceScore       DECIMAL(5,2)    NOT NULL,
    AiRiskScore             INT             NOT NULL,

    -- Reviewer decision
    ReviewerDecision        NVARCHAR(50)    NOT NULL,    -- 'Approve' | 'Reject'
    ReviewerAgreedWithAI    BIT             NOT NULL,    -- Computed: did reviewer follow suggestion?
    ReviewerComment         NVARCHAR(MAX)   NULL,
    ReviewerUserId          UNIQUEIDENTIFIER NOT NULL,
    ReviewerUserName        NVARCHAR(200)   NULL,

    -- Timestamps
    DecisionTimestamp       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    TimeToDecisionMs        INT             NULL,        -- Time from page load to decision click

    -- Tenant
    CustomerId              INT             NOT NULL DEFAULT 1,

    CONSTRAINT FK_AIFeedback_Recommendation
        FOREIGN KEY (AIRecommendationId)
        REFERENCES dbo.AIRecommendations(Id),

    CONSTRAINT FK_AIFeedback_Step
        FOREIGN KEY (WorkflowInstanceStepId)
        REFERENCES dbo.WorkflowInstanceSteps(Id)
);

CREATE NONCLUSTERED INDEX IX_AIFeedback_Agreement
    ON dbo.AIAssistantFeedback (AiRecommendation, ReviewerAgreedWithAI)
    INCLUDE (AiConfidenceScore);
```

#### 5.1.3 `AIRoleDepartmentBaselines`

Stores embedding vectors and statistical baselines for anomaly detection.

```sql
CREATE TABLE dbo.AIRoleDepartmentBaselines (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    Department          NVARCHAR(200)   NOT NULL,
    RoleId              BIGINT          NOT NULL,
    RoleName            NVARCHAR(450)   NOT NULL,
    SystemId            BIGINT          NOT NULL,

    -- Statistical baseline
    AssignmentCount     INT             NOT NULL,    -- How many users in dept have this role
    AvgUsagePercentage  DECIMAL(5,2)    NULL,
    ApprovalRate        DECIMAL(5,2)    NULL,        -- Historical % approved in campaigns

    -- Embedding (for cosine similarity)
    EmbeddingVector     VARBINARY(MAX)  NULL,        -- Serialized float[] from text-embedding-3-small

    -- Metadata
    ComputedAt          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    SampleSize          INT             NOT NULL,

    CONSTRAINT UQ_AIBaseline_DeptRoleSystem
        UNIQUE (Department, RoleId, SystemId)
);
```

#### 5.1.4 `AIProcessingJobs`

Tracks async AI batch processing status per campaign.

```sql
CREATE TABLE dbo.AIProcessingJobs (
    Id                      BIGINT IDENTITY(1,1) PRIMARY KEY,
    CertificationProcessId  BIGINT          NOT NULL,
    Status                  NVARCHAR(50)    NOT NULL,    -- 'Queued' | 'Processing' | 'Completed' | 'Failed'
    TotalItems              INT             NOT NULL,
    ProcessedItems          INT             NOT NULL DEFAULT 0,
    FailedItems             INT             NOT NULL DEFAULT 0,
    StartedAt               DATETIME2       NULL,
    CompletedAt             DATETIME2       NULL,
    ErrorMessage            NVARCHAR(MAX)   NULL,
    ModelVersion            NVARCHAR(100)   NULL,
    CustomerId              INT             NOT NULL DEFAULT 1,

    CONSTRAINT FK_AIJobs_Certification
        FOREIGN KEY (CertificationProcessId)
        REFERENCES dbo.AuthoirizationCertifications(Id)
);
```

### 5.2 New Database View

#### `V_AIReviewItemContext`

Aggregates all data needed for AI context per review item in a single query.

```sql
CREATE VIEW dbo.V_AIReviewItemContext AS
WITH Settings AS (
    SELECT
        MAX(CASE WHEN ap.PropertyName = 'LimitRoleUsageDataHistory'
            THEN TRY_CAST(ap.Value AS INT) END) AS LimitUsageDays
    FROM dbo.ApplicationParameters ap
),
ViolationCounts AS (
    SELECT
        sev.EntityId AS UserId,
        COUNT(*) AS ActiveViolationCount,
        MAX(sfc.RiskLevel) AS MaxViolationRisk,
        STRING_AGG(sfc.Name, '; ') AS ViolationNames
    FROM dbo.SoxEntityViolations sev
    JOIN dbo.SoxForbiddenCombinations sfc ON sfc.Id = sev.ForbiddenCombinationId
    GROUP BY sev.EntityId
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
    -- Core identifiers
    ri.Id                       AS ReviewItemId,
    ri.CertificationProcessId,
    ri.ApproverUserId,
    ri.UserId,
    ri.RoleId,

    -- Employee context
    ri.EmployeeName,
    ri.EmployeeDepartment,
    ri.EmployeeJob,
    ri.EmployeeManagerName,

    -- Role context
    ri.RoleName,
    ri.RoleDescription,
    ri.Account,
    rl.Level                    AS RoleRiskLevel,

    -- Usage data
    ri.UsagePercentage,
    ri.UsedActivities,
    ri.LastUsed,
    uru.TotalActions,
    uru.LastUse                 AS RoleLastUse,

    -- SoD violations
    ISNULL(vc.ActiveViolationCount, 0) AS ActiveViolationCount,
    vc.MaxViolationRisk,
    vc.ViolationNames,

    -- Historical decisions
    ISNULL(ch.PastReviewCount, 0)   AS PastReviewCount,
    ISNULL(ch.PastApprovals, 0)     AS PastApprovals,
    ISNULL(ch.PastRejections, 0)    AS PastRejections,
    ch.LastReviewedOn,
    ch.LastReviewedBy,

    -- Baseline comparison
    ISNULL(bl.AssignmentCount, 0)   AS DeptRoleAssignmentCount,
    bl.AvgUsagePercentage           AS DeptAvgUsagePercentage,
    bl.ApprovalRate                 AS DeptHistoricalApprovalRate,

    -- Current state
    ri.IsApproved,
    ri.Comments

FROM dbo.V_CertificationReviewItems ri
LEFT JOIN dbo.V_Users vu
    ON vu.UserId = ri.UserId AND vu.SystemId = ri.SystemId
LEFT JOIN dbo.UserRoleUsages uru
    ON uru.UserId = ri.UserId AND uru.RoleName = ri.RoleName
LEFT JOIN dbo.MetaDataForRole mdr
    ON mdr.RoleId = ri.RoleId
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
```

### 5.3 EF Core Migration

Following the existing migration naming convention (`{yyyyMMddHHmmss}_{Name}.cs`):

**File:** `AI.Copilot.Access.Certification/backend/src/AI.Copilot.Access.Certification.Migrations/Migrations/{timestamp}_AddAIRecommendationTables.cs`

```csharp
public partial class AddAIRecommendationTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AIRecommendations",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CertificationProcessId = table.Column<long>(nullable: false),
                WorkflowInstanceStepId = table.Column<long>(nullable: false),
                UserId = table.Column<long>(nullable: true),
                RoleId = table.Column<long>(nullable: true),
                Recommendation = table.Column<string>(maxLength: 50, nullable: false),
                ConfidenceScore = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                RiskScore = table.Column<int>(nullable: false),
                RiskSummary = table.Column<string>(nullable: false),
                AnomalyFlags = table.Column<string>(nullable: true),
                AnomalyScore = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                KeyFactors = table.Column<string>(nullable: true),
                ModelVersion = table.Column<string>(maxLength: 100, nullable: false),
                PromptVersion = table.Column<string>(maxLength: 50, nullable: false),
                ProcessingTimeMs = table.Column<int>(nullable: true),
                GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()"),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CustomerId = table.Column<int>(nullable: false, defaultValue: 1)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AIRecommendations", x => x.Id);
                table.ForeignKey("FK_AIRecommendations_Certification",
                    x => x.CertificationProcessId,
                    "AuthoirizationCertifications", "Id");
            });

        migrationBuilder.CreateIndex("IX_AIRecommendations_CertStep",
            "AIRecommendations",
            new[] { "CertificationProcessId", "WorkflowInstanceStepId" });

        // AIAssistantFeedback, AIRoleDepartmentBaselines, AIProcessingJobs
        // follow the same pattern...
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AIProcessingJobs");
        migrationBuilder.DropTable("AIRoleDepartmentBaselines");
        migrationBuilder.DropTable("AIAssistantFeedback");
        migrationBuilder.DropTable("AIRecommendations");
    }
}
```

### 5.4 EF Core Entity Definitions

#### `AIRecommendation.cs`

**File:** `AI.Copilot.Access.Certification/backend/src/AI.Copilot.Access.Certification.Shared/Entities/Components/AI/AIRecommendation.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AI.Copilot.Access.Certification.Shared.Entities.Components.Certifications;
using AI.Copilot.Access.Certification.Shared.Platform.TableApi;

namespace AI.Copilot.Access.Certification.Shared.Entities.Components.AI;

[DbEntity]
[Table("AIRecommendations")]
[TableApiEntity("AIRecommendations")]
public class AIRecommendation : IApproverFiltered
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Column("CertificationProcessId")]
    [TableApiField]
    public long CertificationProcessId { get; set; }

    [Column("WorkflowInstanceStepId")]
    [TableApiField]
    public long WorkflowInstanceStepId { get; set; }

    [Column("UserId")]
    [TableApiField]
    public long? UserId { get; set; }

    [Column("RoleId")]
    [TableApiField]
    public long? RoleId { get; set; }

    [Column("Recommendation")]
    [MaxLength(50)]
    [TableApiField]
    public string Recommendation { get; set; } = string.Empty;

    [Column("ConfidenceScore")]
    [TableApiField]
    public decimal ConfidenceScore { get; set; }

    [Column("RiskScore")]
    [TableApiField]
    public int RiskScore { get; set; }

    [Column("RiskSummary")]
    [TableApiField]
    public string RiskSummary { get; set; } = string.Empty;

    [Column("AnomalyFlags")]
    [TableApiField]
    public string? AnomalyFlags { get; set; }

    [Column("AnomalyScore")]
    [TableApiField]
    public decimal? AnomalyScore { get; set; }

    [Column("KeyFactors")]
    [TableApiField]
    public string? KeyFactors { get; set; }

    [Column("ModelVersion")]
    [MaxLength(100)]
    [TableApiField]
    public string ModelVersion { get; set; } = string.Empty;

    [Column("GeneratedAt")]
    [TableApiField]
    public DateTime GeneratedAt { get; set; }

    // IApproverFiltered — inherits from the linked ReviewItem's campaign
    [NotMapped]
    public Guid ApproverUserId { get; set; }

    // Navigation
    [TableApiNavigation]
    [ForeignKey(nameof(CertificationProcessId))]
    public MyCertifications? Certification { get; set; }
}
```

---

## 6. AI Service Design

### 6.1 Service Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                  AI RECOMMENDATION SERVICE                          │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  IAIRecommendationService (Interface)                        │   │
│  │                                                              │   │
│  │  + GenerateRecommendationsAsync(campaignId) : Task           │   │
│  │  + GetRecommendation(stepId) : AIRecommendation?             │   │
│  │  + GetRecommendations(campaignId) : List<AIRecommendation>   │   │
│  │  + RecordFeedback(feedback) : Task                           │   │
│  │  + GetProcessingStatus(campaignId) : AIProcessingJob?        │   │
│  │  + RefreshBaselines() : Task                                 │   │
│  └──────────────────┬───────────────────────────────────────────┘   │
│                     │                                               │
│  ┌──────────────────┴───────────────────────────────────────────┐   │
│  │  AIRecommendationService (Implementation)                    │   │
│  │                                                              │   │
│  │  Dependencies (injected via DI):                             │   │
│  │  ├── IDataContextAggregator                                  │   │
│  │  ├── ILlmGateway                                             │   │
│  │  ├── IAnomalyDetector                                        │   │
│  │  ├── IRecommendationEngine                                   │   │
│  │  └── IDatabaseService                                        │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                     │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐ │
│  │ DataContext      │  │ LlmGateway      │  │ AnomalyDetector     │ │
│  │ Aggregator       │  │                 │  │                     │ │
│  │                  │  │ • Azure OpenAI   │  │ • Embedding gen.   │ │
│  │ • Query V_AI..   │  │   HttpClient    │  │ • Cosine distance  │ │
│  │   ReviewItem     │  │ • Prompt builder│  │ • Statistical       │ │
│  │   Context view   │  │ • Token mgmt   │  │   outlier detect.  │ │
│  │ • Batch items    │  │ • Retry policy  │  │ • Baseline compare │ │
│  │ • Enrich with    │  │ • Rate limiting │  │                     │ │
│  │   violation data │  │                 │  │                     │ │
│  └─────────────────┘  └─────────────────┘  └─────────────────────┘ │
│                                                                     │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  RecommendationEngine                                         │  │
│  │                                                               │  │
│  │  Combines LLM + Anomaly outputs into final recommendation:   │  │
│  │  • Parse LLM response → structured recommendation            │  │
│  │  • Merge anomaly score into risk score                        │  │
│  │  • Compute confidence based on (data completeness + LLM      │  │
│  │    certainty + historical agreement rate)                     │  │
│  │  • Apply override rules (e.g., if anomaly > 80 → always      │  │
│  │    "Review" regardless of LLM suggestion)                    │  │
│  └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

### 6.2 Component Specifications

#### 6.2.1 `IDataContextAggregator`

```csharp
namespace AI.Copilot.Access.Certification.Services.AI;

public interface IDataContextAggregator
{
    /// <summary>
    /// Retrieves enriched context for all review items in a campaign.
    /// Queries V_AIReviewItemContext + SoD violations + change documents.
    /// </summary>
    Task<List<ReviewItemContext>> GetCampaignContextAsync(long campaignId);

    /// <summary>
    /// Retrieves enriched context for a single review item.
    /// </summary>
    Task<ReviewItemContext> GetItemContextAsync(long workflowInstanceStepId);
}

public class ReviewItemContext
{
    // Identity
    public long ReviewItemId { get; set; }
    public long CertificationProcessId { get; set; }
    public long? UserId { get; set; }
    public long? RoleId { get; set; }

    // Employee
    public string EmployeeName { get; set; }
    public string EmployeeDepartment { get; set; }
    public string EmployeeJob { get; set; }
    public string EmployeeManagerName { get; set; }

    // Role
    public string RoleName { get; set; }
    public string RoleDescription { get; set; }
    public string RoleRiskLevel { get; set; }  // "Critical", "High", "Medium", "Low"

    // Usage
    public long UsagePercentage { get; set; }
    public int UsedActivities { get; set; }
    public DateTime? LastUsed { get; set; }

    // SoD
    public int ActiveViolationCount { get; set; }
    public string ViolationNames { get; set; }
    public int? MaxViolationRisk { get; set; }

    // History
    public int PastReviewCount { get; set; }
    public int PastApprovals { get; set; }
    public int PastRejections { get; set; }
    public DateTime? LastReviewedOn { get; set; }
    public string LastReviewedBy { get; set; }

    // Baseline
    public int DeptRoleAssignmentCount { get; set; }
    public decimal? DeptAvgUsagePercentage { get; set; }
    public decimal? DeptHistoricalApprovalRate { get; set; }
}
```

#### 6.2.2 `ILlmGateway`

```csharp
namespace AI.Copilot.Access.Certification.Services.AI;

public interface ILlmGateway
{
    /// <summary>
    /// Sends a batch of review items to the LLM for risk summarization
    /// and recommendation. Items are batched (10-20 per prompt) to
    /// optimize token usage and reduce API calls.
    /// </summary>
    Task<List<LlmRecommendationResult>> GenerateRecommendationsAsync(
        List<ReviewItemContext> items,
        CancellationToken cancellationToken = default);
}

public class LlmRecommendationResult
{
    public long ReviewItemId { get; set; }
    public string Recommendation { get; set; }      // "Approve" | "Reject" | "Review"
    public decimal ConfidenceScore { get; set; }     // 0 - 100
    public int RiskScore { get; set; }               // 0 - 100
    public string RiskSummary { get; set; }          // Plain-English summary
    public string[] KeyFactors { get; set; }         // Contributing factors
    public int ProcessingTimeMs { get; set; }
}
```

#### 6.2.3 `IAnomalyDetector`

```csharp
namespace AI.Copilot.Access.Certification.Services.AI;

public interface IAnomalyDetector
{
    /// <summary>
    /// Detects anomalies by comparing each item against department-role baselines.
    /// Uses embedding cosine distance + statistical outlier detection.
    /// </summary>
    Task<List<AnomalyResult>> DetectAnomaliesAsync(
        List<ReviewItemContext> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes baseline embeddings and statistics from historical data.
    /// Should be run periodically (e.g., weekly or after each campaign closes).
    /// </summary>
    Task RefreshBaselinesAsync(CancellationToken cancellationToken = default);
}

public class AnomalyResult
{
    public long ReviewItemId { get; set; }
    public decimal AnomalyScore { get; set; }        // 0 - 100 (higher = more anomalous)
    public string[] AnomalyFlags { get; set; }       // Human-readable descriptions
    // Examples:
    //   "Role outside department norm; 0 of 45 peers in Finance have this role"
    //   "Usage at 2% vs. department average of 78% — potential unused access"
    //   "No review precedent in 24 months for this user-role combination"
}
```

### 6.3 LLM Gateway Implementation

```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace AI.Copilot.Access.Certification.Services.AI;

public class AzureOpenAILlmGateway : ILlmGateway
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AIServiceSettings _settings;
    private readonly ILogger<AzureOpenAILlmGateway> _logger;

    private const int BATCH_SIZE = 15;  // Items per LLM call
    private const string PROMPT_VERSION = "v1.0";

    public AzureOpenAILlmGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<AIServiceSettings> settings,
        ILogger<AzureOpenAILlmGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<List<LlmRecommendationResult>> GenerateRecommendationsAsync(
        List<ReviewItemContext> items,
        CancellationToken cancellationToken = default)
    {
        var results = new List<LlmRecommendationResult>();
        var batches = items.Chunk(BATCH_SIZE);

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();
            var prompt = BuildPrompt(batch);
            var response = await CallAzureOpenAIAsync(prompt, cancellationToken);
            stopwatch.Stop();

            var batchResults = ParseResponse(response, batch, stopwatch.ElapsedMilliseconds);
            results.AddRange(batchResults);
        }

        return results;
    }

    private string BuildPrompt(ReviewItemContext[] batch)
    {
        // See Appendix B for full prompt templates
        var itemsJson = JsonSerializer.Serialize(batch.Select(item => new
        {
            id = item.ReviewItemId,
            employee = new { item.EmployeeName, item.EmployeeDepartment, item.EmployeeJob, item.EmployeeManagerName },
            role = new { item.RoleName, item.RoleDescription, item.RoleRiskLevel },
            usage = new { item.UsagePercentage, item.UsedActivities, item.LastUsed },
            sod = new { item.ActiveViolationCount, item.ViolationNames, item.MaxViolationRisk },
            history = new { item.PastReviewCount, item.PastApprovals, item.PastRejections,
                           item.LastReviewedOn, item.LastReviewedBy },
            baseline = new { item.DeptRoleAssignmentCount, item.DeptAvgUsagePercentage,
                            item.DeptHistoricalApprovalRate }
        }));

        return PromptTemplates.RiskRecommendation.Replace("{{ITEMS}}", itemsJson);
    }

    private async Task<string> CallAzureOpenAIAsync(
        string prompt, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("AzureOpenAI");

        var requestBody = new
        {
            messages = new[]
            {
                new { role = "system", content = PromptTemplates.SystemPrompt },
                new { role = "user",   content = prompt }
            },
            temperature = 0.1,          // Low temperature for consistent outputs
            max_tokens = 4096,
            response_format = new { type = "json_object" }
        };

        var response = await client.PostAsJsonAsync(
            $"openai/deployments/{_settings.DeploymentName}/chat/completions?api-version=2024-08-01-preview",
            requestBody,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AzureOpenAIResponse>(
            cancellationToken: cancellationToken);

        return result.Choices[0].Message.Content;
    }
}
```

### 6.4 Configuration

```csharp
namespace AI.Copilot.Access.Certification.Services.AI;

public class AIServiceSettings
{
    /// <summary>Azure OpenAI endpoint (e.g., https://{resource}.openai.azure.com/)</summary>
    public string Endpoint { get; set; }

    /// <summary>Azure OpenAI API key (stored in Azure Key Vault)</summary>
    public string ApiKey { get; set; }

    /// <summary>Deployment name (e.g., "gpt-4o" or "gpt-4o-mini")</summary>
    public string DeploymentName { get; set; } = "gpt-4o-mini";

    /// <summary>Embedding model deployment name</summary>
    public string EmbeddingDeploymentName { get; set; } = "text-embedding-3-small";

    /// <summary>Max items per LLM batch call</summary>
    public int BatchSize { get; set; } = 15;

    /// <summary>Enable/disable AI recommendations globally</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Confidence threshold below which recommendation shows as "Review" instead</summary>
    public decimal MinConfidenceThreshold { get; set; } = 40.0m;

    /// <summary>Anomaly score threshold above which item is always flagged for manual review</summary>
    public decimal AnomalyFlagThreshold { get; set; } = 75.0m;

    /// <summary>Max concurrent LLM API calls</summary>
    public int MaxConcurrency { get; set; } = 5;

    /// <summary>Retry count for transient LLM failures</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Recommendation expiry in hours (re-generate if older)</summary>
    public int RecommendationExpiryHours { get; set; } = 168; // 7 days
}
```

**Registration** (in `appsettings.json` / Azure Key Vault):

```json
{
  "AIService": {
    "Endpoint": "https://pathlock-ai.openai.azure.com/",
    "ApiKey": "{{from-keyvault}}",
    "DeploymentName": "gpt-4o-mini",
    "EmbeddingDeploymentName": "text-embedding-3-small",
    "Enabled": true,
    "BatchSize": 15,
    "MaxConcurrency": 5
  }
}
```

**DI Registration:**

```csharp
// In service registration (following existing IHttpClientFactory pattern)
services.AddHttpClient("AzureOpenAI", (sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<AIServiceSettings>>().Value;
    client.BaseAddress = new Uri(settings.Endpoint);
    client.DefaultRequestHeaders.Add("api-key", settings.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(60);
});

services.Configure<AIServiceSettings>(configuration.GetSection("AIService"));
services.AddSingleton<ILlmGateway, AzureOpenAILlmGateway>();
services.AddSingleton<IAnomalyDetector, EmbeddingAnomalyDetector>();
services.AddSingleton<IDataContextAggregator, SqlDataContextAggregator>();
services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
services.AddSingleton<IAIRecommendationService, AIRecommendationService>();
```

---

## 7. Backend Integration Design

### 7.1 Worker Message Handler (Nexus)

Triggered when a campaign is created or when an admin manually requests AI analysis.

**File:** `AI.Copilot.Access.Certification/backend/src/AI.Copilot.Access.Certification.Worker/App/AI/AIGenerateRecommendationsHandler.cs`

```csharp
using AI.Copilot.Access.Certification.Services.AI;
using AI.Copilot.Access.Certification.Shared.Legacy.Messaging;
using AI.Copilot.Access.Certification.Worker.Attributes;

namespace AI.Copilot.Access.Certification.Worker.App.AI;

[WorkerMessageHandler]
public class AIGenerateRecommendationsHandler
    : IMessageHandler<AIGenerateRecommendationsMessage>
{
    private readonly IAIRecommendationService _aiService;
    private readonly ILogger<AIGenerateRecommendationsHandler> _logger;

    public AIGenerateRecommendationsHandler(
        IAIRecommendationService aiService,
        ILogger<AIGenerateRecommendationsHandler> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<BaseMessage> HandleMessage(
        AIGenerateRecommendationsMessage message)
    {
        _logger.LogInformation(
            "Starting AI recommendation generation for campaign {CampaignId}",
            message.CertificationProcessId);

        try
        {
            await _aiService.GenerateRecommendationsAsync(
                message.CertificationProcessId);

            _logger.LogInformation(
                "Completed AI recommendations for campaign {CampaignId}",
                message.CertificationProcessId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to generate AI recommendations for campaign {CampaignId}",
                message.CertificationProcessId);
        }

        return null;
    }
}
```

**Message definition:**

```csharp
namespace AI.Copilot.Access.Certification.Shared.Legacy.Messaging.Messages.AI;

public class AIGenerateRecommendationsMessage : BaseMessage
{
    public long CertificationProcessId { get; set; }
    public bool ForceRegenerate { get; set; } = false;
}
```

### 7.2 Trigger Point — Campaign Creation Hook

**File:** `Bl/AuthoirizationCertificationBO.cs` — extend `CalculateStatistics`:

```csharp
public static void CalculateStatistics(ProfileTailorDataClassesDataContext db, long id)
{
    try
    {
        PlcTelemetryEngine.LogMethodTiming(() =>
        {
            db.Calculate_AuthoirizationCertificationStatistics(id,
                CommonSettings.Default.AuthorizationReviewRiskWeightForCritical,
                CommonSettings.Default.AuthorizationReviewRiskWeightForNonCritical);
            db.sp_MaterializeApprovalExclusionsByUser(id, false);
            db.sp_MaterializeApprovalRollupByGroup(id, false);

            // NEW: Queue AI recommendation generation
            if (CommonSettings.Default.AIRecommendationsEnabled)
            {
                QueueAIRecommendationGeneration(id);
            }

        }, new KeyValuePair<string, string>(
            WorkerProcessConstants.CertificationProcessId, id.ToString()));
    }
    catch (Exception ex)
    {
        Logger.SaveExceptionToLog(ex);
    }
}

private static void QueueAIRecommendationGeneration(long certificationProcessId)
{
    try
    {
        var message = new AIGenerateRecommendationsMessage
        {
            CertificationProcessId = certificationProcessId
        };
        MessageQueueClient.Send(message);
    }
    catch (Exception ex)
    {
        // Non-blocking: AI failure must not break campaign flow
        Logger.SaveExceptionToLog(ex);
    }
}
```

### 7.3 AI Recommendation API Endpoints

**File:** `AI.Copilot.Access.Certification/backend/src/AI.Copilot.Access.Certification.Api/Controllers/AIRecommendationController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using AI.Copilot.Access.Certification.Services.AI;

namespace AI.Copilot.Access.Certification.Api.Controllers;

[ApiController]
[Route("api/v1/ai")]
public class AIRecommendationController : ControllerBase
{
    private readonly IAIRecommendationService _aiService;

    public AIRecommendationController(IAIRecommendationService aiService)
    {
        _aiService = aiService;
    }

    /// <summary>
    /// Get AI recommendations for all items in a campaign.
    /// </summary>
    [HttpGet("recommendations/{campaignId:long}")]
    public async Task<ActionResult<List<AIRecommendationDto>>> GetRecommendations(
        long campaignId)
    {
        var results = await _aiService.GetRecommendationsAsync(campaignId);
        return Ok(results);
    }

    /// <summary>
    /// Get AI recommendation for a single review item.
    /// </summary>
    [HttpGet("recommendations/{campaignId:long}/item/{stepId:long}")]
    public async Task<ActionResult<AIRecommendationDto>> GetItemRecommendation(
        long campaignId, long stepId)
    {
        var result = await _aiService.GetRecommendationAsync(stepId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Record reviewer feedback on an AI recommendation.
    /// </summary>
    [HttpPost("feedback")]
    public async Task<ActionResult> RecordFeedback(
        [FromBody] AIFeedbackRequest request)
    {
        await _aiService.RecordFeedbackAsync(request);
        return Ok();
    }

    /// <summary>
    /// Get AI processing status for a campaign.
    /// </summary>
    [HttpGet("status/{campaignId:long}")]
    public async Task<ActionResult<AIProcessingStatusDto>> GetProcessingStatus(
        long campaignId)
    {
        var status = await _aiService.GetProcessingStatusAsync(campaignId);
        if (status == null) return NotFound();
        return Ok(status);
    }

    /// <summary>
    /// Manually trigger AI recommendation generation for a campaign.
    /// </summary>
    [HttpPost("generate/{campaignId:long}")]
    public async Task<ActionResult> TriggerGeneration(long campaignId)
    {
        await _aiService.QueueGenerationAsync(campaignId);
        return Accepted();
    }
}
```

### 7.4 Nexus Repository (Table API Integration)

For the `AIRecommendations` table to appear in the generic Table API alongside `ReviewItems`:

**File:** `AI.Copilot.Access.Certification/backend/src/AI.Copilot.Access.Certification.Components/Certification/AIRecommendationRepository.cs`

```csharp
using AI.Copilot.Access.Certification.Shared.Entities.Components.AI;
using AI.Copilot.Access.Certification.Shared.Platform.Components;
using AI.Copilot.Access.Certification.Shared.Platform.Session;
using AI.Copilot.Access.Certification.Shared.Platform.TableApi;

namespace AI.Copilot.Access.Certification.Components.Certification;

public interface IAIRecommendationRepository : IRepository<AIRecommendation> { }

[Component(typeof(IAIRecommendationRepository), ComponentType.Repository)]
public class AIRecommendationRepository
    : ApproverFilteredRepository<AIRecommendation>, IAIRecommendationRepository
{
    public AIRecommendationRepository(ISessionContext sessionContext)
        : base(sessionContext) { }
}
```

### 7.5 Extending `V_CertificationReviewItems` (Optional — Inline AI Columns)

To surface AI data directly in the existing ReviewItems grid without a separate API call, extend the SQL view:

```sql
-- Add to V_CertificationReviewItems view definition
LEFT JOIN dbo.AIRecommendations air
    ON air.WorkflowInstanceStepId = S.Id
    AND air.CertificationProcessId = I.CertificationProcessId
```

Add columns:

```sql
air.Recommendation      AS AiRecommendation,
air.ConfidenceScore     AS AiConfidenceScore,
air.RiskScore           AS AiRiskScore,
air.RiskSummary         AS AiRiskSummary,
air.AnomalyFlags        AS AiAnomalyFlags
```

And extend the `ReviewItems.cs` entity with matching properties.

---

## 8. Frontend Integration Design

### 8.1 Nexus Frontend (React)

#### 8.1.1 YAML Metadata Changes

**File:** `review_items.yaml` — Add AI fields to entity definition:

```yaml
# Add under fields: section
- name: AiRecommendation
  type: string
  label: AI Rec.
  description: AI-generated recommendation
  variant: badge
  filterable: true
  sortable: true
  visible: true

- name: AiRiskScore
  type: numberrange
  label: AI Risk
  description: AI-computed risk score (0-100)
  variant: badge
  filterable: true
  sortable: true
  visible: true
  ranges:
    - { from: 0, to: 30, color: green, label: Low }
    - { from: 31, to: 60, color: orange, label: Medium }
    - { from: 61, to: 85, color: red, label: High }
    - { from: 86, to: 100, color: red, label: Critical }

- name: AiConfidenceScore
  type: numberrange
  label: Confidence
  description: AI confidence in the recommendation
  filterable: true
  sortable: true
  visible: false
  ranges:
    - { from: 0, to: 40, color: gray, label: Low }
    - { from: 41, to: 70, color: orange, label: Medium }
    - { from: 71, to: 100, color: green, label: High }

- name: AiRiskSummary
  type: string
  label: AI Risk Summary
  description: Plain-English risk assessment
  visible: false
```

**`ReviewItemsList` listView** — Add columns:

```yaml
listViews:
  ReviewItemsList:
    columns:
      # ... existing columns ...
      - { name: AiRecommendation, label: AI Rec., columnWidth: 110 }
      - { name: AiRiskScore, label: AI Risk, columnWidth: 100 }
```

**`my_certifications.yaml`** — Add AI Insights tab:

```yaml
formViews:
  MyCertificationsForm:
    label: "{{{title}}}"
    description: "List of Certification items to Review"
    entity: MyCertifications
    default: true
    tabs:
      - type: list
        name: ReviewItems
        label: Certification Items
        view: ReviewItemsList
        reference:
          entity: ReviewItems
          referenceField: Certification
      - type: coded
        name: AiInsights
        label: AI Insights
        component: AiCampaignInsightsPanel
        order: 200
        params:
          certificationId: "{id}"
```

#### 8.1.2 New React Component — `AiCampaignInsightsPanel`

**File:** `AI.Copilot.Access.Certification/frontend/src/applications/portal/components/AiCampaignInsightsPanel.tsx`

```tsx
import React from 'react';
import { useQuery } from '@tanstack/react-query';
import { httpClient } from '@/platform/utils/api/httpClient';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/platform/components/ui/popover';
import { Brain, AlertTriangle, CheckCircle, XCircle, Loader2 } from 'lucide-react';

interface AIRecommendation {
  reviewItemId: number;
  recommendation: 'Approve' | 'Reject' | 'Review';
  confidenceScore: number;
  riskScore: number;
  riskSummary: string;
  anomalyFlags: string[] | null;
  anomalyScore: number | null;
  keyFactors: string[] | null;
}

interface AIProcessingStatus {
  status: 'Queued' | 'Processing' | 'Completed' | 'Failed';
  totalItems: number;
  processedItems: number;
}

interface Props {
  certificationId: string;
}

export function AiCampaignInsightsPanel({ certificationId }: Props) {
  const { data: status } = useQuery({
    queryKey: ['ai-status', certificationId],
    queryFn: () => httpClient.get<AIProcessingStatus>(
      `/api/v1/ai/status/${certificationId}`
    ),
    refetchInterval: (data) =>
      data?.status === 'Processing' ? 5000 : false,
  });

  const { data: recommendations } = useQuery({
    queryKey: ['ai-recommendations', certificationId],
    queryFn: () => httpClient.get<AIRecommendation[]>(
      `/api/v1/ai/recommendations/${certificationId}`
    ),
    enabled: status?.status === 'Completed',
  });

  if (!status || status.status === 'Queued') {
    return <StatusBanner icon={<Loader2 className="animate-spin" />}
      text="AI analysis queued..." />;
  }

  if (status.status === 'Processing') {
    return (
      <StatusBanner
        icon={<Brain className="animate-pulse text-purple-600" />}
        text={`Analyzing ${status.processedItems}/${status.totalItems} items...`}
      />
    );
  }

  if (status.status === 'Failed') {
    return <StatusBanner icon={<AlertTriangle className="text-red-500" />}
      text="AI analysis failed. Review items manually." />;
  }

  if (!recommendations?.length) return null;

  const summary = computeSummary(recommendations);

  return (
    <div className="p-6 space-y-6">
      {/* Campaign-level AI summary */}
      <div className="grid grid-cols-4 gap-4">
        <SummaryCard label="Recommend Approve" value={summary.approveCount}
          color="green" icon={<CheckCircle />} />
        <SummaryCard label="Recommend Reject" value={summary.rejectCount}
          color="red" icon={<XCircle />} />
        <SummaryCard label="Flag for Review" value={summary.reviewCount}
          color="orange" icon={<AlertTriangle />} />
        <SummaryCard label="Anomalies Detected" value={summary.anomalyCount}
          color="purple" icon={<Brain />} />
      </div>

      {/* Anomaly highlights */}
      {summary.topAnomalies.length > 0 && (
        <div className="border rounded-lg p-4">
          <h3 className="font-semibold mb-3">Top Anomalies</h3>
          {summary.topAnomalies.map((item, idx) => (
            <AnomalyRow key={idx} item={item} />
          ))}
        </div>
      )}

      {/* High-risk items needing attention */}
      {summary.highRiskItems.length > 0 && (
        <div className="border rounded-lg p-4">
          <h3 className="font-semibold mb-3">High-Risk Items</h3>
          {summary.highRiskItems.map((item, idx) => (
            <RiskRow key={idx} item={item} />
          ))}
        </div>
      )}
    </div>
  );
}
```

#### 8.1.3 New React Component — `AiRecommendationBadge`

Inline badge for each row in the ReviewItems grid:

**File:** `AI.Copilot.Access.Certification/frontend/src/applications/portal/components/AiRecommendationBadge.tsx`

```tsx
import React from 'react';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/platform/components/ui/popover';

interface Props {
  recommendation: 'Approve' | 'Reject' | 'Review';
  confidenceScore: number;
  riskSummary: string;
  anomalyFlags: string[] | null;
}

const colorMap = {
  Approve: 'bg-green-100 text-green-800',
  Reject:  'bg-red-100 text-red-800',
  Review:  'bg-amber-100 text-amber-800',
};

export function AiRecommendationBadge({
  recommendation, confidenceScore, riskSummary, anomalyFlags
}: Props) {
  return (
    <Popover>
      <PopoverTrigger asChild>
        <button className={`px-2 py-1 rounded-full text-xs font-medium
          cursor-pointer ${colorMap[recommendation]}`}>
          {recommendation} ({confidenceScore}%)
        </button>
      </PopoverTrigger>
      <PopoverContent className="w-80 p-4">
        <h4 className="font-semibold mb-2">AI Risk Summary</h4>
        <p className="text-sm text-gray-700 mb-3">{riskSummary}</p>
        {anomalyFlags?.length > 0 && (
          <div>
            <h5 className="text-xs font-semibold text-amber-700 mb-1">
              Anomalies Detected
            </h5>
            <ul className="text-xs text-gray-600 list-disc pl-4">
              {anomalyFlags.map((flag, i) => (
                <li key={i}>{flag}</li>
              ))}
            </ul>
          </div>
        )}
      </PopoverContent>
    </Popover>
  );
}
```

### 8.2 PLC Frontend (ASP.NET WebForms)

#### 8.2.1 ReviewScreen.aspx — Right-Side AI Panel

**Injection point:** After the `.inbox_items.column.main` div, activate the existing `.column.content` CSS slot.

```aspx
<%-- Add after the closing </div> of inbox_items at ~line 601 --%>

<div class="inbox_ai_insights column content" id="aiInsightsPanel"
     runat="server" visible="false">
    <asp:UpdatePanel ID="upAIInsights" runat="server"
                     UpdateMode="Conditional">
        <ContentTemplate>
            <div class="panel-header">
                <img src="~/Pictures/ai-brain.svg" alt="" />
                <span>AI Recommendation</span>
            </div>

            <div class="ai-recommendation-badge" id="divAIBadge" runat="server">
                <asp:Label ID="lblAIRecommendation" runat="server"
                           CssClass="ai-badge" />
                <asp:Label ID="lblAIConfidence" runat="server"
                           CssClass="ai-confidence" />
            </div>

            <div class="ai-risk-summary">
                <h4>Risk Summary</h4>
                <asp:Literal ID="litRiskSummary" runat="server" />
            </div>

            <div class="ai-anomalies" id="divAnomalies" runat="server"
                 visible="false">
                <h4>⚠ Anomalies Detected</h4>
                <asp:BulletedList ID="blAnomalyFlags" runat="server"
                                  BulletStyle="Disc" />
            </div>

            <div class="ai-key-factors">
                <h4>Key Factors</h4>
                <asp:BulletedList ID="blKeyFactors" runat="server"
                                  BulletStyle="Numbered" />
            </div>

            <div class="ai-feedback">
                <small>Was this helpful?</small>
                <asp:LinkButton ID="lnkAIHelpful" runat="server"
                    OnClick="AIFeedback_Click" CommandArgument="yes"
                    Text="👍 Yes" CssClass="ai-feedback-btn" />
                <asp:LinkButton ID="lnkAINotHelpful" runat="server"
                    OnClick="AIFeedback_Click" CommandArgument="no"
                    Text="👎 No" CssClass="ai-feedback-btn" />
            </div>
        </ContentTemplate>
    </asp:UpdatePanel>
</div>
```

**CSS adjustments:**

```css
/* Adjust column widths when AI panel is active */
body.aiEnabled .campaignItemReview .column.side    { width: 15%; }
body.aiEnabled .campaignItemReview .column.main    { width: 53%; }
body.aiEnabled .campaignItemReview .column.content { width: 30%; display: block; }

.ai-recommendation-badge .ai-badge {
    display: inline-block;
    padding: 4px 12px;
    border-radius: 12px;
    font-weight: bold;
    font-size: 14px;
}
.ai-badge.approve { background: #dcfce7; color: #166534; }
.ai-badge.reject  { background: #fee2e2; color: #991b1b; }
.ai-badge.review  { background: #fef3c7; color: #92400e; }
```

#### 8.2.2 Code-Behind — Load AI Recommendation Per Row

```csharp
// In ReviewScreen.aspx.cs — add to the row selection event handler
private void LoadAIRecommendation(long workflowInstanceStepId)
{
    if (!CommonSettings.Default.AIRecommendationsEnabled) return;

    try
    {
        var aiService = ServiceLocator.Resolve<IAIRecommendationService>();
        var recommendation = aiService.GetRecommendation(workflowInstanceStepId);

        if (recommendation != null)
        {
            aiInsightsPanel.Visible = true;
            lblAIRecommendation.Text = recommendation.Recommendation;
            lblAIRecommendation.CssClass = "ai-badge " +
                recommendation.Recommendation.ToLower();
            lblAIConfidence.Text = $"{recommendation.ConfidenceScore:F0}% confidence";
            litRiskSummary.Text = recommendation.RiskSummary;

            if (!string.IsNullOrEmpty(recommendation.AnomalyFlags))
            {
                divAnomalies.Visible = true;
                var flags = JsonConvert.DeserializeObject<string[]>(
                    recommendation.AnomalyFlags);
                blAnomalyFlags.DataSource = flags;
                blAnomalyFlags.DataBind();
            }

            if (!string.IsNullOrEmpty(recommendation.KeyFactors))
            {
                var factors = JsonConvert.DeserializeObject<string[]>(
                    recommendation.KeyFactors);
                blKeyFactors.DataSource = factors;
                blKeyFactors.DataBind();
            }

            upAIInsights.Update();
        }
    }
    catch (Exception ex)
    {
        Logger.SaveExceptionToLog(ex);
        // Silently fail — AI panel just stays hidden
    }
}
```

#### 8.2.3 RoleDetailsV5.ascx — New AI Insights Tab

**Injection point:** After `TabPanel4` (Peer Analysis), before `</ajax:TabContainer>`.

```aspx
<ajax:TabPanel ID="TabPanelAIInsights" runat="server">
    <HeaderTemplate>AI Insights</HeaderTemplate>
    <ContentTemplate>
        <asp:UpdatePanel ID="upAIInsightsTab" runat="server">
            <ContentTemplate>
                <div class="ai-insights-detail-tab">
                    <div class="ai-summary-section">
                        <h4>Risk Assessment</h4>
                        <asp:Literal ID="litAIDetailSummary" runat="server" />
                    </div>
                    <div class="ai-history-section">
                        <h4>Historical Pattern</h4>
                        <asp:Label ID="lblPastDecisions" runat="server" />
                        <asp:GridView ID="gvPastReviews" runat="server"
                            AutoGenerateColumns="false" CssClass="data-grid">
                            <Columns>
                                <asp:BoundField DataField="Campaign"
                                    HeaderText="Campaign" />
                                <asp:BoundField DataField="Decision"
                                    HeaderText="Decision" />
                                <asp:BoundField DataField="ReviewedBy"
                                    HeaderText="Reviewed By" />
                                <asp:BoundField DataField="ReviewedOn"
                                    HeaderText="Date" DataFormatString="{0:d}" />
                                <asp:BoundField DataField="Comment"
                                    HeaderText="Comment" />
                            </Columns>
                        </asp:GridView>
                    </div>
                    <div class="ai-anomaly-section" id="divAIAnomalyDetail"
                         runat="server" visible="false">
                        <h4>⚠ Anomaly Analysis</h4>
                        <asp:Literal ID="litAnomalyDetail" runat="server" />
                    </div>
                </div>
            </ContentTemplate>
        </asp:UpdatePanel>
    </ContentTemplate>
</ajax:TabPanel>
```

#### 8.2.4 Per-Row AI Badge in Review Grid

In `ReadApproval()` at `ReviewScreen.aspx.cs` line 1593, inject an AI badge alongside the Approve/Reject radios:

```csharp
// Inside ReadApproval method — add before the approve radio button
if (CommonSettings.Default.AIRecommendationsEnabled && aiRecommendation != null)
{
    var badgeColor = aiRecommendation.Recommendation switch
    {
        "Approve" => "#dcfce7",
        "Reject"  => "#fee2e2",
        _         => "#fef3c7"
    };
    var badgeText = aiRecommendation.Recommendation switch
    {
        "Approve" => "✓ AI: Approve",
        "Reject"  => "✗ AI: Reject",
        _         => "⚠ AI: Review"
    };

    writer.Write($@"<span class='ai-inline-badge'
        style='background:{badgeColor}; padding:2px 6px; border-radius:8px;
        font-size:11px; margin-right:8px;'
        title='{HttpUtility.HtmlAttributeEncode(aiRecommendation.RiskSummary)}'
        >{badgeText} ({aiRecommendation.ConfidenceScore:F0}%)</span>");
}
```

### 8.3 Feedback Capture — Integration with Save/Submit

#### Nexus (React)

Hook into the existing Accept/Reject UI actions defined in `review_items.yaml`:

```typescript
// In the Accept/Reject action handler, after the main action completes:
async function recordAIFeedback(
  stepId: number,
  campaignId: number,
  reviewerDecision: 'Approve' | 'Reject',
  aiRecommendation: AIRecommendation | null
) {
  if (!aiRecommendation) return;

  await httpClient.post('/api/v1/ai/feedback', {
    aiRecommendationId: aiRecommendation.id,
    certificationProcessId: campaignId,
    workflowInstanceStepId: stepId,
    aiRecommendation: aiRecommendation.recommendation,
    aiConfidenceScore: aiRecommendation.confidenceScore,
    aiRiskScore: aiRecommendation.riskScore,
    reviewerDecision,
    reviewerAgreedWithAI: reviewerDecision ===
      (aiRecommendation.recommendation === 'Review'
        ? reviewerDecision  // "Review" always counts as agreement
        : aiRecommendation.recommendation),
  });
}
```

#### PLC (WebForms)

Hook into `Save_click` at `ReviewScreen.aspx.cs` line 1640:

```csharp
// After UpdateItemsToUpdate() processes each item:
if (CommonSettings.Default.AIRecommendationsEnabled)
{
    Task.Run(() =>
    {
        try
        {
            var aiService = ServiceLocator.Resolve<IAIRecommendationService>();
            foreach (var item in processedItems)
            {
                aiService.RecordFeedbackAsync(new AIFeedbackRequest
                {
                    WorkflowInstanceStepId = item.StepId,
                    CertificationProcessId = _certificationProcessId,
                    ReviewerDecision = item.IsApproved ? "Approve" : "Reject",
                    ReviewerComment = item.Remarks
                });
            }
        }
        catch (Exception ex)
        {
            Logger.SaveExceptionToLog(ex);
        }
    });
}
```

---

## 9. End-to-End Flow

```
┌──────────┐    ┌──────────────┐    ┌──────────────┐    ┌───────────────┐
│ Campaign  │    │  Worker      │    │ Azure OpenAI │    │ Database      │
│ Manager   │    │  Queue       │    │              │    │               │
└─────┬─────┘    └──────┬───────┘    └──────┬───────┘    └───────┬───────┘
      │                 │                    │                    │
      │  1. Create Campaign                  │                    │
      │─────────────────────────────────────────────────────────▶│
      │                 │                    │          Create    │
      │                 │                    │       WorkflowInst.│
      │                 │                    │                    │
      │  2. CalculateStatistics triggers                         │
      │     AI message  │                    │                    │
      │────────────────▶│                    │                    │
      │                 │                    │                    │
      │           3. Handler wakes           │                    │
      │                 │  Query V_AI...     │                    │
      │                 │───────────────────────────────────────▶│
      │                 │◀──────────────────────────────────────│
      │                 │  Context data                          │
      │                 │                    │                    │
      │           4. Batch items to LLM      │                    │
      │                 │───────────────────▶│                    │
      │                 │  Risk summaries +  │                    │
      │                 │◀──────────────────│                    │
      │                 │  recommendations    │                    │
      │                 │                    │                    │
      │           5. Generate embeddings     │                    │
      │                 │───────────────────▶│                    │
      │                 │◀──────────────────│                    │
      │                 │  Anomaly scores    │                    │
      │                 │                    │                    │
      │           6. Store results           │                    │
      │                 │───────────────────────────────────────▶│
      │                 │                    │         INSERT     │
      │                 │                    │     AIRecommendations
      │                 │                    │                    │

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

┌──────────┐    ┌──────────────┐    ┌──────────────┐    ┌───────────────┐
│ Reviewer  │    │  Nexus API / │    │ AI Reco.     │    │ Database      │
│ (Human)   │    │  PLC App     │    │ Table        │    │               │
└─────┬─────┘    └──────┬───────┘    └──────┬───────┘    └───────┬───────┘
      │                 │                    │                    │
      │  7. Open campaign review             │                    │
      │────────────────▶│                    │                    │
      │                 │  Query ReviewItems + AIRecommendations  │
      │                 │───────────────────────────────────────▶│
      │                 │◀──────────────────────────────────────│
      │◀────────────────│                    │                    │
      │  Grid with AI badges, risk scores    │                    │
      │                 │                    │                    │
      │  8. Click item → see AI panel        │                    │
      │────────────────▶│                    │                    │
      │◀────────────────│ Risk summary +     │                    │
      │                  anomalies + history  │                    │
      │                 │                    │                    │
      │  9. Accept/Reject (agree/disagree w/ AI)                 │
      │────────────────▶│                    │                    │
      │                 │  Update IsApproved + INSERT Feedback    │
      │                 │───────────────────────────────────────▶│
      │                 │                    │                    │
      │  10. Continue to next item            │                    │
      │    (AI pre-filled = faster decisions) │                    │
```

**Step-by-step detail:**

| Step | Actor | Action | System Component |
|---|---|---|---|
| 1 | Campaign Manager | Creates campaign via StartProceses.aspx or API | `AuthoirizationCertificationBO` creates `WorkflowInstances` |
| 2 | System | `CalculateStatistics()` completes → queues `AIGenerateRecommendationsMessage` | `AuthoirizationCertificationBO.cs` line 822 |
| 3 | Worker | `AIGenerateRecommendationsHandler` fetches context via `V_AIReviewItemContext` | Data Context Aggregator |
| 4 | Worker | Batches 15 items per prompt → sends to Azure OpenAI → receives JSON recommendations | `AzureOpenAILlmGateway` |
| 5 | Worker | Generates embeddings for anomaly detection → compares with baselines | `EmbeddingAnomalyDetector` |
| 6 | Worker | Stores all results in `AIRecommendations` table, updates `AIProcessingJobs` status | `AIRecommendationService` |
| 7 | Reviewer | Opens campaign → ReviewItems grid now shows AI columns (risk score, recommendation badge) | `V_CertificationReviewItems` + JOIN to `AIRecommendations` |
| 8 | Reviewer | Clicks item → AI Insights panel shows risk summary, anomalies, historical pattern | `AIRecommendationController` or in-line data |
| 9 | Reviewer | Clicks Approve/Reject → system records decision + captures AI feedback | `AIAssistantFeedback` table |
| 10 | Reviewer | Moves to next item — AI pre-suggestion reduces scan-to-decide time | Repeat from step 8 |

---

## 10. Security & Privacy

### 10.1 Data Flow Security

| Concern | Control |
|---|---|
| **Data sent to LLM** | Use **Azure OpenAI** (data stays in designated Azure region, not used for model training). Prompts contain business data only (role names, departments, usage %)  — no passwords, no SSNs, no raw credentials |
| **PII minimization** | Prompts use employee names + departments (necessary for context). Can be anonymized to IDs if required by policy — configurable via `AIServiceSettings.AnonymizePrompts` |
| **Tenant isolation** | `SessionContextMiddleware` enforces tenant scoping on all API calls. `AIRecommendation` table includes `CustomerId`. Queries always filter by tenant |
| **API key security** | Azure OpenAI API key stored in **Azure Key Vault**, injected via `IOptions<AIServiceSettings>`. Never logged, never in source control |
| **Transport** | All Azure OpenAI calls over HTTPS/TLS 1.2+. Internal API calls use same ASP.NET Core HTTPS pipeline |

### 10.2 Authorization

| Endpoint | Auth Requirement |
|---|---|
| `GET /api/v1/ai/recommendations/{campaignId}` | Authenticated + `ApproverFilteredRepository` ensures user can only see their own campaign items |
| `POST /api/v1/ai/feedback` | Authenticated + reviewer identity from JWT |
| `POST /api/v1/ai/generate/{campaignId}` | Authenticated + role: `Authorizations Review` (admin-only trigger) |
| `GET /api/v1/table/AIRecommendations` | Authenticated + `ApproverFilteredRepository` auto-filter |

### 10.3 Audit Trail

Every AI interaction is fully auditable:

| Event | Stored In | Fields |
|---|---|---|
| AI recommendation generated | `AIRecommendations` | Recommendation, ConfidenceScore, RiskScore, ModelVersion, PromptVersion, GeneratedAt |
| Reviewer sees recommendation | Application logs | UserId, StepId, Timestamp (via existing `PlcTelemetryEngine`) |
| Reviewer agrees/disagrees | `AIAssistantFeedback` | AiRecommendation, ReviewerDecision, ReviewerAgreedWithAI, DecisionTimestamp, TimeToDecisionMs |
| AI processing job status | `AIProcessingJobs` | Status, TotalItems, ProcessedItems, FailedItems, timestamps |

---

## 11. Implementation Phases

### Phase 1: Foundation (Weeks 1–4)

| Task | Deliverable |
|---|---|
| Create database tables | `AIRecommendations`, `AIAssistantFeedback`, `AIRoleDepartmentBaselines`, `AIProcessingJobs` |
| Create EF migration | `AddAIRecommendationTables.cs` |
| Create EF entities | `AIRecommendation.cs`, `AIAssistantFeedback.cs` |
| Register Azure OpenAI HttpClient | `AddHttpClient("AzureOpenAI", ...)` in service registration |
| Create `AIServiceSettings` | Configuration class + `appsettings.json` section |
| Create `IDataContextAggregator` | SQL view `V_AIReviewItemContext` + C# query service |
| Create `ILlmGateway` interface + implementation | `AzureOpenAILlmGateway` with batch processing |
| Create `IAIRecommendationService` | Core service orchestrating data → LLM → storage |
| Create Worker message handler | `AIGenerateRecommendationsHandler` with `[WorkerMessageHandler]` |
| Create API controller | `AIRecommendationController` with CRUD endpoints |
| Add trigger in `CalculateStatistics()` | Queue `AIGenerateRecommendationsMessage` |
| Unit tests | Handler tests, service tests, gateway mock tests |

**Exit criteria:** AI recommendations generated for a test campaign and queryable via API.

### Phase 2: Risk Summarization & Nexus UI (Weeks 5–8)

| Task | Deliverable |
|---|---|
| Design prompt templates | System prompt + risk recommendation prompt (see Appendix B) |
| Implement LLM response parsing | JSON structured output → `AIRecommendation` rows |
| Extend `V_CertificationReviewItems` | LEFT JOIN to `AIRecommendations` |
| Extend `ReviewItems.cs` entity | `AiRecommendation`, `AiRiskScore`, `AiConfidenceScore`, `AiRiskSummary` |
| Update `review_items.yaml` | Add AI columns to entity + listView |
| Update `my_certifications.yaml` | Add `type: coded` AI Insights tab |
| Create `AiCampaignInsightsPanel.tsx` | React component for campaign-level AI summary |
| Create `AiRecommendationBadge.tsx` | Inline popover badge component |
| Create Nexus repository | `AIRecommendationRepository` extending `ApproverFilteredRepository<T>` |
| Integration tests | End-to-end: create campaign → AI processes → Nexus shows results |

**Exit criteria:** Nexus reviewer sees AI badges on review items grid + AI Insights tab with campaign summary.

### Phase 3: Anomaly Detection (Weeks 9–12)

| Task | Deliverable |
|---|---|
| Create `IAnomalyDetector` | Interface + `EmbeddingAnomalyDetector` implementation |
| Implement embedding generation | Azure OpenAI `text-embedding-3-small` for `{department}+{role}+{usage}` vectors |
| Create baseline computation job | Periodic job to populate `AIRoleDepartmentBaselines` from historical data |
| Implement cosine distance scoring | Compare item embedding against department baseline |
| Statistical outlier detection | Usage % deviation, missing precedent detection, peer comparison |
| Merge anomaly results into recommendations | `RecommendationEngine` combines LLM + anomaly outputs |
| Update prompt templates | Include anomaly context in LLM prompt for explanation generation |
| Tests | Baseline computation tests, anomaly scoring tests, edge case tests |

**Exit criteria:** Anomaly flags appear on review items; items outside department norms are flagged with explanations.

### Phase 4: PLC UI Integration (Weeks 13–15)

| Task | Deliverable |
|---|---|
| Add right-side AI panel to `ReviewScreen.aspx` | Activate `.column.content` CSS slot, add UpdatePanel |
| Add AI Insights tab to `RoleDetailsV5.ascx` | New `TabPanel` in existing `TabContainer` |
| Implement `LoadAIRecommendation()` code-behind | Per-row AI data loading on selection |
| Add per-row AI badges in `ReadApproval()` | Inline badge next to Approve/Reject radios |
| Add feedback capture to `Save_click` | Record agree/disagree in `AIAssistantFeedback` |
| Add `AIRecommendationsEnabled` to `CommonSettings` | Feature flag for PLC |
| CSS styling | AI panel styles, badge styles, responsive layout |
| Manual testing | Cross-browser, various campaign sizes, edge cases |

**Exit criteria:** PLC reviewer sees AI panel, badges, and AI Insights tab; feedback is captured on save.

### Phase 5: Feedback Loop & Analytics (Weeks 16–17)

| Task | Deliverable |
|---|---|
| Implement feedback recording | `RecordFeedbackAsync` in both Nexus and PLC flows |
| Create AI accuracy dashboard | Report showing agreement rate, confidence calibration |
| Add `TimeToDecisionMs` tracking | Measure time from page load to decision click |
| Export feedback data for analysis | CSV/JSON export endpoint for data science team |
| Monitor and tune | Adjust confidence thresholds, prompt wording, batch sizes |

**Exit criteria:** Feedback captured for 100% of AI-advised decisions; accuracy dashboard operational.

### Phase 6: Model Improvement (Weeks 18–24, ongoing)

| Task | Deliverable |
|---|---|
| Analyze feedback data | Identify patterns where AI was wrong; segment by risk level, department, role type |
| Fine-tune prompts | Iterate on prompt templates based on high-disagreement areas |
| Train lightweight classifier | Azure ML or scikit-learn model on feedback data for fast pre-filtering |
| Implement A/B testing | Compare AI-assisted vs. non-assisted reviewer metrics |
| Confidence calibration | Ensure 80% confidence = 80% actual agreement rate |

**Exit criteria:** AI agreement rate ≥70%; measurable reduction in per-item review time.

---

## 12. Risk Assessment & Mitigations

| # | Risk | Severity | Probability | Mitigation |
|---|---|---|---|---|
| 1 | **Azure OpenAI latency** causes slow campaign initialization | Medium | Medium | Pre-compute asynchronously via Worker queue. Reviewer never waits for LLM. Processing status shown with progress bar |
| 2 | **LLM hallucination** — generates incorrect risk summaries | High | Low | Structured JSON output format with constrained fields. Confidence score caps uncertain outputs. All summaries are advisory only — never auto-applied |
| 3 | **Data privacy violation** — PII sent to external model | High | Low | Use Azure OpenAI (tenant-isolated). Configurable PII anonymization. No biometric/financial data in prompts |
| 4 | **Cost overrun** — high token usage on large campaigns | Medium | Medium | Use GPT-4o-mini ($0.15/1M input tokens). 5,000-item campaign ≈ $2-5. Configurable batch size. Caching with TTL to avoid re-processing |
| 5 | **Reviewer over-reliance** on AI — rubber-stamping suggestions | High | Medium | Display confidence score prominently. "Review" recommendation for low-confidence items forces manual assessment. Analytics dashboard tracks rubber-stamp patterns |
| 6 | **PLC WebForms complexity** — async calls in synchronous code-behind | Medium | Medium | All LLM calls happen in Worker (async). PLC code-behind only reads cached results from DB — no async LLM calls in ASPX |
| 7 | **Feature flag failure** — AI feature breaks existing campaign flow | High | Low | `AIRecommendationsEnabled` flag defaults to `false`. AI code wrapped in try/catch, never blocks critical path. AI failure = panel stays hidden |
| 8 | **Regulatory audit questions** — auditors challenge AI-assisted decisions | Medium | Medium | Full audit trail in `AIAssistantFeedback`. Model version + prompt version stored. Human always makes final decision — AI is documented as advisory |
| 9 | **Embedding drift** — baselines become stale over time | Low | Medium | Scheduled baseline refresh job (weekly). Baselines include `ComputedAt` timestamp + `SampleSize` |
| 10 | **Dual-UI maintenance burden** — PLC + Nexus both need updates | Medium | High | Shared `IAIRecommendationService` and `AIRecommendationController` API. UI layers are thin consumers of the same data. PLC integration can be deprioritized if Nexus adoption is high |

---

## Appendix A — Existing Data Model Reference

### Tables Consumed by AI Service (Read-Only)

| Table | Key Columns Used | Purpose |
|---|---|---|
| `AuthoirizationCertifications` | `Id`, `Title`, `IsActive`, `IsFinished`, `ReviewElementName`, `ExpectedEndDate` | Campaign metadata |
| `WorkflowInstances` | `Id`, `CertificationProcessId`, `UserId`, `ClosedOn` | Per-user-role workflow instances |
| `WorkflowInstanceSteps` | `Id`, `WorkflowInstanceId`, `IsApproved`, `Comments`, `HandledBy`, `StepEnd`, `WaitingForGroupId` | Step-level decisions + approver history |
| `WorkflowAuthorizationRequests` | `WorkflowInstanceId`, `RoleId`, `TransactionId` | Role being reviewed |
| `V_CertificationReviewItems` | (view) All review item columns | Pre-joined review data |
| `CompanyEmployees` | `EmployeeId`, `FullName`, `Department`, `Title`, `JobTitle`, `DirectManagerId` | Employee profile |
| `V_Users` | `UserId`, `SystemId`, `SapUserName`, `EmployeeNumber` | System user mapping |
| `V_Roles` | `RoleId`, `Name`, `Description` | Role metadata |
| `L_RiskLevels` | `Id`, `Level`, `SortValue` | Risk classifications |
| `MetaDataForRole` | `RoleId`, `RiskLevelId` | Role ↔ risk level mapping |
| `SoxForbiddenCombinations` | `Id`, `Name`, `RiskLevel`, `RiskDescription` | SoD rule definitions |
| `SoxEntityViolations` | `EntityId`, `ForbiddenCombinationId`, `RiskCounter`, `SolveRecommendion` | Active SoD violations per user |
| `UserRoleUsages` | `UserId`, `RoleName`, `TotalActions`, `UsedActions`, `LastUse` | Pre-computed usage stats |
| `AllowedTransactions` | `UserId`, `TransactionId`, `TimeStamp` | Raw transaction usage logs |
| `ApprovalRollupByGroup` | `CertificationProcessId`, `PendingInstances`, `TotalCount` | Materialized approval progress |

### Stored Procedures Used

| SP | Purpose |
|---|---|
| `spGetCertificationHistory` | Returns last campaign, decision, reviewer, comment for a user-role pair |
| `Calculate_AuthoirizationCertificationStatistics` | Trigger point — calculates campaign stats, then queues AI |

---

## Appendix B — Prompt Templates

### System Prompt

```
You are an authorization review risk analyst for enterprise access governance.
Your role is to analyze user-role assignments and provide risk assessments
to help human reviewers make approve/reject decisions in access certification
campaigns.

You must:
1. Provide factual, evidence-based risk summaries
2. Recommend one of: "Approve", "Reject", or "Review" (when uncertain)
3. Assign a risk score (0-100) and confidence score (0-100)
4. List key factors that influenced your recommendation
5. Be concise — each summary should be 2-3 sentences maximum

You must NOT:
1. Make definitive decisions — you are advisory only
2. Hallucinate data — only reference facts provided in the input
3. Provide legal or compliance opinions
4. Reference any data not present in the provided context

Output format: JSON array matching the input item IDs.
```

### Risk Recommendation Prompt Template

```
Analyze the following authorization review items and provide risk assessments.

For each item, consider:
- Role risk level and description
- User's actual usage of the role (usage % and last used date)
- Whether the user has active SoD (Segregation of Duties) violations
- Historical review patterns (past approvals/rejections for this user-role)
- Department baseline (how common is this role in the user's department)

Items to analyze:
{{ITEMS}}

Respond with a JSON object containing a "recommendations" array.
Each element must have:
{
  "id": <reviewItemId>,
  "recommendation": "Approve" | "Reject" | "Review",
  "confidenceScore": <0-100>,
  "riskScore": <0-100>,
  "riskSummary": "<2-3 sentence plain-English summary>",
  "keyFactors": ["<factor1>", "<factor2>", ...]
}

Guidelines for recommendation:
- "Approve" when: high usage, no violations, consistent with department norms,
  previously approved
- "Reject" when: zero/very low usage + high-risk role, active SoD violations
  without mitigation, outside department norms with no precedent
- "Review" when: mixed signals, moderate risk, first-time assignment,
  or insufficient data for confident recommendation
```

### Anomaly Explanation Prompt Template

```
The following authorization review item has been flagged as anomalous
by our statistical analysis. Explain why this assignment is unusual
and what the reviewer should investigate.

Item data:
{{ITEM}}

Anomaly signals:
{{ANOMALY_SIGNALS}}

Department baseline:
- {{DEPT_ROLE_COUNT}} of {{DEPT_TOTAL_USERS}} users in {{DEPARTMENT}}
  have role {{ROLE_NAME}}
- Average usage in department: {{DEPT_AVG_USAGE}}%
- This user's usage: {{USER_USAGE}}%
- Historical approval rate for this role in department: {{APPROVAL_RATE}}%

Provide a 2-3 sentence explanation of why this is anomalous and what
the reviewer should look for. Output as JSON:
{
  "explanation": "<summary>",
  "investigationPoints": ["<point1>", "<point2>"]
}
```

---

*End of Solution Design Document*
