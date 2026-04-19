/** AI Recommendation decision types */
export type AIDecision = 'Approve' | 'Reject' | 'NeedsReview';

/** AI Risk level classifications */
export type AIRiskLevel = 'Low' | 'Medium' | 'High' | 'Critical';

/** AI Recommendation status */
export type AIRecommendationStatus = 'Pending' | 'Generated' | 'Failed' | 'Expired' | 'NotGenerated' | 'Processing' | 'NoItems';

/** Single AI recommendation for a review item */
export interface AIRecommendationDto {
  id: string;
  certificationProcessId: number;
  reviewItemStepId: number;
  userId: number | null;
  roleId: number | null;
  decision: AIDecision;
  confidenceScore: number;
  riskLevel: AIRiskLevel;
  riskSummary: string | null;
  riskFactors: string[];
  usagePercentage: number | null;
  daysSinceLastUsed: number | null;
  hasSodViolation: boolean;
  peerUsagePercent: number | null;
  isAnomaly: boolean;
  anomalyScore: number | null;
  status: AIRecommendationStatus;
  errorMessage: string | null;
  generatedAt: string | null;
  // Display fields enriched from review items view
  roleName: string | null;
  roleDescription: string | null;
  employeeName: string | null;
  employeeJob: string | null;
  employeeDepartment: string | null;
  account: string | null;
  systemName: string | null;
}

/** Campaign-level AI recommendation summary */
export interface AIRecommendationSummary {
  certificationProcessId: number;
  totalItems: number;
  recommendedApprove: number;
  recommendedReject: number;
  needsReview: number;
  highRiskCount: number;
  anomalyCount: number;
  sodViolationCount: number;
  averageConfidence: number;
  failedCount: number;
  status: AIRecommendationStatus;
  generatedAt: string | null;
}

/** Feedback request body */
export interface AIRecommendationFeedbackRequest {
  actualDecision: string;
  agreedWithAI: boolean;
  overrideReason?: string;
  feedbackComments?: string;
  qualityRating?: number;
}

/** Request to generate recommendations */
export interface GenerateRecommendationsRequest {
  forceRegenerate?: boolean;
  specificStepIds?: number[];
}

// ─── UC5: Interactive Reviewer Assistant (Conversational Agent) ───

/** A single turn in the reviewer chat conversation */
export interface ChatTurn {
  role: 'user' | 'assistant';
  content: string;
}

/** Request body sent to the reviewer assistant chat endpoint */
export interface ReviewerChatRequest {
  question: string;
  conversationHistory?: ChatTurn[];
}

/** A reasoning step performed by the assistant's agent loop */
export interface AgentReasoningStep {
  stepNumber: number;
  action: string;
  toolName?: string;
  toolArguments?: string;
  toolResult?: string;
  reasoning?: string;
  timestamp: string;
}

/** Response from the reviewer assistant chat endpoint */
export interface ReviewerChatResponse {
  answer: string;
  suggestedFollowUps?: string[];
  reasoningSteps?: AgentReasoningStep[];
  tokensUsed: number;
}
