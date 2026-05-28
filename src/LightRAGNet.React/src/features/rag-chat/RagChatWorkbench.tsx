import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { History, Plus, Trash2, Upload } from "lucide-react";

import { queryRagStream } from "@/api/ragChatApi";
import type { ChatMessage, RagQueryReference } from "@/types/ragChat";
import { ChatPane } from "./ChatPane";
import { QueryDetailsDialog } from "./QueryDetailsDialog";
import { QuerySettingsPanel } from "./QuerySettingsPanel";
import { buildRagQueryRequest, defaultQuerySettings, type QuerySettings } from "./ragChatSettings";
import "@/features/rag-chat/rag-chat.css";

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
    isMountedRef.current = true;

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
    <section className="rag-chat">
      <section className="rag-chat__workbench">
        <header className="rag-chat__topline">
          <div className="rag-chat__heading">
            <div className="rag-chat__title-row">
              <h1>RAG Chat</h1>
              <span className="rag-chat__status-chip">{settings.mode}</span>
              <span className="rag-chat__status-chip">{settings.streamResponse ? "Streaming" : "Non-stream"}</span>
            </div>
            <p>Query the document store and knowledge graph from one focused workspace.</p>
          </div>
          <div className="rag-chat__header-actions">
            <button className="rag-chat__utility-action" type="button" aria-label="Open conversation history">
              <History size={16} aria-hidden="true" />
            </button>
            <button className="rag-chat__utility-action" type="button" aria-label="Upload context document">
              <Upload size={16} aria-hidden="true" />
            </button>
            <button
              className="rag-chat__primary-action"
              type="button"
              disabled={isRunning}
              onClick={() => setMessages([])}
            >
              <Plus size={16} aria-hidden="true" />
              <span>New conversation</span>
            </button>
            <button
              className="rag-chat__danger-action"
              type="button"
              aria-label="Clear conversation history"
              disabled={isRunning || messages.length === 0}
              onClick={() => setMessages([])}
            >
              <Trash2 size={16} aria-hidden="true" />
            </button>
          </div>
        </header>

        <div className={`rag-chat__layout rag-chat__layout--fixed ${messages.length === 0 ? "rag-chat__layout--empty" : ""}`}>
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
    </section>
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
