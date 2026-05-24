import { useEffect, useRef } from "react";

import { getRagQueryData } from "../api/ragChatApi";
import type { ChatMessage } from "../types/ragChat";

type Props = {
  apiBase: string;
  message: ChatMessage;
  onClose: () => void;
  onUpdateMessage: (message: ChatMessage) => void;
};

export function QueryDetailsDialog({ apiBase, message, onClose, onUpdateMessage }: Props) {
  const abortRef = useRef<AbortController | null>(null);
  const isMountedRef = useRef(true);

  useEffect(() => {
    return () => {
      isMountedRef.current = false;
      abortRef.current?.abort();
      abortRef.current = null;
    };
  }, []);

  const loadRetrievalData = async () => {
    if (!message.request || message.retrievalData || message.isLoadingRetrievalData) {
      return;
    }

    const controller = new AbortController();
    abortRef.current?.abort();
    abortRef.current = controller;
    onUpdateMessage({ ...message, retrievalDataErrorMessage: undefined, isLoadingRetrievalData: true });

    try {
      const retrievalData = await getRagQueryData(apiBase, message.request, { signal: controller.signal });

      if (!isMountedRef.current || abortRef.current !== controller) {
        return;
      }

      onUpdateMessage({ ...message, retrievalData, retrievalDataErrorMessage: undefined, isLoadingRetrievalData: false });
    } catch (error) {
      if (!isMountedRef.current || abortRef.current !== controller) {
        return;
      }

      onUpdateMessage({
        ...message,
        retrievalDataErrorMessage: error instanceof Error ? error.message : "Failed to load retrieval data.",
        isLoadingRetrievalData: false
      });
    } finally {
      if (abortRef.current === controller) {
        abortRef.current = null;
      }
    }
  };

  return (
    <div className="rag-chat__dialog-backdrop" role="dialog" aria-modal="true" aria-labelledby="rag-chat-query-details-title">
      <div className="rag-chat__dialog lrn-dialog">
        <div className="lrn-panel__head">
          <h2 id="rag-chat-query-details-title">Query details</h2>
          <button className="lrn-icon-button" type="button" aria-label="Close query details" onClick={onClose}>
            x
          </button>
        </div>

        <div className="rag-chat__dialog-body">
          <div className="rag-chat__dialog-toolbar">
            <button
              className="lrn-button lrn-button--accent"
              type="button"
              disabled={!message.request || Boolean(message.retrievalData) || message.isLoadingRetrievalData}
              onClick={() => void loadRetrievalData()}
            >
              {message.isLoadingRetrievalData ? "Loading retrieval data" : "Load retrieval data"}
            </button>
            {message.retrievalDataErrorMessage ? <span className="rag-chat__error">{message.retrievalDataErrorMessage}</span> : null}
          </div>

          <section className="rag-chat__details-section">
            <h3>Request</h3>
            <pre className="lrn-code-surface">{JSON.stringify(message.request ?? null, null, 2)}</pre>
          </section>

          <section className="rag-chat__details-section">
            <h3>Metadata</h3>
            <pre className="lrn-code-surface">{JSON.stringify(message.metadata ?? null, null, 2)}</pre>
          </section>

          <section className="rag-chat__details-section">
            <h3>Retrieval Data</h3>
            <pre className="lrn-code-surface">
              {JSON.stringify(message.retrievalData ?? { status: "not_loaded" }, null, 2)}
            </pre>
          </section>

          <section className="rag-chat__details-section">
            <h3>Raw diagnostics</h3>
            <pre className="lrn-code-surface">{JSON.stringify(message.metadata?.diagnostics ?? {}, null, 2)}</pre>
          </section>
        </div>
      </div>
    </div>
  );
}
