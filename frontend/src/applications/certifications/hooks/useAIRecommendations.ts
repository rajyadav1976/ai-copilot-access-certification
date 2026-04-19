import { useEffect, useRef } from 'react';

import { httpGet, httpPost } from '@platform/utils/api/httpClient';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import type {
  AIRecommendationDto,
  AIRecommendationFeedbackRequest,
  AIRecommendationSummary,
  ChatTurn,
  GenerateRecommendationsRequest,
  ReviewerChatResponse,
} from '../types/ai-recommendations';

const AI_REC_BASE_URL = '/certifications/ai-recommendations';

/**
 * Hook to fetch AI recommendations for a campaign.
 */
export function useAIRecommendations(certificationProcessId: number | null) {
  return useQuery({
    queryKey: ['ai-recommendations', certificationProcessId],
    queryFn: async () => {
      return httpGet<AIRecommendationDto[]>(
        `${AI_REC_BASE_URL}/${certificationProcessId}`,
      );
    },
    enabled: !!certificationProcessId,
    staleTime: 60_000,
    refetchOnMount: true,
  });
}

/**
 * Hook to fetch a single recommendation by step ID.
 */
export function useAIRecommendationByStep(
  certificationProcessId: number | null,
  stepId: number | null,
) {
  return useQuery({
    queryKey: ['ai-recommendation-step', certificationProcessId, stepId],
    queryFn: async () => {
      return httpGet<AIRecommendationDto>(
        `${AI_REC_BASE_URL}/${certificationProcessId}/step/${stepId}`,
      );
    },
    enabled: !!certificationProcessId && !!stepId,
    staleTime: 60_000,
  });
}

/**
 * Hook to fetch AI recommendation summary for a campaign.
 * Automatically polls every 3 seconds while status is "Processing".
 */
export function useAIRecommendationSummary(
  certificationProcessId: number | null,
) {
  const queryClient = useQueryClient();
  const previousStatusRef = useRef<string | undefined>(undefined);

  const query = useQuery({
    queryKey: ['ai-recommendation-summary', certificationProcessId],
    queryFn: async () => {
      return httpGet<AIRecommendationSummary>(
        `${AI_REC_BASE_URL}/${certificationProcessId}/summary`,
      );
    },
    enabled: !!certificationProcessId,
    staleTime: 5_000,
    refetchOnMount: true,
    // Poll every 3s while Processing, stop when status changes
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === 'Processing' ? 3_000 : false;
    },
  });

  // When status transitions from "Processing" to another state, refresh recommendations
  useEffect(() => {
    const currentStatus = query.data?.status;
    if (
      previousStatusRef.current === 'Processing' &&
      currentStatus &&
      currentStatus !== 'Processing'
    ) {
      queryClient.invalidateQueries({
        queryKey: ['ai-recommendations', certificationProcessId],
      });
      queryClient.invalidateQueries({
        queryKey: ['ai-recommendations-exist', certificationProcessId],
      });
    }
    previousStatusRef.current = currentStatus;
  }, [query.data?.status, certificationProcessId, queryClient]);

  return query;
}

/**
 * Hook to check if recommendations exist for a campaign.
 */
export function useAIRecommendationsExist(
  certificationProcessId: number | null,
) {
  return useQuery({
    queryKey: ['ai-recommendations-exist', certificationProcessId],
    queryFn: async () => {
      return httpGet<{ exists: boolean }>(
        `${AI_REC_BASE_URL}/${certificationProcessId}/exists`,
      );
    },
    enabled: !!certificationProcessId,
    staleTime: 30_000,
  });
}

/**
 * Hook to trigger AI recommendation generation.
 * The backend returns immediately with "Processing" status.
 * The summary hook auto-polls until generation completes.
 */
export function useGenerateAIRecommendations(
  certificationProcessId: number | null,
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request?: GenerateRecommendationsRequest) => {
      return httpPost<AIRecommendationSummary>(
        `${AI_REC_BASE_URL}/${certificationProcessId}/generate`,
        request ?? {},
      );
    },
    onMutate: () => {
      // Optimistically set summary to "Processing" immediately for instant UI feedback
      if (certificationProcessId) {
        queryClient.setQueryData(
          ['ai-recommendation-summary', certificationProcessId],
          (old: AIRecommendationSummary | undefined) => ({
            certificationProcessId,
            totalItems: old?.totalItems ?? 0,
            recommendedApprove: 0,
            recommendedReject: 0,
            needsReview: 0,
            highRiskCount: 0,
            anomalyCount: 0,
            sodViolationCount: 0,
            averageConfidence: 0,
            status: 'Processing' as const,
            generatedAt: old?.generatedAt,
          }),
        );
      }
    },
    onSuccess: (data) => {
      // Update summary cache with server response (may be "Processing" or "Generated")
      if (certificationProcessId) {
        queryClient.setQueryData(
          ['ai-recommendation-summary', certificationProcessId],
          data,
        );
      }
    },
    onError: () => {
      // On error, refetch summary to get the actual state
      queryClient.invalidateQueries({
        queryKey: ['ai-recommendation-summary', certificationProcessId],
      });
    },
  });
}

/**
 * Hook to submit feedback on an AI recommendation.
 */
export function useSubmitAIFeedback() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      recommendationId,
      feedback,
    }: {
      recommendationId: string;
      feedback: AIRecommendationFeedbackRequest;
    }) => {
      return httpPost(
        `${AI_REC_BASE_URL}/${recommendationId}/feedback`,
        feedback,
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ['ai-recommendations'],
      });
    },
  });
}

/**
 * Hook for the UC5 Interactive Reviewer Assistant chat.
 * Sends a question to the backend agent endpoint, which uses tools to investigate
 * and returns an evidence-based answer with suggested follow-ups.
 */
export function useReviewerChat(
  certificationProcessId: number | null,
  stepId: number | null,
) {
  return useMutation({
    mutationFn: async ({
      question,
      conversationHistory,
    }: {
      question: string;
      conversationHistory?: ChatTurn[];
    }) => {
      return httpPost<ReviewerChatResponse>(
        `${AI_REC_BASE_URL}/${certificationProcessId}/step/${stepId}/chat`,
        { question, conversationHistory },
      );
    },
  });
}
