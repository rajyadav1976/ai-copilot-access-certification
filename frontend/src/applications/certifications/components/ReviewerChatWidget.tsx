import {
  memo,
  useCallback,
  useEffect,
  useRef,
  useState,
} from 'react';

import { Button } from '@external/shadcn/components/ui/button';
import { Textarea } from '@external/shadcn/components/ui/textarea';
import { cn } from '@external/shadcn/lib/utils';

import { useReviewerChat } from '../hooks/useAIRecommendations';
import type {
  AgentReasoningStep,
  ChatTurn,
} from '../types/ai-recommendations';

// ─── Types ───────────────────────────────────────────────────────

interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  timestamp: Date;
  reasoningSteps?: AgentReasoningStep[];
  suggestedFollowUps?: string[];
  tokensUsed?: number;
}

interface ReviewerChatWidgetProps {
  certificationProcessId: number;
  stepId: number;
}

// ─── Constants ───────────────────────────────────────────────────

const QUICK_QUESTIONS = [
  'Why was this flagged?',
  'What would happen if I approve this?',
  'Do similar users have this role?',
  'Is there a history of SoD violations?',
];

// ─── Component ───────────────────────────────────────────────────

/**
 * UC5: Interactive Reviewer Assistant — conversational chat widget.
 *
 * Allows certification reviewers to ask natural-language questions about a
 * specific review item. The backend agentic assistant investigates using
 * tools (peer analysis, SoD checks, historical decisions, etc.) and returns
 * evidence-based answers with suggested follow-ups.
 *
 * Maintains full multi-turn conversation history in local state and sends
 * it to the backend so that the assistant has conversational context.
 */
export const ReviewerChatWidget = memo(function ReviewerChatWidget({
  certificationProcessId,
  stepId,
}: ReviewerChatWidgetProps) {
  // ── State ──────────────────────────────────────────────────
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [isOpen, setIsOpen] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);

  const { mutateAsync: sendMessage, isPending } = useReviewerChat(
    certificationProcessId,
    stepId,
  );

  // ── Auto-scroll when new messages arrive ───────────────────
  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages, isPending]);

  // ── Focus input when chat opens ────────────────────────────
  useEffect(() => {
    if (isOpen) {
      setTimeout(() => inputRef.current?.focus(), 150);
    }
  }, [isOpen]);

  // ── Build conversation history for backend ─────────────────
  const buildHistory = useCallback((): ChatTurn[] => {
    return messages.map((m) => ({
      role: m.role,
      content: m.content,
    }));
  }, [messages]);

  // ── Send question ──────────────────────────────────────────
  const handleSend = useCallback(
    async (question?: string) => {
      const text = (question ?? input).trim();
      if (!text || isPending) return;

      // Add user message
      const userMsg: ChatMessage = {
        id: `user-${Date.now()}`,
        role: 'user',
        content: text,
        timestamp: new Date(),
      };
      setMessages((prev) => [...prev, userMsg]);
      setInput('');

      try {
        const response = await sendMessage({
          question: text,
          conversationHistory: buildHistory(),
        });

        const assistantMsg: ChatMessage = {
          id: `asst-${Date.now()}`,
          role: 'assistant',
          content: response.answer,
          timestamp: new Date(),
          reasoningSteps: response.reasoningSteps,
          suggestedFollowUps: response.suggestedFollowUps,
          tokensUsed: response.tokensUsed,
        };
        setMessages((prev) => [...prev, assistantMsg]);
      } catch {
        const errorMsg: ChatMessage = {
          id: `err-${Date.now()}`,
          role: 'assistant',
          content:
            'Sorry, I encountered an error processing your question. Please try again.',
          timestamp: new Date(),
        };
        setMessages((prev) => [...prev, errorMsg]);
      }
    },
    [input, isPending, sendMessage, buildHistory],
  );

  // ── Keyboard handler ──────────────────────────────────────
  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        handleSend();
      }
    },
    [handleSend],
  );

  // ── Collapsed state ────────────────────────────────────────
  if (!isOpen) {
    return (
      <button
        onClick={() => setIsOpen(true)}
        className="flex w-full items-center gap-2 rounded-lg border border-dashed border-blue-300 bg-blue-50/50 p-3 text-left transition-colors hover:bg-blue-50"
      >
        <div className="flex-1">
          <span className="text-sm font-medium text-blue-800">
            Ask AI Assistant
          </span>
          <p className="text-xs text-blue-600/70">
            Ask questions about this review item — the AI will investigate
            using real data
          </p>
        </div>
        <span className="text-sm text-blue-400">▼</span>
      </button>
    );
  }

  // ── Expanded chat ──────────────────────────────────────────
  return (
    <div className="flex flex-col overflow-hidden rounded-lg border border-blue-200 bg-white shadow-sm">
      {/* Header */}
      <div className="flex items-center justify-between border-b bg-blue-50 px-3 py-2">
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-blue-800">
            AI Reviewer Assistant
          </span>
          {isPending && (
            <span className="flex items-center gap-1 text-xs text-blue-500">
              Investigating…
            </span>
          )}
        </div>
        <button
          onClick={() => setIsOpen(false)}
          className="rounded p-0.5 text-blue-400 hover:bg-blue-100 hover:text-blue-600"
          title="Collapse"
        >
          <span className="text-sm">▲</span>
        </button>
      </div>

      {/* Messages area */}
      <div
        ref={scrollRef}
        className="flex max-h-80 min-h-[10rem] flex-col gap-3 overflow-y-auto p-3"
      >
        {messages.length === 0 && !isPending && (
          <EmptyState onAsk={handleSend} />
        )}

        {messages.map((msg) => (
          <MessageBubble key={msg.id} message={msg} onAsk={handleSend} />
        ))}

        {isPending && <ThinkingIndicator />}
      </div>

      {/* Input area */}
      <div className="flex items-end gap-2 border-t bg-gray-50/50 p-2">
        <Textarea
          ref={inputRef}
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Ask about this review item…"
          className="min-h-[2.5rem] max-h-24 resize-none rounded-md border-gray-200 bg-white text-sm"
          disabled={isPending}
          rows={1}
        />
        <Button
          size="icon"
          variant="default"
          onClick={() => handleSend()}
          disabled={!input.trim() || isPending}
          className="h-9 w-9 shrink-0 bg-blue-600 hover:bg-blue-700"
          title="Send"
        >
          {isPending ? '…' : '➤'}
        </Button>
      </div>
    </div>
  );
});

