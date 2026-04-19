import { memo, useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';

import { useNexusNavigate } from '@platform/hooks/useNexusNavigate';

import {
  useAIRecommendations,
  useAIRecommendationSummary,
  useGenerateAIRecommendations,
} from '../hooks/useAIRecommendations';
import type {
  AIDecision,
  AIRecommendationDto,
  AIRiskLevel,
} from '../types/ai-recommendations';

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
 * AI Insights Tab component for the MyCertificationsForm.
 * Registered as a "coded" tab and rendered by the platform's CodedTab component.
 */
export const AIInsightsTab = memo(function AIInsightsTab() {
  const [searchParams] = useSearchParams();
  const certificationProcessId = Number(searchParams.get('id')) || null;
  const { navigate } = useNexusNavigate();

  const handleOpenDetail = useCallback(
    (rec: AIRecommendationDto) => {
      sessionStorage.setItem(
        'ai-recommendation-detail',
        JSON.stringify(rec),
      );
      navigate('/AIRecommendationDetails');
    },
    [navigate],
  );

  const {
    data: summary,
    isLoading: isSummaryLoading,
    error: summaryError,
  } = useAIRecommendationSummary(certificationProcessId);

  const {
    data: recommendations,
    isLoading: isRecsLoading,
    error: recsError,
  } = useAIRecommendations(certificationProcessId);

  const {
    mutateAsync: generateRecommendations,
    isPending: isMutating,
  } = useGenerateAIRecommendations(certificationProcessId);

  const isLoading = isSummaryLoading || isRecsLoading;
  const error = summaryError || recsError;
  // "Processing" can come from the summary (backend still working) or from the mutation in-flight
  const isProcessing = summary?.status === 'Processing' || isMutating;

  // Sort recommendations: high risk first, then by confidence ascending
  const sortedRecommendations = useMemo(() => {
    if (!recommendations) return [];
    return [...recommendations].sort((a, b) => {
      const riskOrder: Record<AIRiskLevel, number> = {
        Critical: 0,
        High: 1,
        Medium: 2,
        Low: 3,
      };
      const riskDiff =
        (riskOrder[a.riskLevel] ?? 2) - (riskOrder[b.riskLevel] ?? 2);
      if (riskDiff !== 0) return riskDiff;
      return a.confidenceScore - b.confidenceScore;
    });
  }, [recommendations]);

  if (!certificationProcessId) {
    return (
      <div className="flex items-center justify-center p-8 text-muted-foreground">
        No campaign selected
      </div>
    );
  }

  const handleGenerate = async (forceRegenerate = false) => {
    await generateRecommendations({ forceRegenerate });
  };

  return (
    <div className="flex h-full flex-col gap-4 overflow-auto p-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h2 className="text-lg font-semibold">AI Recommendation Insights</h2>
        </div>
        <div className="flex gap-2">
          {summary?.status === 'Generated' && (
            <button
              onClick={() => handleGenerate(true)}
              disabled={isProcessing}
              className="inline-flex items-center gap-1.5 rounded-md border border-input bg-background px-3 py-1.5 text-sm font-medium hover:bg-accent disabled:opacity-50"
            >
              Regenerate
            </button>
          )}
          {summary?.status === 'Processing' && (
            <button
              disabled
              className="inline-flex items-center gap-1.5 rounded-md border border-violet-300 bg-violet-50 px-3 py-1.5 text-sm font-medium text-violet-700 opacity-80"
            >
              Processing...
            </button>
          )}
          {summary?.status === 'Failed' && (
            <button
              onClick={() => handleGenerate(true)}
              disabled={isProcessing}
              className="inline-flex items-center gap-1.5 rounded-md bg-red-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50"
            >
              {isProcessing ? 'Retrying...' : 'Retry Generation'}
            </button>
          )}
          {(!summary || summary.status === 'NotGenerated') && (
            <button
              onClick={() => handleGenerate(false)}
              disabled={isProcessing}
              className="inline-flex items-center gap-1.5 rounded-md bg-violet-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-violet-700 disabled:opacity-50"
            >
              {isProcessing ? 'Generating...' : 'Generate AI Recommendations'}
            </button>
          )}
        </div>
      </div>

      {/* Error state */}
      {error && (
        <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          Error loading recommendations:{' '}
          {error instanceof Error ? error.message : 'Unknown error'}
        </div>
      )}

      {/* Processing state — shown while backend is generating */}
      {isProcessing && !isLoading && (
        <div className="flex flex-col items-center justify-center gap-3 rounded-md border border-violet-200 bg-violet-50 p-8">
          <div className="text-center">
            <p className="font-medium text-violet-800">
              Generating AI recommendations...
            </p>
            <p className="mt-1 text-sm text-violet-600">
              Analyzing review items with AI agents. This may take a few minutes.
            </p>
          </div>
        </div>
      )}

      {/* Initial loading state */}
      {isLoading && (
        <div className="flex items-center justify-center gap-2 p-8 text-muted-foreground">
          <span>Loading AI insights...</span>
        </div>
      )}

      {/* Summary cards */}
      {summary && summary.status === 'Generated' && !isLoading && (
        <>
          <SummaryCards summary={summary} />

          {/* Partial failure banner */}
          {summary.failedCount > 0 && (
            <div className="rounded-md border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
              <strong>{summary.failedCount}</strong> of {summary.totalItems} items failed LLM generation.
              Expand failed rows below to see error details. Click <strong>Regenerate</strong> to retry.
            </div>
          )}

          {/* Recommendations table (always full width) */}
          <div className="flex min-h-0 flex-1 flex-col rounded-md border">
            <div className="border-b bg-muted/40 px-4 py-2">
              <h3 className="text-sm font-medium">
                Review Item Recommendations ({sortedRecommendations.length})
              </h3>
            </div>
            <div className="flex-1 overflow-auto">
              <table className="w-full text-sm">
                <thead className="sticky top-0 bg-background">
                  <tr className="border-b">
                    <th className="w-8 px-1 py-2"></th>
                    <th className="px-3 py-2 text-left font-medium">
                      Step ID
                    </th>
                    <th className="px-3 py-2 text-left font-medium">
                      Decision
                    </th>
                    <th className="px-3 py-2 text-left font-medium">
                      Confidence
                    </th>
                    <th className="px-3 py-2 text-left font-medium">Risk</th>
                    <th className="px-3 py-2 text-left font-medium">
                      Usage %
                    </th>
                    <th className="px-3 py-2 text-left font-medium">
                      Days Unused
                    </th>
                    <th className="px-3 py-2 text-left font-medium">Flags</th>
                    <th className="px-3 py-2 text-left font-medium">
                      Status
                    </th>
                    <th className="px-3 py-2 text-left font-medium">
                      Summary
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {sortedRecommendations.map((rec) => (
                    <RecommendationRow
                      key={rec.id}
                      recommendation={rec}
                      onOpen={() => handleOpenDetail(rec)}
                    />
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}

      {/* Failed state */}
      {summary &&
        summary.status === 'Failed' &&
        !isLoading &&
        !isProcessing && (
          <div className="flex flex-col items-center justify-center gap-3 rounded-md border border-red-200 bg-red-50 p-8">
            <p className="text-center text-sm text-red-800">
              AI recommendation generation failed for all {summary.totalItems}{' '}
              items.
              <br />
              This may be caused by a temporary Azure OpenAI service issue.
              <br />
              Click <strong>Retry Generation</strong> above to try again.
            </p>
          </div>
        )}

      {/* No recommendations state */}
      {summary &&
        summary.status === 'NotGenerated' &&
        !isLoading &&
        !isProcessing && (
          <div className="flex flex-col items-center justify-center gap-3 rounded-md border border-dashed p-8 text-muted-foreground">
            <p className="text-center">
              No AI recommendations have been generated for this campaign yet.
              <br />
              Click the button above to analyze all review items.
            </p>
          </div>
        )}
    </div>
  );
});

/** Summary statistics cards */
function SummaryCards({
  summary,
}: {
  summary: {
    totalItems: number;
    recommendedApprove: number;
    recommendedReject: number;
    needsReview: number;
    highRiskCount: number;
    anomalyCount: number;
    sodViolationCount: number;
    averageConfidence: number;
    failedCount: number;
    generatedAt: string | null;
  };
}) {
  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-7">
      <StatCard
        label="Total Items"
        value={summary.totalItems}
      />
      <StatCard
        label="Approve"
        value={summary.recommendedApprove}
        className="border-green-200"
      />
      <StatCard
        label="Reject"
        value={summary.recommendedReject}
        className="border-red-200"
      />
      <StatCard
        label="Needs Review"
        value={summary.needsReview}
        className="border-amber-200"
      />
      <StatCard
        label="High Risk"
        value={summary.highRiskCount}
        className="border-red-200"
      />
      <StatCard
        label="Anomalies"
        value={summary.anomalyCount}
        className="border-orange-200"
      />
      <StatCard
        label="Avg Confidence"
        value={`${(summary.averageConfidence * 100).toFixed(1)}%`}
        className="border-blue-200"
      />
      {summary.failedCount > 0 && (
        <StatCard
          label="LLM Failed"
          value={summary.failedCount}
          className="border-red-300 bg-red-50"
        />
      )}
    </div>
  );
}

/** Single stat card */
function StatCard({
  label,
  value,
  className = '',
}: {
  label: string;
  value: number | string;
  className?: string;
}) {
  return (
    <div className={`rounded-md border p-3 ${className}`}>
      <span className="text-xs text-muted-foreground">{label}</span>
      <div className="mt-1 text-xl font-semibold">{value}</div>
    </div>
  );
}

/** Single recommendation row — clicks open the detail flyout */
function RecommendationRow({
  recommendation,
  onOpen,
}: {
  recommendation: AIRecommendationDto;
  onOpen: () => void;
}) {
  const decision = recommendation.decision as AIDecision;
  const riskLevel = recommendation.riskLevel as AIRiskLevel;
  const isFailed = recommendation.status === 'Failed';
  const decisionStyle = DECISION_STYLES[decision] ?? DECISION_STYLES.NeedsReview;
  const riskStyle = RISK_STYLES[riskLevel] ?? RISK_STYLES.Medium;

  return (
    <tr
      className={`cursor-pointer border-b last:border-b-0 hover:bg-muted/30 ${isFailed ? 'bg-red-50/50' : ''}`}
      onClick={onOpen}
    >
      <td className="w-8 px-1 py-2 text-center">
        <svg className="inline-block h-3.5 w-3.5 text-muted-foreground/60" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <polyline points="15 3 21 3 21 9" />
          <line x1="21" y1="3" x2="14" y2="10" />
          <polyline points="9 21 3 21 3 15" />
          <line x1="3" y1="21" x2="10" y2="14" />
        </svg>
      </td>
      <td className="px-3 py-2">
        <span className="font-mono text-xs">
          {recommendation.reviewItemStepId}
        </span>
      </td>
      <td className="px-3 py-2">
        <span
          className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${decisionStyle.bg} ${decisionStyle.text}`}
        >
          {recommendation.decision}
        </span>
      </td>
      <td className="px-3 py-2">
        <span className="font-mono text-xs">
          {(recommendation.confidenceScore * 100).toFixed(1)}%
        </span>
      </td>
      <td className="px-3 py-2">
        <span
          className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${riskStyle.bg} ${riskStyle.text}`}
        >
          {recommendation.riskLevel}
        </span>
      </td>
      <td className="px-3 py-2 text-xs">
        {recommendation.usagePercentage != null
          ? `${recommendation.usagePercentage}%`
          : '-'}
      </td>
      <td className="px-3 py-2 text-xs">
        {recommendation.daysSinceLastUsed != null
          ? `${recommendation.daysSinceLastUsed}d`
          : 'Never'}
      </td>
      <td className="px-3 py-2">
        <div className="flex gap-1">
          {recommendation.hasSodViolation && (
            <span
              className="rounded-full bg-red-100 px-1.5 py-0.5 text-[10px] font-medium text-red-800"
              title="Separation of Duties violation"
            >
              SoD
            </span>
          )}
          {recommendation.isAnomaly && (
            <span
              className="rounded-full bg-orange-100 px-1.5 py-0.5 text-[10px] font-medium text-orange-800"
              title={`Anomaly score: ${recommendation.anomalyScore?.toFixed(2)}`}
            >
              Anomaly
            </span>
          )}
        </div>
      </td>
      <td className="px-3 py-2">
        {isFailed ? (
          <span
            className="inline-flex rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-800"
            title={recommendation.errorMessage ?? 'LLM call failed'}
          >
            LLM Failed
          </span>
        ) : (
          <span className="inline-flex rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800">
            Generated
          </span>
        )}
      </td>
      <td className="max-w-xs truncate px-3 py-2 text-xs text-muted-foreground">
        {isFailed
          ? <span className="text-red-600" title={recommendation.errorMessage ?? undefined}>{recommendation.errorMessage ?? 'LLM call failed'}</span>
          : (recommendation.riskSummary ?? '-')}
      </td>
    </tr>
  );
}
