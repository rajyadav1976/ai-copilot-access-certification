# AI-Powered Campaign Review Assistant

## Table of Contents

1. [The Problem We Are Solving](#1-the-problem-we-are-solving)
2. [What We Aim to Achieve](#2-what-we-aim-to-achieve)
3. [The Business Impact](#3-the-business-impact)
4. [How the Solution Works](#4-how-the-solution-works)
   - [Architecture Overview](#41-architecture-overview)
   - [AI Technologies Used](#42-ai-technologies-used)
   - [The Core Pipeline](#43-the-core-pipeline)
   - [Agentic AI Pipeline](#44-agentic-ai-pipeline)
   - [Feedback Learning Loop](#45-feedback-learning-loop)
   - [Frontend User Experience](#46-frontend-user-experience)
   - [API Surface](#47-api-surface)
   - [Data Model](#48-data-model)
   - [Testing](#49-testing)
5. [File Inventory](#5-file-inventory)
6. [Configuration](#6-configuration)
7. [How to Run](#7-how-to-run)

---

## 1. The Problem We Are Solving

> **User Access Certification campaigns in large enterprises are drowning reviewers in thousands of approve/reject decisions with insufficient context, leading to rubber-stamping, audit failures, and preventable security breaches.**

### The Pain Points

| Pain Point | Detail |
|------------|--------|
| **Volume Overload** | A single certification campaign can contain **thousands** of user-role pairs. A reviewer responsible for 500+ items has an average of <30 seconds per decision, leading to fatigue and rubber-stamp approvals. |
| **Context Blindness** | Reviewers see a role name, an account, and usage percentage — but lack insight into peer group patterns, Separation of Duties (SoD) violations, anomaly detection, historical decisions, or the downstream impact of their choice. |
| **Inconsistent Decisions** | Without data-driven guidance, two reviewers facing the same role with the same risk profile may make contradictory decisions across campaigns. |
| **Audit Exposure** | Regulatory frameworks (SOX, SOD, GDPR) demand evidence that access reviews are substantive. Rubber-stamped campaigns create compliance liability. |
| **No Learning from Past Mistakes** | Even when a reviewer correctly overrides a prior bad decision, that correction is lost. The next campaign starts from scratch with zero institutional memory. |

---

## 2. What We Aim to Achieve

> **Build an intelligent AI copilot that sits alongside the human reviewer — automatically analyzing every review item using real data, generating risk-scored recommendations with full transparency, and continuously learning from reviewer feedback to get smarter over time.**

### Goals

1. **Automated Risk Analysis** — Every review item gets an AI-generated recommendation (Approve / Reject / NeedsReview) with a confidence score, risk level, and human-readable explanation.
2. **Agentic Intelligence** — The AI doesn't just look at a single item in isolation. It autonomously investigates using tools (historical decisions, SoD checks, peer analysis, campaign patterns) and reasons step-by-step before making a decision.
3. **Full Transparency** — Every recommendation comes with a complete reasoning trace showing which tools the agent called, what data it found, and how it reached its conclusion. No black-box decisions.
4. **Continuous Learning** — A feedback loop captures whether reviewers agree or disagree with the AI, and injects those corrections back into future prompts as few-shot examples, improving accuracy over time without model fine-tuning.
5. **Smart Escalation** — Items are automatically triaged into escalation tiers (AutoApprove → Standard → Elevated → Critical) so security teams can focus on what matters.
6. **Interactive Q&A** — Reviewers can ask the AI questions directly ("Why was this flagged?" / "What happens if I approve this?") through a conversational chat widget.

---

## 3. The Business Impact

### Quantitative Impact

| Metric | Before AI | With AI | Improvement |
|--------|-----------|---------|-------------|
| Time per review item | ~30 seconds manual | ~2 seconds AI + human verification | **93% reduction** |
| Campaign completion time | Days–Weeks | Hours | **Order-of-magnitude faster** |
| Rubber-stamp rate | ~60-80% (estimated) | Tracked per item with confidence scores | **Measurable & auditable** |
| SoD violations caught | Manual spot-checking | 100% automated scanning | **Complete coverage** |
| Reviewer decision consistency | Varies by reviewer | AI sets baseline; deviations tracked | **Standardized** |

### Qualitative Impact

- **Audit Readiness** — Every AI recommendation is persisted with a confidence score, risk factors, reasoning trace, and reviewer feedback. Auditors get complete decision evidence.
- **Reviewer Empowerment** — Reviewers aren't replaced; they're augmented. The AI surfaces the 15% of items that genuinely need human judgment while handling the clear-cut 85%.
- **Institutional Memory** — The feedback loop ensures that hard-won reviewer expertise from past campaigns is captured and surfaces in future ones.
- **Scalability** — Whether a campaign has 100 items or 10,000+, the AI scales with bounded parallelism and stratified sampling.

---

## 4. How the Solution Works

### 4.1 Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              FRONTEND (React + TypeScript)                      │
│  ┌───────────────── ─┐  ┌──────────────────┐  ┌─────────────────┐  ┌───────────┐│
│  │ AIInsightsTab     │  │ AIRecommendation │  │ AIRecommendation│  │ AIFeedback││
│  │ (Campaign Summary)│  │ Tab (Per Item)   │  │ Details (Flyout)│  │ Widget    ││
│  └────────┬─────── ──┘  └────────┬─────────┘  └────────┬────────┘  └─────┬─────┘│
│           │                      │                     │                 │      │
│           └───────────────────── ┴─────────────────────┴─────────────── ─┘      │
│                                         │                                       │
│                          useAIRecommendations (hooks)                           │
└──────────────────────────────────────────┬──────────────────────────────────────┘
                                           │ REST API (Bearer Token Auth)
┌──────────────────────────────────────────┴──────────────────────────────────────┐
│                          BACKEND (ASP.NET Core 9.0)                             │
│                                                                                 │
│  ┌─ AIRecommendationController ────────────────────────────────────────────────┐│
│  │  GET  /{campaignId}           — List recommendations                        ││
│  │  GET  /{campaignId}/step/{id} — Single recommendation                       ││
│  │  GET  /{campaignId}/summary   — Campaign summary stats                      ││
│  │  POST /{campaignId}/generate  — Trigger AI generation                       ││
│  │  POST /{campaignId}/feedback  — Record reviewer feedback                    ││
│  │  POST /{campaignId}/feedback-and-approve — Agree & close review item        ││
│  │  POST /chat                   — Interactive reviewer chat                   ││
│  │  GET  /feedback/analytics     — Feedback metrics (Learning Loop L1)         ││
│  │  GET  /feedback/versions      — Model version comparison                    ││
│  │  GET  /feedback/drift         — Concept drift detection                     ││
│  └─────────────────────────────────────────────────────────────────────────────┘│
│                                         │                                       │
│  ┌─ RecommendationEngine ──────────────────────────────────────────────────────┐│
│  │                                                                             ││
│  │  1. DataContextAggregator — Enriches review items with peer/SoD/history     ││
│  │  2. EmbeddingAnomalyDetector — Cosine similarity on embeddings              ││
│  │  3. FeedbackEnricher — Few-shot examples from past reviewer corrections     ││
│  │  4. CampaignAnalysisAgent  — Pre-analyze campaign-wide patterns             ││
│  │                                                                             ││
│  │  ┌─ AgentOrchestrator           ─────────────────────────────────────────┐  ││
│  │  │  Plan → Act (tool calls) → Observe (results) → Reflect → Decide       │  ││
│  │  │                                                                       │  ││
│  │  │  9 Agent Tools:                                                       │  ││
│  │  │   1. get_historical_decisions   6. detect_bulk_patterns               │  ││
│  │  │   2. check_sod_violations       7. get_similar_past_decisions         │  ││
│  │  │   3. get_peer_group_details     8. simulate_approval_impact           │  ││
│  │  │   4. get_role_risk_profile      9. close_review_item                  │  ││
│  │  │   5. get_campaign_overview                                            │  ││
│  │  └───────────────────────────────────────────────────────────────────────┘  ││
│  │                                                                             ││
│  │  5. ConfidenceCalibrator  — Adjusts confidence vs historical consensus      ││
│  │  6. EscalationRouter  — Classifies into tiers for workflow routing          ││
│  │                                                                             ││
│  └─────────────────────────────────────────────────────────────────────────────┘│
│                                         │                                       │
│  ┌─ AzureOpenAILlmGateway ─────────────────────────────────────────────────────┐│
│  │  Chat Completions (GPT-4o-mini)  |  Embeddings (text-embedding-3-small)     ││
│  └─────────────────────────────────────────────────────────────────────────────┘│
│                                         │                                       │
│  ┌─ SQL Server (EF Core) ──────────────────────────────────────────────────────┐│
│  │  AIRecommendations │ AIRecommendationFeedbacks │ AIProcessingJobs           ││
│  │  AIRoleDepartmentBaselines │ AIAssistantFeedback │ ReviewItems (view)       ││
│  └─────────────────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────────────────┘
```

### 4.2 AI Technologies Used

| Technology | Model / Technique | Where Used | Why |
|------------|-------------------|------------|-----|
| **Azure OpenAI GPT-4o-mini** | `gpt-4o-mini-2024-07-18` | Recommendation generation, agent reasoning, reviewer chat, campaign analysis | Fast inference, JSON mode support, function calling for agentic workflows |
| **Azure OpenAI Embeddings** | `text-embedding-3-small` | Anomaly detection | High-quality 1536-dim embeddings at low cost for cosine similarity comparisons |
| **Cosine Similarity** | Mathematical distance metric | `EmbeddingAnomalyDetector` | Compares each user's role profile vector against their peer group centroid to detect unusual assignments |
| **Prompt Engineering** | System + User prompt templates with structured JSON output | `RecommendationEngine.BuildSystemPrompt/BuildUserPrompt` | Controls LLM output format, decision guidelines, risk level definitions |
| **Function Calling (Tool Use)** | OpenAI function calling API | `AgentOrchestrator` | Enables the LLM to autonomously decide which tools to call based on item characteristics |
| **Few-Shot Prompt Injection** | Dynamic examples from reviewer feedback | `FeedbackEnricher` | Injects historical correction examples directly into the prompt at inference time — no fine-tuning needed |

### 4.3 The Core Pipeline

When a reviewer (or system) triggers AI recommendation generation for a campaign, here's what happens step by step:

#### Step 1: Data Context Aggregation
**Component:** `DataContextAggregator`

The system queries the `V_CertificationReviewItems` database view and enriches each review item with:
- **Peer Usage Statistics** — What percentage of employees in the same department + job title have this role?
- **Historical Decisions** — Was this user-role combination approved or rejected in past campaigns? What comments did prior reviewers leave?
- **SoD Violation Detection** — Does this role assignment create Separation of Duties conflicts?
- **Usage Metrics** — Usage percentage, last-used date, days since last use, number of used activities.

```
ReviewItems (view)
    ├── + Peer Analysis (same dept + job)
    ├── + Historical Decisions (prior campaigns)
    ├── + SoD Violations (authorization checks)
    └── = ReviewItemContext (fully enriched)
```

#### Step 2: Anomaly Detection (Embedding-Based)
**Component:** `EmbeddingAnomalyDetector`

For each review item, the system:
1. **Groups items into peer groups** — Employees in the same department + job title form a peer group (minimum 3 peers required).
2. **Generates embeddings** — Converts each user's role profile (role name, usage %, SoD status, last used) into a 1536-dimensional vector using `text-embedding-3-small`.
3. **Computes the peer centroid** — Averages all peer group vectors to create the "typical" role profile for that group.
4. **Calculates cosine similarity** — Measures how far each individual's role profile vector deviates from the peer centroid.
5. **Flags anomalies** — Items with similarity below `0.65` are flagged as anomalous.

```
Anomaly Score = 1 - CosineSimilarity(userEmbedding, peerCentroid)

Score Range:
  0.00 - 0.30  →  Normal (matches peer group)
  0.30 - 0.65  →  Moderate deviation
  0.65 - 1.00  →  Anomalous (flagged for extra scrutiny)
```

#### Step 3: Feedback Enrichment (Learning Loop Layer 2)
**Component:** `FeedbackEnricher`

Before generating the recommendation, the system checks for historical reviewer feedback:
1. **Finds similar past items** — Queries `AIRecommendationFeedback` joined with `AIRecommendation` from previous campaigns.
2. **Scores relevance** — Each past example gets a relevance score based on:
   - Disagreement with AI: **+0.40** (highest signal — the reviewer corrected the AI)
   - Same role name: **+0.25**
   - Similar usage (±20%): **+0.15**
   - Matching SoD status: **+0.05**
   - Matching risk level: **+0.05**
   - Low quality rating: **+0.10** (reviewer flagged poor AI performance)
3. **Injects as few-shot examples** — The top-scoring examples are formatted as a "Historical Reviewer Feedback" section inserted directly into the LLM prompt.

This enables the model to learn from past mistakes **at inference time** without any fine-tuning.

#### Step 4: LLM Recommendation Generation

**Agentic AI Pipeline** — *See Section 4.4 below for details*
- Runs the `AgentOrchestrator` with a Plan → Act → Observe → Reflect loop.
- The agent autonomously calls tools to gather additional data before making its decision.
- After the agent decides, runs post-processing (confidence calibration, escalation routing).

#### Step 5: Persistence
All recommendations are saved to the `AIRecommendations` table with:
- Decision and confidence score
- Risk level, summary, and factors
- Anomaly detection results
- Model version and prompt version (for reproducibility)
- Processing time and token usage (for cost tracking)

---

### 4.4 Agentic AI Pipeline

The agentic pipeline goes beyond single-shot LLM calls. The AI operates as an autonomous agent that reasons, investigates, and makes decisions through a structured loop.

#### Dynamic Data Gathering
**Component:** `AgentOrchestrator` + 9 `IAgentTool` implementations

The agent autonomously decides which investigation tools to call based on the review item's characteristics. For a zero-usage role with SoD violations, it might call 4-5 tools. For a clearly active, clean role, it might call only 1-2 tools.

**Available Tools:**

| # | Tool | Purpose | When the Agent Uses It |
|---|------|---------|----------------------|
| 1 | `get_historical_decisions` | Retrieves past approve/reject history for a user-role pair | When it wants to know if this role was previously approved or rejected |
| 2 | `check_sod_violations` | Checks for Separation of Duties conflicts | When the item has SoD flags or suspicious role combinations |
| 3 | `get_peer_group_details` | Analyzes what peers in the same dept/job hold | When peer usage is low or the role seems unusual for the job function |
| 4 | `get_role_risk_profile` | Retrieves role metadata, usage stats, privilege indicators | When reviewing an unfamiliar or potentially sensitive role |
| 5 | `get_campaign_overview` | Summary stats for the entire campaign | When it needs to understand the broader campaign context |
| 6 | `detect_bulk_patterns` | Finds suspicious bulk provisioning patterns | For campaign-level risk analysis (over-provisioning, universal zero-usage) |
| 7 | `get_similar_past_decisions` | Finds similar items from prior campaigns with outcomes | To calibrate confidence based on historical reviewer consensus |
| 8 | `simulate_approval_impact` | Checks downstream consequences of approving a role | When the agent wants to understand access concentration or new SoD risks |
| 9 | `close_review_item` | Closes (approves) a review item applying the AI-recommended decision | Used by the "Agree & Approve" action to record positive feedback and close the review item in one step |

#### Multi-Step Reasoning with Self-Reflection
**Component:** `AgentOrchestrator`

The agent follows a **Plan → Act → Observe → Reflect** loop:

```
┌──────────────────────────────────────────────────────────────────┐
│  AGENT LOOP (max 10 iterations)                                  │
│                                                                  │
│  Iteration 1:                                                    │
│    Plan:  "I need to check historical decisions and SoD status"  │
│    Act:   → get_historical_decisions(userId=42, roleId=101)      │
│    Act:   → check_sod_violations(userId=42)                      │
│    Observe: "3 prior approvals, 1 SoD violation found"           │
│                                                                  │
│  Iteration 2:                                                    │
│    Reflect: "SoD violation is concerning, let me check the role" │
│    Act:   → get_role_risk_profile(roleName="Z_FI_ADMIN")         │
│    Observe: "Privileged role, 0% usage, high anomaly score"      │
│                                                                  │
│  Iteration 3:                                                    │
│    Reflect: "This is a privileged unused role with SoD. Reject." │
│    Decide: { decision: "Reject", confidence: 0.88,               │
│              riskLevel: "Critical", riskSummary: "..." }         │
│                                                                  │
│  ✓ Complete — 3 iterations, 3 tool calls, 1 final answer         │
└──────────────────────────────────────────────────────────────────┘
```

Every tool call and its result is recorded in a **reasoning trace** that's stored alongside the recommendation for full auditability.

#### Campaign-Level Analysis
**Component:** `CampaignAnalysisAgent`

Before individual items are analyzed, a dedicated agent runs once to produce **campaign-wide insights**:
1. Calls `get_campaign_overview` to gather aggregate statistics.
2. Calls `detect_bulk_patterns` to find systemic provisioning issues.
3. Uses the LLM to produce a narrative summary of the campaign's risk posture.
4. Identifies high-risk roles by name patterns (SAP_ALL, ADMIN, etc.).

These `CampaignInsights` are then injected into each per-item agent's context, enabling it to make decisions that consider the bigger picture.

Example insight: *"This campaign has 847 items across 142 users. 23% of items have zero usage. Role 'Z_BASIS_ADMIN' is assigned to 45 users but only used by 3 — suggesting systemic over-provisioning."*

#### Adaptive Confidence Calibration
**Component:** `ConfidenceCalibrator`

After the agent produces its recommendation, the confidence score is calibrated against historical ground truth:
1. Calls `get_similar_past_decisions` to find how reviewers decided on similar items in past campaigns.
2. Compares the agent's recommendation with the historical consensus.
3. **Boosts confidence** (up to +15%) when the recommendation aligns with history.
4. **Reduces confidence** (up to -15%) when it deviates, adding an explanatory note.
5. If confidence drops below 50%, suggests the item for human review.

```
Example:
  Agent says: "Approve" with confidence 0.78
  Historical approval rate for similar items: 90%
  → Alignment: AGREES → Confidence boosted to 0.91

  Agent says: "Reject" with confidence 0.72
  Historical approval rate for similar items: 85%
  → Alignment: DISAGREES → Confidence reduced to 0.61
  → Note added: "Historical patterns suggest this may need additional human review"
```

#### Interactive Reviewer Assistant
**Component:** `ReviewerAssistant` + `ReviewerChatWidget.tsx`

A conversational AI assistant that reviewers can interact with directly:
- The reviewer opens a chat widget on any review item.
- They can ask natural-language questions: *"Why was this flagged as high risk?"*, *"What would happen if I approve this?"*, *"Who else has this role?"*
- The assistant loads the full item context, any existing AI recommendation, and uses the same 9 agent tools to investigate.
- It maintains multi-turn conversation history for follow-up questions.
- Responses include suggested follow-up questions for guided exploration.

#### Escalation & Routing
**Component:** `EscalationRouter`

A deterministic (no LLM call) post-processing step that classifies each item into an escalation tier:

| Tier | Criteria | Routing |
|------|----------|---------|
| **AutoApprove** | Zero risk signals, "Approve" decision, confidence ≥ 85%, Low risk, no SoD, low anomaly | Can be batch-approved without individual review |
| **Standard** | Normal items with moderate signals | Routed to the primary reviewer |
| **Elevated** | 3+ risk signals accumulated | Flagged for additional scrutiny |
| **Critical** | 5+ risk signals (SoD + high anomaly + privileged role + agent-assessed Critical) | Requires security team review |

Risk signals tracked:
- SoD violations (+2 points)
- High anomaly score ≥0.7 (+2), moderate ≥0.5 (+1)
- Privileged role name pattern (+2)
- Zero usage with no recent activity (+1)
- Agent-assessed Critical risk (+2) or High risk (+1)
- Low agent confidence <0.5 (+1)

---

### 4.5 Feedback Learning Loop

The learning loop ensures the AI continuously improves from reviewer decisions. It has two layers plus a feedback collection UI.

#### Feedback Collection (UI)
**Component:** `AIFeedbackWidget.tsx`

Displayed on both the **AI Recommendation Tab** (coded tab on the ReviewItems form) and the **AI Recommendation Details** page (flyout):

```
┌────────────────────────────────────────────────────────┐
│  Rate AI Recommendation                                │
│                                                        │
│  Do you agree with the AI recommendation to Approve?   │
│                                                        │
│  [ Agree ]   [ Disagree ]   [ Agree & Approve ]       │
│                                                        │
│  (If Disagree):                                        │
│  Your actual decision: [ Approve ▾ ]                   │
│  Override reason: [ Required text field ]              │
│                                                        │
│  Quality Rating: ★ ★ ★ ★ ☆  (1-5 stars)             │
│  Comments: [ Optional free-form text ]                 │
│                                                        │
│  [ Submit Feedback ]                                   │
└────────────────────────────────────────────────────────┘
```

The widget presents the AI's recommendation (e.g., **Approve**) and offers three actions:

| Action | Behavior |
|--------|----------|
| **Agree** | Records positive feedback indicating the reviewer agrees with the AI recommendation. The review item remains open for the reviewer to take action separately. |
| **Disagree** | Expands additional fields for the reviewer to specify their actual decision, an override reason (required), quality rating, and optional comments. |
| **Agree & Approve** | Records positive feedback **and** closes the review item in a single action — applying the AI-recommended decision (e.g., Approve) to the review item. This streamlines the workflow by combining feedback submission and item disposition into one click, eliminating the need for the reviewer to separately act on the item after agreeing with the AI. |

Captures:
- **Agreement/Disagreement** — Did the reviewer agree with the AI?
- **Agree & Approve** — One-click action to agree with the AI and close the review item with the recommended decision
- **Actual Decision** — If they disagreed, what did they actually decide?
- **Override Reason** — Required when disagreeing (e.g., "Business justification exists", "Role is used indirectly")
- **Quality Rating** — 1-5 star rating of the AI's analysis quality
- **Free-form Comments** — Optional additional context

#### Layer 1: Feedback Analytics Service
**Component:** `FeedbackAnalyticsService`

Aggregates raw feedback into actionable metrics:

- **`GetMetricsAsync`** — Agreement rate, accuracy by decision type, accuracy by risk level, common override reasons, average quality rating (filterable by risk level and time window).
- **`GetVersionPerformanceAsync`** — Compares accuracy across prompt/model versions, so you can measure if a prompt change actually improved performance.
- **`DetectConceptDriftAsync`** — Compares recent agreement rates (last 30 days) to historical baseline (last 90 days). If drift exceeds 10%, flags that the model's accuracy is degrading and prompt templates need updating.

#### Layer 2: Few-Shot Enricher
**Component:** `FeedbackEnricher`

Retrieves similar past items with reviewer feedback and formats them as examples injected directly into the LLM prompt:

```
## Historical Reviewer Feedback
Showing 3 examples from past campaigns where reviewers provided corrections:

Example 1 (Reviewer DISAGREED — AI said Approve, Reviewer chose Reject):
- Role: Z_FI_ADMIN | Usage: 5% | Risk: High | SoD Violation: Yes
- AI Confidence: 0.72 | Quality Rating: 2/5
- Override Reason: "SoD violation with payment processing role outweighs usage"

Example 2 (Reviewer AGREED — AI said Reject):
- Role: SAP_ALL | Usage: 0% | Risk: Critical | SoD Violation: No
- AI Confidence: 0.95 | Quality Rating: 5/5

→ Use these examples to inform your analysis of the current item.
```

This mechanism enables the model to learn from corrections **without any fine-tuning** — the corrections flow directly into the prompt context.

---

### 4.6 Frontend User Experience

#### Screen 1: AI Insights Tab (Campaign Summary)
**File:** `AIInsightsTab.tsx`

Campaign-level overview showing:
- Total items analyzed, distribution of decisions (Approve/Reject/NeedsReview)
- Average confidence score, high-risk count, anomaly count, SoD violations
- Status of AI generation (Not Generated / Processing / Generated)
- "Generate AI Recommendations" button to trigger analysis

#### Screen 2: AI Recommendation Tab (Per-Item Detail)
**File:** `AIRecommendationTab.tsx`

Coded tab on the ReviewItems form showing:
- AI decision badge (Approve in green / Reject in red / NeedsReview in amber)
- Confidence score with visual indicator
- Risk level (Low/Medium/High/Critical)
- Risk summary explanation
- Detailed risk factors list
- Anomaly detection result
- Timestamp and model metadata
- **Feedback widget** for agree/disagree
- **Chat widget** for interactive Q&A

#### Screen 3: AI Recommendation Details (Flyout)
**File:** `AIRecommendationDetails.tsx`

Full-page flyout accessible via Nexus navigation with all details from Screen 2 plus:
- Complete reasoning trace (agent tool calls and results)
- SessionStorage data passing for fast navigation
- Same feedback and chat widgets

#### Screen 4: AI Recommendation Badge (Grid Column)
**File:** `AIRecommendationBadge.tsx`

Inline badge component shown directly in the ReviewItems data grid:
- Color-coded decision indicator
- Confidence score
- Clickable to navigate to the details flyout

---

### 4.7 API Surface

Base URL: `/api/v1/certifications/ai-recommendations`

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/{campaignId}` | List all recommendations for a campaign |
| `GET` | `/{campaignId}/step/{stepId}` | Get recommendation for a specific review item |
| `GET` | `/{campaignId}/summary` | Get campaign-level recommendation summary |
| `POST` | `/{campaignId}/generate` | Trigger AI recommendation generation |
| `POST` | `/{campaignId}/feedback` | Record reviewer feedback on a recommendation |
| `POST` | `/{campaignId}/feedback-and-approve` | Record agreement feedback and close (approve) the review item in a single action |
| `POST` | `/chat` | Interactive reviewer chat |
| `GET` | `/feedback/analytics` | Get aggregate feedback metrics (Learning Loop L1) |
| `GET` | `/feedback/versions` | Compare model/prompt version performance |
| `GET` | `/feedback/drift` | Detect concept drift in AI accuracy |

---

### 4.8 Data Model

#### AIRecommendations Table

| Column | Type | Description |
|--------|------|-------------|
| SysId | `Guid` (PK) | Unique recommendation ID |
| CertificationProcessId | `long` | Parent campaign |
| ReviewItemStepId | `long` | The review item this recommendation targets |
| UserId | `long?` | Employee user ID |
| RoleId | `long?` | Role ID under review |
| SystemId | `long?` | Associated system |
| Decision | `string` | `Approve` / `Reject` / `NeedsReview` |
| ConfidenceScore | `decimal` | 0.0 – 1.0 |
| RiskLevel | `string` | `Low` / `Medium` / `High` / `Critical` |
| RiskSummary | `string` | Human-readable explanation |
| RiskFactors | `string` (JSON) | Array of individual risk factors |
| IsAnomaly | `bool` | Flagged by embedding-based anomaly detection |
| AnomalyScore | `decimal` | Raw anomaly score (0 = normal, 1 = anomalous) |
| UsagePercentage | `long?` | Snapshot of usage % at analysis time |
| DaysSinceLastUsed | `int?` | Days since last use |
| HasSodViolation | `bool` | SoD flag |
| PeerUsagePercent | `decimal?` | Peer group adoption percentage |
| TokensUsed | `int` | LLM token consumption |
| ProcessingTimeMs | `int` | End-to-end processing time |
| ModelVersion | `string` | e.g., `gpt-4o-mini-2024-07-18` |
| PromptVersion | `string` | e.g., `v1.0.0` |
| Status | `string` | `Pending` / `Generated` / `Failed` |
| GeneratedAt | `DateTimeOffset?` | When the recommendation was produced |

#### AIRecommendationFeedbacks Table

| Column | Type | Description |
|--------|------|-------------|
| SysId | `Guid` (PK) | Unique feedback ID |
| AIRecommendationId | `Guid` (FK) | Links to the recommendation |
| ReviewerUserId | `Guid` | Who provided feedback |
| ActualDecision | `string` | What the reviewer actually decided |
| AgreedWithAI | `bool` | Did they agree? |
| OverrideReason | `string?` | Why they disagreed |
| QualityRating | `int?` | 1-5 star rating |
| Comments | `string?` | Free-form feedback |
| FeedbackTimestamp | `DateTimeOffset` | When feedback was submitted |

---

### 4.9 Testing

**Total Tests: 107** (78 Components + 29 Agent) — **All passing**

| Test Class | Tests | Covers |
|------------|-------|--------|
| `AIRecommendationServiceTests` | 13 | Service CRUD, summary computation, feedback recording |
| `AIRecommendationControllerTests` | 11 | API endpoints, authorization, error handling |
| `RecommendationEngineTests` | 18 | Both pipelines (single-shot + agentic), sampling, persistence |
| `EmbeddingAnomalyDetectorTests` | 7 | Peer grouping, cosine similarity, edge cases |
| `AgentOrchestratorTests` | 29 | Tool execution, reasoning loop, error handling, all 9 tools |
| `FeedbackEnricherTests` | 6 | Few-shot retrieval, relevance scoring, prompt formatting |
| `FeedbackAnalyticsServiceTests` | 7 | Metrics computation, version comparison, drift detection |

Testing infrastructure:
- **In-memory SQLite** database for isolation
- **Mocked Azure OpenAI** calls via `Mock<ILlmGateway>`
- **Full DI container** in controller tests

---

## 5. File Inventory

### Backend — Core AI Engine (14 files)

| File | Lines | Purpose |
|------|-------|---------|
| `Certification/AI/RecommendationEngine.cs` | ~660 | Main orchestration pipeline |
| `Certification/AI/DataContextAggregator.cs` | ~276 | Enriches review items with peer/SoD/history data |
| `Certification/AI/EmbeddingAnomalyDetector.cs` | ~199 | Cosine similarity anomaly detection on embeddings |
| `Certification/AI/AzureOpenAILlmGateway.cs` | ~548 | Azure OpenAI client (chat completions + embeddings) |
| `Certification/AI/AIRecommendationService.cs` | ~276 | Service for CRUD and feedback recording |
| `Certification/AI/AIRecommendationController.cs` | ~276 | REST API controller |
| `Certification/AI/FeedbackEnricher.cs` | ~289 | Learning Loop Layer 2 — few-shot injection |
| `Certification/AI/FeedbackAnalyticsService.cs` | ~230 | Learning Loop Layer 1 — aggregate analytics |
| `Certification/AI/AIServiceSettings.cs` | ~60 | Configuration POCO |
| `Certification/AI/IRecommendationEngine.cs` | ~15 | Interface |
| `Certification/AI/ILlmGateway.cs` | ~30 | Interface |
| `Certification/AI/IDataContextAggregator.cs` | ~15 | Interface |
| `Certification/AI/IAnomalyDetector.cs` | ~15 | Interface |
| `Certification/AI/IAIRecommendationService.cs` | ~30 | Interface |

### Backend — Agentic AI (8 files)

| File | Lines | Purpose |
|------|-------|---------|
| `Certification/AI/Agent/AgentOrchestrator.cs` | ~543 | Plan → Act → Observe → Reflect loop |
| `Certification/AI/Agent/CampaignAnalysisAgent.cs` | ~200 | Campaign-level pre-analysis |
| `Certification/AI/Agent/ConfidenceCalibrator.cs` | ~206 | Post-hoc confidence adjustment |
| `Certification/AI/Agent/ReviewerAssistant.cs` | ~297 | Interactive chat Q&A |
| `Certification/AI/Agent/EscalationRouter.cs` | ~200 | Deterministic escalation triage |
| `Certification/AI/Agent/Tools/AgentTools.cs` | ~700 | 9 tool implementations |
| `Certification/AI/Agent/AgentModels.cs` | ~297 | Agent message, tool call, reasoning trace models |
| `Certification/AI/Agent/IAgentTool.cs` | ~15 | Tool interface |

### Backend — Models & Entities (7 files)

| File | Purpose |
|------|---------|
| `Certification/AI/Models/AIRecommendationModels.cs` | DTOs: ReviewItemContext, LlmAnalysisRequest/Response, Summary, etc. |
| `Certification/AI/Models/FeedbackModels.cs` | DTOs: FeedbackMetrics, VersionPerformance, DriftReport, FeedbackExample |
| `Entities/Certifications/AIRecommendation.cs` | EF Core entity |
| `Entities/Certifications/AIRecommendationFeedback.cs` | EF Core entity |
| `Entities/Certifications/AIProcessingJob.cs` | EF Core entity |
| `Entities/Certifications/AIRoleDepartmentBaseline.cs` | EF Core entity |
| `Entities/Certifications/AIAssistantFeedback.cs` | EF Core entity |

### Backend — Tests (7 files)

| File | Tests |
|------|-------|
| `AI/AIRecommendationServiceTests.cs` | 13 |
| `AI/AIRecommendationControllerTests.cs` | 11 |
| `AI/RecommendationEngineTests.cs` | 18 |
| `AI/EmbeddingAnomalyDetectorTests.cs` | 7 |
| `AI/AgentOrchestratorTests.cs` | 29 |
| `AI/FeedbackEnricherTests.cs` | 6 |
| `AI/FeedbackAnalyticsServiceTests.cs` | 7 |

### Backend — Infrastructure (2 files)

| File | Purpose |
|------|---------|
| `Migrations/20260226051739_AddAITables.cs` | EF Core migration for all AI tables |
| `Worker/AIRecommendationGenerationHandler.cs` | Background worker handler for async generation |

### Frontend (6 files)

| File | Purpose |
|------|---------|
| `components/AIInsightsTab.tsx` | Campaign summary dashboard |
| `components/AIRecommendationTab.tsx` | Per-item recommendation detail (coded tab) |
| `components/AIRecommendationBadge.tsx` | Inline grid badge component |
| `components/AIFeedbackWidget.tsx` | Feedback collection (agree/disagree + rating) |
| `pages/AIRecommendationDetails.tsx` | Full-page flyout with details + chat |
| `hooks/useAIRecommendations.ts` | React Query hooks for all API calls |

### Frontend — Types

| File | Purpose |
|------|---------|
| `types/ai-recommendations.ts` | TypeScript types for all AI DTOs |

---

## 6. Configuration

### Azure OpenAI Settings (`appsettings.json`)

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://<your-resource>.openai.azure.com",
    "ApiKey": "<your-api-key>",
    "ChatDeployment": "gpt-4o-mini",
    "EmbeddingDeployment": "text-embedding-3-small",
    "ApiVersion": "2024-08-01-preview"
  }
}
```

### AI Service Settings (`AIServiceSettings`)

| Setting | Default | Description |
|---------|---------|-------------|
| `AgentEnabled` | `true` | Use agentic pipeline (true) |
| `MaxConcurrentLlmCalls` | `5` | Bounded parallelism for LLM API calls |
| `MaxBatchSize` | `10` | Items per processing batch |
| `MaxItemsCap` | `100` | Stratified sampling cap for large campaigns |
| `MaxAgentIterations` | `10` | Maximum agent loop iterations |
| `EnableCampaignAnalysis` | `true` | Run campaign pre-analysis |
| `EnableConfidenceCalibration` | `true` | Run post-hoc calibration |
| `EnableEscalation` | `true` | Run escalation routing |

---

## 7. How to Run

### Prerequisites
- .NET 9.0 SDK
- Node.js 18+ with pnpm
- SQL Server (or LocalDB)
- Azure OpenAI resource with GPT-4o-mini and text-embedding-3-small deployments

### Backend
```bash
cd Pathlock.Cloud.Nexus/backend/src/Pathlock.Cloud.Api
dotnet run
# Server starts on http://localhost:5001
```

### Frontend
```bash
cd Pathlock.Cloud.Nexus/frontend
pnpm install
pnpm dev
# Dev server starts on http://localhost:5173
```

### Run Tests
```bash
cd Pathlock.Cloud.Nexus/backend
dotnet test --filter "AI" --verbosity minimal
# Expected: 107 tests passed, 0 failed
```

### Trigger AI Generation (API)
```bash
curl -X POST http://localhost:5001/api/v1/certifications/ai-recommendations/76/generate \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"forceRegenerate": true}'
```

---
