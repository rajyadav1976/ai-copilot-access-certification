import type { PageTemplateProps } from '@platform/types/page';
import { useMemo } from 'react';
import type {
  AIDecision,
  AIRecommendationDto,
  AIRiskLevel,
} from '../types/ai-recommendations';
import { AIFeedbackWidget } from '../components/AIFeedbackWidget';
import { ReviewerChatWidget } from '../components/ReviewerChatWidget';

/** Color mapping for decisions */
const DECISION_STYLES: Record<
  AIDecision,
  { bg: string; text: string }
> = {
  Approve: { bg: 'bg-green-100', text: 'text-green-800' },
  Reject: { bg: 'bg-red-100', text: 'text-red-800' },
  NeedsReview: {
    bg: 'bg-amber-100',
    text: 'text-amber-800',
  },
};

/** Color mapping for risk levels */
const RISK_STYLES: Record<AIRiskLevel, { bg: string; text: string }> = {
  Low: { bg: 'bg-green-100', text: 'text-green-800' },
  Medium: { bg: 'bg-amber-100', text: 'text-amber-800' },
  High: { bg: 'bg-red-100', text: 'text-red-800' },
  Critical: { bg: 'bg-red-200', text: 'text-red-900' },
};

/**
 * AI Recommendation Details page.
 * Displayed inside the platform Flyout when navigated via useNexusNavigate.
 * Receives the recommendation object via location.state.recommendation.
 */
