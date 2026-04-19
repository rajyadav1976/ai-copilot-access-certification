import { useCallback, useMemo, useState } from 'react';

import { Button } from '@external/shadcn/components/ui/button';
import { Textarea } from '@external/shadcn/components/ui/textarea';
import { tableApiKeys } from '@platform/modules/table-api/utils/queryKeys';
import { httpPatch } from '@platform/utils/api/httpClient';
import { useQueryClient } from '@tanstack/react-query';

import { useSubmitAIFeedback } from '../hooks/useAIRecommendations';
import type { AIDecision } from '../types/ai-recommendations';

interface AIFeedbackWidgetProps {
  /** The SysId (GUID) of the AIRecommendation entity */
  recommendationId: string;
  /** The AI's original decision for display context */
  aiDecision: AIDecision;
  /** The WorkflowInstanceStep ID (primary key) for submitting the actual decision */
  reviewItemId?: number | string | null;
  /** Whether the review item has already been actioned (IsApproved is not null) */
  isAlreadyActioned?: boolean;
  /** The actual isApproved value from the review item (true=Approved, false=Rejected, null=pending) */
  actualIsApproved?: boolean | null;
}

type FeedbackState = 'idle' | 'open' | 'submitting' | 'submitted';

/**
 * Compact feedback collection widget that appears on the AI Recommendation detail screens.
 * Collects:
 *  - Whether the reviewer agrees with the AI recommendation
 *  - The reviewer's actual decision
 *  - Override reason (if they disagree)
 *  - Free-form comments
 *  - Quality rating (1-5 stars)
 *
 * This data feeds into the Feedback Learning Loop (Layer 2) to improve future recommendations
 * via few-shot prompt injection.
 */
