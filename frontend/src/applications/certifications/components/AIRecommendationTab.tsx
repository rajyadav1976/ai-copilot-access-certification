import { memo, useMemo } from 'react';
import { useSearchParams } from 'react-router';

import { useParentContext } from '@platform/pages/FormViews/ParentContext';

import { useAIRecommendationByStep } from '../hooks/useAIRecommendations';
import type { AIDecision, AIRiskLevel } from '../types/ai-recommendations';
import { AIFeedbackWidget } from './AIFeedbackWidget';
import { ReviewerChatWidget } from './ReviewerChatWidget';

/** Color mapping for decisions */
const DECISION_STYLES: Record<
  AIDecision,
  { bg: string; text: string; label: string }
> = {
  Approve: {
    bg: 'bg-green-100',
    text: 'text-green-800',
    label: 'Approve',
  },
  Reject: {
    bg: 'bg-red-100',
    text: 'text-red-800',
    label: 'Reject',
  },
  NeedsReview: {
    bg: 'bg-amber-100',
    text: 'text-amber-800',
    label: 'Needs Review',
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
 * AI Recommendation Tab for the ReviewItems form.
 * Renders AI recommendation details for the current review item.
 * Registered as a coded tab and rendered by the platform's CodedTab component.
 */
export const AIRecommendationTab = memo(function AIRecommendationTab() {
  const [searchParams] = useSearchParams();
  const parentData = useParentContext();

  // Extract IDs from URL search params and parent context
  const stepId = useMemo(() => {
    const idParam = searchParams.get('id');
    return idParam ? Number(idParam) : null;
  }, [searchParams]);

  const certificationProcessId = useMemo(() => {
    // Try from parent context first
    const fromContext =
      parentData?.certificationProcessId ??
      parentData?.CertificationProcessId;
    if (fromContext) return Number(fromContext);

    // Fallback: parse from initialFields in URL
    try {
      const initialFields = searchParams.get('initialFields');
      if (initialFields) {
        const parsed = JSON.parse(initialFields);
        return Number(
          parsed.certificationProcessId ?? parsed.CertificationProcessId,
        );
      }
    } catch {
      // ignore parse errors
    }
    return null;
  }, [parentData, searchParams]);

  const {
    data: recommendation,
    isLoading,
    error,
  } = useAIRecommendationByStep(certificationProcessId, stepId);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <span className="ml-2 text-sm text-muted-foreground">
          Loading AI recommendation…
        </span>
      </div>
    );
  }

  if (error || !recommendation) {
    return (
      <div className="flex flex-col items-center justify-center gap-2 py-16 text-muted-foreground">
        <p className="text-sm">
          No AI recommendation available for this item.
        </p>
        <p className="text-xs opacity-60">
          Generate recommendations from the AI Insights tab on the campaign
          view.
        </p>
      </div>
    );
  }

  const decision = recommendation.decision as AIDecision;
  const riskLevel = recommendation.riskLevel as AIRiskLevel;
  const isFailed = recommendation.status === 'Failed';
  const decisionStyle =
    DECISION_STYLES[decision] ?? DECISION_STYLES.NeedsReview;
  const riskStyle = RISK_STYLES[riskLevel] ?? RISK_STYLES.Medium;

  // Show LLM failure banner if this item failed
  if (isFailed) {
    return (
      <div className="flex flex-col gap-4 py-4">
        <div className="rounded-md border border-red-200 bg-red-50 p-4">
          <h4 className="text-sm font-semibold text-red-800">
            LLM Failed
          </h4>
          <p className="mt-1 text-sm text-red-700">
            {recommendation.errorMessage ?? 'The AI model failed to generate a recommendation for this item.'}
          </p>
          <p className="mt-2 text-xs text-red-600">
            Try regenerating recommendations from the AI Insights tab on the campaign view.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-5 py-4">
      {/* Decision & Risk Header */}
      <div className="flex flex-wrap items-center gap-3">
        <span
          className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-sm font-semibold ${decisionStyle.bg} ${decisionStyle.text}`}
        >
          {decisionStyle.label}
        </span>
        <span
          className={`inline-flex items-center rounded-full px-3 py-1.5 text-sm font-semibold ${riskStyle.bg} ${riskStyle.text}`}
        >
          {recommendation.riskLevel} Risk
        </span>
        <span className="rounded-full bg-gray-100 px-3 py-1.5 text-sm font-medium text-gray-700">
          Confidence:{' '}
          {(recommendation.confidenceScore * 100).toFixed(1)}%
        </span>
      </div>

      {/* Flags */}
      {(recommendation.hasSodViolation || recommendation.isAnomaly) && (
        <div className="flex flex-wrap gap-2">
          {recommendation.hasSodViolation && (
            <span className="inline-flex items-center gap-1 rounded-full bg-red-100 px-2.5 py-1 text-xs font-medium text-red-800">
              SoD Violation
            </span>
          )}
          {recommendation.isAnomaly && (
            <span className="inline-flex items-center gap-1 rounded-full bg-orange-100 px-2.5 py-1 text-xs font-medium text-orange-800">
              Anomaly Detected
            </span>
          )}
        </div>
      )}

      {/* AI Analysis / Risk Summary */}
      <Section title="AI Analysis">
        <p className="text-sm leading-relaxed text-foreground">
          {recommendation.riskSummary || 'No summary available.'}
        </p>
      </Section>

      {/* Risk Factors */}
      {recommendation.riskFactors && recommendation.riskFactors.length > 0 && (
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

      {/* Usage & Activity Details */}
      <Section title="Usage & Activity">
        <Field
          label="Usage Percentage"
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

      {/* Timestamp */}
      {recommendation.generatedAt && (
        <div className="border-t pt-3 text-xs text-muted-foreground">
          Generated:{' '}
          {new Date(recommendation.generatedAt).toLocaleString()}
        </div>
      )}

      {/* Feedback Collection Widget */}
      {recommendation.id && (
        <div className="border-t pt-4">
          <AIFeedbackWidget
            recommendationId={recommendation.id}
            aiDecision={decision}
            reviewItemId={stepId}
            isAlreadyActioned={parentData?.isApproved != null}
            actualIsApproved={parentData?.isApproved as boolean | null | undefined}
          />
        </div>
      )}

      {/* UC5: Interactive Reviewer Assistant Chat */}
      {certificationProcessId && stepId && (
        <div className="border-t pt-4">
          <ReviewerChatWidget
            certificationProcessId={certificationProcessId}
            stepId={stepId}
          />
        </div>
      )}
    </div>
  );
});

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