export function AIRecommendationDetails(_props: PageTemplateProps) {
  const recommendation = useMemo(() => {
    try {
      const stored = sessionStorage.getItem('ai-recommendation-detail');
      return stored
        ? (JSON.parse(stored) as AIRecommendationDto)
        : undefined;
    } catch {
      return undefined;
    }
  }, []);

  if (!recommendation) {
    return (
      <div className="flex h-full items-center justify-center text-muted-foreground">
        No recommendation data available.
      </div>
    );
  }

  const decision = recommendation.decision as AIDecision;
  const riskLevel = recommendation.riskLevel as AIRiskLevel;
  const decisionStyle =
    DECISION_STYLES[decision] ?? DECISION_STYLES.NeedsReview;
  const riskStyle = RISK_STYLES[riskLevel] ?? RISK_STYLES.Medium;
  const isFailed = recommendation.status === 'Failed';

  return (
    <div className="flex h-full flex-col overflow-y-auto">
      {/* Header bar */}
      <div className="border-b bg-muted/30 px-6 py-4">
        <h2 className="text-lg font-semibold">Recommendation Details</h2>
        <p className="mt-0.5 text-sm text-muted-foreground">
          Step ID:{' '}
          <span className="font-mono">
            {recommendation.reviewItemStepId}
          </span>
        </p>
      </div>

      {/* Body */}
      <div className="flex flex-col gap-5 p-6">
        {/* LLM Failed banner */}
        {isFailed && (
          <div className="rounded-md border border-red-200 bg-red-50 p-4">
            <h4 className="text-sm font-semibold text-red-800">
              LLM Failed
            </h4>
            <p className="mt-1 text-sm text-red-700">
              {recommendation.errorMessage ?? 'The AI model failed to generate a recommendation for this item.'}
            </p>
          </div>
        )}

        {/* Decision & Risk badges */}
        {!isFailed && (
        <div className="flex items-center gap-3">
          <span
            className={`inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-xs font-medium ${decisionStyle.bg} ${decisionStyle.text}`}
          >
            {recommendation.decision}
          </span>
          <span
            className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${riskStyle.bg} ${riskStyle.text}`}
          >
            {recommendation.riskLevel} Risk
          </span>
          <span className="text-xs text-muted-foreground">
            Confidence: {(recommendation.confidenceScore * 100).toFixed(1)}%
          </span>
        </div>
        )}

        {/* Role Information */}
        <Section title="Role Information">
          <Field label="Role" value={recommendation.roleName} />
          <Field label="Description" value={recommendation.roleDescription} />
          <Field label="Account" value={recommendation.account} />
          <Field label="System" value={recommendation.systemName} />
        </Section>

        {/* Employee Information */}
        <Section title="Employee Information">
          <Field label="Name" value={recommendation.employeeName} />
          <Field label="Job Title" value={recommendation.employeeJob} />
          <Field label="Department" value={recommendation.employeeDepartment} />
        </Section>

        {/* Usage & Activity */}
        <Section title="Usage & Activity">
          <Field
            label="Usage %"
            value={
              recommendation.usagePercentage != null
                ? `${recommendation.usagePercentage}%`
                : null
            }
          />
          <Field
            label="Days Since Last Used"
            value={
              recommendation.daysSinceLastUsed != null
                ? `${recommendation.daysSinceLastUsed}`
                : 'Never'
            }
          />
          <Field
            label="Peer Usage"
            value={
              recommendation.peerUsagePercent != null
                ? `${recommendation.peerUsagePercent.toFixed(1)}%`
                : null
            }
          />
          <Field
            label="Anomaly Score"
            value={
              recommendation.anomalyScore != null
                ? recommendation.anomalyScore.toFixed(4)
                : null
            }
          />
        </Section>

        {/* Flags */}
        {(recommendation.hasSodViolation || recommendation.isAnomaly) && (
          <Section title="Flags">
            <div className="flex flex-wrap gap-2">
              {recommendation.hasSodViolation && (
                <span className="rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-800">
                  SoD Violation
                </span>
              )}
              {recommendation.isAnomaly && (
                <span className="rounded-full bg-orange-100 px-2 py-0.5 text-xs font-medium text-orange-800">
                  Anomaly Detected
                </span>
              )}
            </div>
          </Section>
        )}

        {/* AI Analysis */}
        <Section title="AI Analysis">
          <p className="text-sm leading-relaxed text-foreground">
            {recommendation.riskSummary || 'No summary available.'}
          </p>
        </Section>

        {/* Risk Factors */}
        {recommendation.riskFactors &&
          recommendation.riskFactors.length > 0 && (
            <Section title="Risk Factors">
              <ul className="list-inside list-disc space-y-1 text-sm">
                {recommendation.riskFactors.map((factor, i) => (
                  <li key={i} className="text-muted-foreground">
                    {factor}
                  </li>
                ))}
              </ul>
            </Section>
          )}

        {/* Timestamp */}
        {recommendation.generatedAt && (
          <div className="border-t pt-3 text-xs text-muted-foreground">
            Generated: {new Date(recommendation.generatedAt).toLocaleString()}
          </div>
        )}

        {/* Feedback Collection Widget */}
        {recommendation.id && (
          <div className="border-t pt-4">
            <AIFeedbackWidget
              recommendationId={recommendation.id}
              aiDecision={decision}
              reviewItemId={recommendation.reviewItemStepId}
            />
          </div>
        )}

        {/* UC5: Interactive Reviewer Assistant Chat */}
        {recommendation.certificationProcessId && recommendation.reviewItemStepId && (
          <div className="border-t pt-4">
            <ReviewerChatWidget
              certificationProcessId={recommendation.certificationProcessId}
              stepId={recommendation.reviewItemStepId}
            />
          </div>
        )}
      </div>
    </div>
  );
}

/** Reusable section with a title */
function Section({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-2">
      <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
        {title}
      </h4>
      <div className="rounded-md border bg-muted/20 p-3">{children}</div>
    </div>
  );
}

/** Single label-value field row */
function Field({
  label,
  value,
}: {
  label: string;
  value: string | null | undefined;
}) {
  return (
    <div className="flex items-baseline justify-between py-1 text-sm">
      <span className="font-medium text-muted-foreground">{label}</span>
      <span className="text-right text-foreground">
        {value || <span className="italic text-muted-foreground">N/A</span>}
      </span>
    </div>
  );
}