// ─── Sub-components ──────────────────────────────────────────────

/** Empty state with quick-start suggestions */
function EmptyState({ onAsk }: { onAsk: (q: string) => void }) {
  return (
    <div className="flex flex-col items-center gap-3 py-4 text-center">
      <div>
        <p className="text-sm font-medium text-gray-600">
          How can I help with this review?
        </p>
        <p className="mt-0.5 text-xs text-gray-400">
          I can investigate usage data, peer comparisons, SoD violations, and
          more
        </p>
      </div>
      <div className="flex flex-wrap justify-center gap-1.5">
        {QUICK_QUESTIONS.map((q) => (
          <button
            key={q}
            onClick={() => onAsk(q)}
            className="rounded-full border border-blue-200 bg-blue-50 px-2.5 py-1 text-xs text-blue-700 transition-colors hover:bg-blue-100"
          >
            {q}
          </button>
        ))}
      </div>
    </div>
  );
}

/** Single chat message bubble */
function MessageBubble({
  message,
  onAsk,
}: {
  message: ChatMessage;
  onAsk: (q: string) => void;
}) {
  const isUser = message.role === 'user';

  return (
    <div className={cn('flex gap-2', isUser ? 'justify-end' : 'justify-start')}>
      {!isUser && (
        <div className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-blue-100">
          <span className="text-xs text-blue-600">AI</span>
        </div>
      )}

      <div
        className={cn(
          'max-w-[85%] space-y-2 rounded-lg px-3 py-2 text-sm',
          isUser
            ? 'bg-blue-600 text-white'
            : 'bg-gray-100 text-gray-800',
        )}
      >
        {/* Answer text */}
        <p className="whitespace-pre-wrap leading-relaxed">{message.content}</p>

        {/* Reasoning trace (collapsible) */}
        {!isUser &&
          message.reasoningSteps &&
          message.reasoningSteps.length > 0 && (
            <ReasoningTrace steps={message.reasoningSteps} />
          )}

        {/* Suggested follow-ups */}
        {!isUser &&
          message.suggestedFollowUps &&
          message.suggestedFollowUps.length > 0 && (
            <div className="flex flex-wrap gap-1 pt-1">
              {message.suggestedFollowUps.map((q) => (
                <button
                  key={q}
                  onClick={() => onAsk(q)}
                  className="rounded-full border border-blue-200 bg-white px-2 py-0.5 text-xs text-blue-600 transition-colors hover:bg-blue-50"
                >
                  {q}
                </button>
              ))}
            </div>
          )}

        {/* Tokens badge */}
        {!isUser && message.tokensUsed != null && message.tokensUsed > 0 && (
          <div className="pt-0.5 text-[10px] text-gray-400">
            {message.tokensUsed.toLocaleString()} tokens used
          </div>
        )}
      </div>

      {isUser && (
        <div className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-gray-200">
          <span className="text-xs text-gray-600">☰</span>
        </div>
      )}
    </div>
  );
}

/** Collapsible reasoning trace showing which tools the agent called */
function ReasoningTrace({ steps }: { steps: AgentReasoningStep[] }) {
  const [expanded, setExpanded] = useState(false);

  return (
    <div className="rounded border border-gray-200 bg-white/60">
      <button
        onClick={() => setExpanded(!expanded)}
        className="flex w-full items-center gap-1.5 px-2 py-1 text-xs text-gray-500 hover:text-gray-700"
      >
        <span>
          {steps.length} tool{steps.length !== 1 ? 's' : ''} used
        </span>
        <span className="ml-auto text-[10px]">{expanded ? '▲' : '▼'}</span>
      </button>
      {expanded && (
        <div className="space-y-1 border-t px-2 py-1.5">
          {steps.map((step) => (
            <div
              key={step.stepNumber}
              className="flex items-start gap-1.5 text-[11px]"
            >
              <span className="mt-px shrink-0 rounded bg-gray-200 px-1 font-mono text-gray-500">
                {step.stepNumber}
              </span>
              <div className="min-w-0 flex-1">
                <span className="font-medium text-gray-700">
                  {step.toolName ?? step.action}
                </span>
                {step.toolResult && (
                  <p className="mt-0.5 truncate text-gray-400">
                    {step.toolResult.slice(0, 120)}
                    {step.toolResult.length > 120 ? '…' : ''}
                  </p>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/** Animated "thinking" indicator while the backend agent is working */
function ThinkingIndicator() {
  return (
    <div className="flex items-center gap-2">
      <div className="flex h-6 w-6 items-center justify-center rounded-full bg-blue-100">
        <span className="text-xs text-blue-600">AI</span>
      </div>
      <div className="flex items-center gap-1 rounded-lg bg-gray-100 px-3 py-2">
        <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-gray-400 [animation-delay:0ms]" />
        <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-gray-400 [animation-delay:150ms]" />
        <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-gray-400 [animation-delay:300ms]" />
        <span className="ml-2 text-xs text-gray-400">
          Investigating…
        </span>
      </div>
    </div>
  );
}
