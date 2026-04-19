import { memo } from 'react';

import type { AIDecision } from '../types/ai-recommendations';

const DECISION_CONFIG: Record<
  AIDecision,
  { label: string; bg: string; text: string }
> = {
  Approve: {
    label: 'Approve',
    bg: 'bg-green-100',
    text: 'text-green-700',
  },
  Reject: {
    label: 'Reject',
    bg: 'bg-red-100',
    text: 'text-red-700',
  },
  NeedsReview: {
    label: 'Review',
    bg: 'bg-amber-100',
    text: 'text-amber-700',
  },
};

interface AIRecommendationBadgeProps {
  /** The AI decision */
  decision: AIDecision;
  /** Confidence score between 0 and 1 */
  confidenceScore: number;
  /** Whether to show the confidence percentage */
  showConfidence?: boolean;
  /** Additional class names */
  className?: string;
}

/**
 * Compact badge displaying the AI recommendation decision.
 * Designed to be placed inline on review item rows.
 */
export const AIRecommendationBadge = memo(function AIRecommendationBadge({
  decision,
  confidenceScore,
  showConfidence = true,
  className = '',
}: AIRecommendationBadgeProps) {
  const config = DECISION_CONFIG[decision] ?? DECISION_CONFIG.NeedsReview;

  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${config.bg} ${config.text} ${className}`}
      title={`AI suggests: ${config.label} (${(confidenceScore * 100).toFixed(1)}% confidence)`}
    >
      {config.label}
      {showConfidence && (
        <span className="font-mono">{(confidenceScore * 100).toFixed(0)}%</span>
      )}
    </span>
  );
});