export function AIFeedbackWidget({
  recommendationId,
  aiDecision,
  reviewItemId,
  isAlreadyActioned = false,
  actualIsApproved,
}: AIFeedbackWidgetProps) {
  const [state, setState] = useState<FeedbackState>('idle');
  const [agreedWithAI, setAgreedWithAI] = useState<boolean | null>(null);
  const [actualDecision, setActualDecision] = useState<string>('');
  const [overrideReason, setOverrideReason] = useState('');
  const [feedbackComments, setFeedbackComments] = useState('');
  const [qualityRating, setQualityRating] = useState(0);
  const [hoveredStar, setHoveredStar] = useState(0);
  const [decisionSubmitted, setDecisionSubmitted] = useState(isAlreadyActioned);

  const feedbackMutation = useSubmitAIFeedback();
  const queryClient = useQueryClient();

  const canSubmit = useMemo(() => {
    if (agreedWithAI === null) return false;
    if (!actualDecision) return false;
    if (!agreedWithAI && !overrideReason.trim()) return false;
    return true;
  }, [agreedWithAI, actualDecision, overrideReason]);

  const handleAgree = useCallback(() => {
    setAgreedWithAI(true);
    setActualDecision(aiDecision);
    setOverrideReason('');
  }, [aiDecision]);

  const handleDisagree = useCallback(() => {
    setAgreedWithAI(false);
    setActualDecision('');
  }, []);

  /** Submit feedback only */
  const handleSubmit = useCallback(async () => {
    if (!canSubmit) return;
    setState('submitting');
    try {
      await feedbackMutation.mutateAsync({
        recommendationId,
        feedback: {
          actualDecision,
          agreedWithAI: agreedWithAI!,
          overrideReason: overrideReason || undefined,
          feedbackComments: feedbackComments || undefined,
          qualityRating: qualityRating > 0 ? qualityRating : undefined,
        },
      });
      setState('submitted');
    } catch {
      setState('open');
    }
  }, [
    canSubmit,
    recommendationId,
    actualDecision,
    agreedWithAI,
    overrideReason,
    feedbackComments,
    qualityRating,
    feedbackMutation,
  ]);

  /**
   * "Agree & Submit" — agrees with AI, submits feedback,
   * AND patches the review item decision (IsApproved) on WorkflowInstanceSteps.
   */
  const handleAgreeAndSubmit = useCallback(async () => {
    if (!reviewItemId) return;

    // Determine IsApproved from AI decision
    const isApproved = aiDecision === 'Approve' ? true : aiDecision === 'Reject' ? false : null;
    if (isApproved === null) return; // NeedsReview can't be auto-submitted

    setState('submitting');
    try {
      // 1. Submit feedback (agree with AI)
      await feedbackMutation.mutateAsync({
        recommendationId,
        feedback: {
          actualDecision: aiDecision,
          agreedWithAI: true,
          feedbackComments: feedbackComments || undefined,
          qualityRating: qualityRating > 0 ? qualityRating : undefined,
        },
      });

      // 2. Patch the review item decision
      await httpPatch(
        `/api/v1/table/WorkflowInstanceSteps/${reviewItemId}`,
        { IsApproved: isApproved },
        { disableSuccessMessage: true, disableErrorMessage: true },
      );

      // 3. Invalidate ReviewItems table cache so the list reflects the new status
      queryClient.invalidateQueries({ queryKey: tableApiKeys.all('ReviewItems') });

      // Keep form open but mark decision as submitted so the button stays disabled
      setDecisionSubmitted(true);
      setAgreedWithAI(true);
      setActualDecision(aiDecision);
      setState('open');
    } catch {
      setState('open');
    }
  }, [
    reviewItemId,
    aiDecision,
    recommendationId,
    feedbackComments,
    qualityRating,
    feedbackMutation,
    queryClient,
  ]);

  // ─── Submitted confirmation ───
  if (state === 'submitted') {
    return (
      <div className="flex items-center gap-2 rounded-md border border-green-200 bg-green-50 p-3">
        <span className="text-sm font-medium text-green-800">
          Thank you! Your feedback helps improve future AI recommendations.
        </span>
      </div>
    );
  }

  // ─── Collapsed prompt ───
  if (state === 'idle') {
    return (
      <button
        onClick={() => setState('open')}
        className="flex w-full items-center gap-2 rounded-md border border-dashed border-blue-300 bg-blue-50/50 px-4 py-3 text-sm text-blue-700 transition-colors hover:bg-blue-100/50"
      >
        <span className="font-medium">Rate this AI recommendation</span>
        <span className="text-blue-500">
          — Help improve future recommendations
        </span>
      </button>
    );
  }

  // ─── Expanded feedback form ───
  return (
    <div className="space-y-4 rounded-md border bg-muted/20 p-4">
      <div className="flex items-center justify-between">
        <h4 className="text-sm font-semibold">
          Act &amp; Provide Feedback
        </h4>
        <button
          onClick={() => setState('idle')}
          className="text-muted-foreground hover:text-foreground"
        >
          ✕
        </button>
      </div>

      {/* Step 1: Agree / Disagree */}
      <div className="space-y-2">
        <p className="text-xs font-medium text-muted-foreground">
          Do you agree with the AI recommendation to{' '}
          <span className="font-semibold text-foreground">{aiDecision}</span>?
        </p>
        <div className="flex gap-2">
          <Button
            variant={agreedWithAI === true ? 'default' : 'outline'}
            size="sm"
            onClick={handleAgree}
            className={
              agreedWithAI === true
                ? 'bg-green-600 text-white hover:bg-green-700'
                : ''
            }
          >
            Agree
          </Button>
          <Button
            variant={agreedWithAI === false ? 'default' : 'outline'}
            size="sm"
            onClick={handleDisagree}
            className={
              agreedWithAI === false
                ? 'bg-red-600 text-white hover:bg-red-700'
                : ''
            }
          >
            Disagree
          </Button>
          {reviewItemId && aiDecision !== 'NeedsReview' && (
            <Button
              size="sm"
              disabled={state === 'submitting' || decisionSubmitted}
              onClick={handleAgreeAndSubmit}
              className={decisionSubmitted
                ? 'bg-gray-400 text-white cursor-not-allowed'
                : 'bg-green-600 text-white hover:bg-green-700'}
            >
              {state === 'submitting'
                ? 'Submitting…'
                : decisionSubmitted
                  ? `${(actualIsApproved != null ? actualIsApproved : aiDecision === 'Approve') ? 'Approved' : 'Rejected'} \u2713`
                  : `Agree & ${aiDecision === 'Approve' ? 'Approve' : 'Reject'}`}
            </Button>
          )}
        </div>
      </div>

      {/* Step 2: If disagreed, pick actual decision + reason */}
      {agreedWithAI === false && (
        <div className="space-y-3 rounded-md border border-amber-200 bg-amber-50/50 p-3">
          <div className="space-y-1.5">
            <label className="text-xs font-medium text-muted-foreground">
              Your decision:
            </label>
            <div className="flex gap-2">
              {(['Approve', 'Reject', 'NeedsReview'] as const).map((d) => (
                <Button
                  key={d}
                  variant={actualDecision === d ? 'default' : 'outline'}
                  size="sm"
                  onClick={() => setActualDecision(d)}
                >
                  {d === 'NeedsReview' ? 'Needs Review' : d}
                </Button>
              ))}
            </div>
          </div>
          <div className="space-y-1.5">
            <label className="text-xs font-medium text-muted-foreground">
              Why do you disagree? <span className="text-red-500">*</span>
            </label>
            <Textarea
              value={overrideReason}
              onChange={(e) => setOverrideReason(e.target.value)}
              placeholder="e.g., Role is needed for month-end processing despite low usage..."
              rows={2}
              className="text-sm"
            />
          </div>
        </div>
      )}

      {/* Step 3: Quality rating (stars) */}
      <div className="space-y-1.5">
        <label className="text-xs font-medium text-muted-foreground">
          How helpful was this recommendation? (optional)
        </label>
        <div className="flex gap-0.5">
          {[1, 2, 3, 4, 5].map((star) => (
            <button
              key={star}
              onMouseEnter={() => setHoveredStar(star)}
              onMouseLeave={() => setHoveredStar(0)}
              onClick={() => setQualityRating(star === qualityRating ? 0 : star)}
              className="p-0.5"
            >
              <span
                className={`inline-block text-lg leading-none transition-colors ${
                  star <= (hoveredStar || qualityRating)
                    ? 'text-yellow-400'
                    : 'text-gray-300'
                }`}
              >
                ★
              </span>
            </button>
          ))}
          {qualityRating > 0 && (
            <span className="ml-2 self-center text-xs text-muted-foreground">
              {qualityRating}/5
            </span>
          )}
        </div>
      </div>

      {/* Step 4: Additional comments */}
      <div className="space-y-1.5">
        <label className="text-xs font-medium text-muted-foreground">
          Additional comments (optional)
        </label>
        <Textarea
          value={feedbackComments}
          onChange={(e) => setFeedbackComments(e.target.value)}
          placeholder="Any additional context that could help improve recommendations..."
          rows={2}
          className="text-sm"
        />
      </div>

      {/* Submit buttons */}
      <div className="flex items-center justify-end gap-2">
        <Button
          size="sm"
          variant="outline"
          disabled={!canSubmit || state === 'submitting'}
          onClick={handleSubmit}
        >
          {state === 'submitting' ? 'Submitting…' : 'Submit Feedback'}
        </Button>
      </div>
    </div>
  );
}
