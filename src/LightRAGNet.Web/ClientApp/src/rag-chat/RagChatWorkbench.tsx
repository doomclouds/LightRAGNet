import { useCallback, useEffect, useMemo, useRef, useState } from "react";

import { queryRagStream } from "../api/ragChatApi";
import type { ChatMessage, RagQueryReference } from "../types/ragChat";
import { ChatPane } from "./ChatPane";
import { QueryDetailsDialog } from "./QueryDetailsDialog";
import { QuerySettingsPanel } from "./QuerySettingsPanel";
import { buildRagQueryRequest, defaultQuerySettings, type QuerySettings } from "./ragChatSettings";

type Props = {
  apiBase: string;
  initialAssistantReferenceUrl?: string;
};

function createId(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }

  return `msg-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

export function RagChatWorkbench({ apiBase, initialAssistantReferenceUrl }: Props) {
  const [settings, setSettings] = useState<QuerySettings>(defaultQuerySettings);
  const [input, setInput] = useState("");
  const [messages, setMessages] = useState<ChatMessage[]>(() => createInitialMessages(initialAssistantReferenceUrl));
  const [activeDetailsMessageId, setActiveDetailsMessageId] = useState<string | null>(null);
  const [isRunning, setIsRunning] = useState(false);
  const abortRef = useRef<AbortController | null>(null);
  const isMountedRef = useRef(true);

  const activeDetailsMessage = useMemo(
    () => messages.find((message) => message.id === activeDetailsMessageId) ?? null,
    [activeDetailsMessageId, messages]
  );

  useEffect(() => {
    return () => {
      isMountedRef.current = false;
      abortRef.current?.abort();
    };
  }, []);

  const send = useCallback(async () => {
    const query = input.trim();

    if (!query || isRunning) {
      return;
    }

    const request = buildRagQueryRequest(query, settings);
    const userMessage: ChatMessage = {
      id: createId(),
      role: "User",
      text: query,
      isComplete: true,
      isStreaming: false,
      isLoadingRetrievalData: false
    };
    const assistantMessage: ChatMessage = {
      id: createId(),
      role: "Assistant",
      text: "",
      request,
      isComplete: false,
      isStreaming: request.stream,
      isLoadingRetrievalData: false
    };

    setInput("");
    setMessages((current) => [...current, userMessage, assistantMessage]);
    setIsRunning(true);
    const controller = new AbortController();
    abortRef.current = controller;

    try {
      await queryRagStream(apiBase, request, {
        signal: controller.signal,
        onChunk: (chunk) => {
          if (!isMountedRef.current || abortRef.current !== controller) {
            return;
          }

          setMessages((current) =>
            current.map((message) =>
              message.id === assistantMessage.id ? { ...message, text: message.text + chunk } : message
            )
          );
        },
        onMetadata: (metadata) => {
          if (!isMountedRef.current || abortRef.current !== controller) {
            return;
          }

          setMessages((current) =>
            current.map((message) => (message.id === assistantMessage.id ? { ...message, metadata } : message))
          );
        }
      });

      if (!isMountedRef.current || abortRef.current !== controller) {
        return;
      }

      setMessages((current) =>
        current.map((message) =>
          message.id === assistantMessage.id ? { ...message, isComplete: true, isStreaming: false } : message
        )
      );
    } catch (error) {
      if (!isMountedRef.current || abortRef.current !== controller) {
        return;
      }

      const message = error instanceof Error ? error.message : "Query failed.";
      setMessages((current) =>
        current.map((item) =>
          item.id === assistantMessage.id
            ? { ...item, isComplete: true, isStreaming: false, errorMessage: message, text: item.text || `Error: ${message}` }
            : item
        )
      );
    } finally {
      if (abortRef.current === controller) {
        abortRef.current = null;
      }

      if (isMountedRef.current) {
        setIsRunning(false);
      }
    }
  }, [apiBase, input, isRunning, settings]);

  return (
    <main className="rag-chat lrn-app">
      <section className="rag-chat__inner">
        <header className="lrn-page-head rag-chat__head">
          <div>
            <h1>RAG Chat</h1>
            <div className="lrn-page-meta">
              <span>{settings.mode}</span>
              <span>{settings.streamResponse ? "Streaming" : "Non-stream"}</span>
            </div>
          </div>
          <button
            className="lrn-button lrn-button--danger"
            type="button"
            disabled={isRunning || messages.length === 0}
            onClick={() => setMessages([])}
          >
            Clear History
          </button>
        </header>

        <div className="rag-chat__layout">
          <ChatPane
            input={input}
            isRunning={isRunning}
            messages={messages}
            onInputChange={setInput}
            onOpenDetails={(message) => setActiveDetailsMessageId(message.id)}
            onSend={() => void send()}
          />
          <QuerySettingsPanel settings={settings} disabled={isRunning} onChange={setSettings} />
        </div>
      </section>

      {activeDetailsMessage ? (
        <QueryDetailsDialog
          apiBase={apiBase}
          message={activeDetailsMessage}
          onClose={() => setActiveDetailsMessageId(null)}
          onUpdateMessage={(updated) =>
            setMessages((current) => current.map((message) => (message.id === updated.id ? updated : message)))
          }
        />
      ) : null}
    </main>
  );
}

function createInitialMessages(initialAssistantReferenceUrl?: string): ChatMessage[] {
  if (!initialAssistantReferenceUrl) {
    return [];
  }

  const reference: RagQueryReference = {
    referenceId: "1",
    filePath: "/uploads/doc.md",
    fileName: "doc.md",
    previewUrl: initialAssistantReferenceUrl,
    openKind: "DocumentPreview"
  };

  return [
    {
      id: "initial-assistant",
      role: "Assistant",
      text: "Preview reference",
      request: buildRagQueryRequest("preview reference", defaultQuerySettings),
      metadata: {
        type: "metadata",
        mode: "Mix",
        stream: false,
        includeReferences: true,
        responseType: "Multiple Paragraphs",
        cachePolicy: "Cacheable request",
        references: [reference],
        highLevelKeywords: [],
        lowLevelKeywords: [],
        diagnostics: { source: "test-preview" }
      },
      isComplete: true,
      isStreaming: false,
      isLoadingRetrievalData: false
    }
  ];
}
