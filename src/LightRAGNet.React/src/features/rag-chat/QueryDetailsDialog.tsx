import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";

import { getRagQueryData } from "@/api/ragChatApi";
import type { ChatMessage, RagQueryDataResponse } from "@/types/ragChat";

type Props = {
  apiBase: string;
  message: ChatMessage;
  onClose: () => void;
  onUpdateMessage: (message: ChatMessage) => void;
};

type DetailTab = {
  id: string;
  label: string;
  value: unknown;
  unloadedFallback?: unknown;
};

type ObjectRecord = Record<string, unknown>;

function readRetrievalSection(retrievalData: RagQueryDataResponse | undefined, key: string): unknown {
  return retrievalData?.data?.[key] ?? [];
}

function serializeDetail(value: unknown): string {
  return JSON.stringify(value, null, 2);
}

function isObjectRecord(value: unknown): value is ObjectRecord {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function formatCellValue(value: unknown): string {
  if (value === null || value === undefined) {
    return "";
  }

  if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") {
    return String(value);
  }

  return JSON.stringify(value);
}

function collectTableColumns(rows: ObjectRecord[]): string[] {
  const columns: string[] = [];

  for (const row of rows) {
    for (const key of Object.keys(row)) {
      if (!columns.includes(key)) {
        columns.push(key);
      }
    }
  }

  return columns;
}

function renderDetailValue(tab: DetailTab | undefined, value: unknown) {
  if (!tab) {
    return null;
  }

  if (tab.id === "raw") {
    return <pre className="lrn-code-surface">{serializeDetail(value)}</pre>;
  }

  if (Array.isArray(value)) {
    if (value.length === 0) {
      return (
        <>
          <p className="rag-chat__muted">[]</p>
          <pre className="lrn-code-surface">{serializeDetail(value)}</pre>
        </>
      );
    }

    const objectRows = value.filter(isObjectRecord);
    if (objectRows.length === value.length) {
      const columns = collectTableColumns(objectRows);

      return (
        <>
          <div className="rag-chat__table-wrap">
            <table className="rag-chat__detail-table">
              <thead>
                <tr>
                  {columns.map((column) => (
                    <th key={column}>{column}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {objectRows.map((row, index) => (
                  <tr key={`${tab.id}-${index}`}>
                    {columns.map((column) => (
                      <td key={column}>{formatCellValue(row[column])}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <pre className="lrn-code-surface">{serializeDetail(value)}</pre>
        </>
      );
    }
  }

  if (isObjectRecord(value)) {
    return (
      <>
        <div className="rag-chat__table-wrap">
          <table className="rag-chat__detail-table rag-chat__detail-table--kv">
            <tbody>
              {Object.entries(value).map(([key, entryValue]) => (
                <tr key={key}>
                  <th>{key}</th>
                  <td>{formatCellValue(entryValue)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <pre className="lrn-code-surface">{serializeDetail(value)}</pre>
      </>
    );
  }

  return <pre className="lrn-code-surface">{serializeDetail(value)}</pre>;
}

export function QueryDetailsDialog({ apiBase, message, onClose, onUpdateMessage }: Props) {
  const abortRef = useRef<AbortController | null>(null);
  const isMountedRef = useRef(true);
  const autoLoadStartedRef = useRef(false);
  const [activeTabId, setActiveTabId] = useState("entities");

  useEffect(() => {
    return () => {
      isMountedRef.current = false;
      abortRef.current?.abort();
      abortRef.current = null;
    };
  }, []);

  const loadRetrievalData = useCallback(async () => {
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
  }, [apiBase, message, onUpdateMessage]);

  useEffect(() => {
    if (autoLoadStartedRef.current) {
      return;
    }

    autoLoadStartedRef.current = true;
    void loadRetrievalData();
  }, [loadRetrievalData]);

  const tabs = useMemo<DetailTab[]>(
    () => [
      {
        id: "entities",
        label: "Entities",
        value: readRetrievalSection(message.retrievalData, "entities")
      },
      {
        id: "relationships",
        label: "Relationships",
        value: readRetrievalSection(message.retrievalData, "relationships")
      },
      {
        id: "chunks",
        label: "Chunks",
        value: readRetrievalSection(message.retrievalData, "chunks")
      },
      {
        id: "references",
        label: "References",
        value: readRetrievalSection(message.retrievalData, "references"),
        unloadedFallback: message.metadata?.references ?? []
      },
      {
        id: "metadata",
        label: "Metadata",
        value: message.retrievalData?.metadata ?? message.metadata ?? null
      },
      {
        id: "diagnostics",
        label: "Diagnostics",
        value: message.metadata?.diagnostics ?? {}
      },
      {
        id: "request",
        label: "Request",
        value: message.request ?? null
      },
      {
        id: "raw",
        label: "Raw JSON",
        value: message.retrievalData ?? { status: message.isLoadingRetrievalData ? "loading" : "not_loaded" }
      }
    ],
    [message]
  );

  const activeTab = tabs.find((tab) => tab.id === activeTabId) ?? tabs[0];
  const activeValue =
    message.retrievalData || activeTab?.unloadedFallback === undefined ? activeTab?.value : activeTab.unloadedFallback;

  return createPortal(
    <div className="rag-chat__dialog-backdrop" role="dialog" aria-modal="true" aria-labelledby="rag-chat-query-details-title">
      <div className="rag-chat__dialog lrn-dialog">
        <div className="lrn-panel__head">
          <div>
            <h2 id="rag-chat-query-details-title">Query details</h2>
            <p>{message.request?.query ?? "Current assistant response diagnostics"}</p>
          </div>
          <button className="lrn-icon-button" type="button" aria-label="Close query details" onClick={onClose}>
            x
          </button>
        </div>

        <div className="rag-chat__dialog-toolbar">
          <button
            className="lrn-button lrn-button--accent"
            type="button"
            disabled={!message.request || Boolean(message.retrievalData) || message.isLoadingRetrievalData}
            onClick={() => void loadRetrievalData()}
          >
            {message.isLoadingRetrievalData ? "Loading retrieval data" : message.retrievalData ? "Retrieval data loaded" : "Load retrieval data"}
          </button>
          {message.retrievalData ? <span className="lrn-chip">{message.retrievalData.status}</span> : null}
          {message.retrievalDataErrorMessage ? <span className="rag-chat__error">{message.retrievalDataErrorMessage}</span> : null}
        </div>

        <div className="rag-chat__detail-tabs" role="tablist" aria-label="Query details sections">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              className={tab.id === activeTab?.id ? "rag-chat__detail-tab rag-chat__detail-tab--active" : "rag-chat__detail-tab"}
              type="button"
              role="tab"
              aria-selected={tab.id === activeTab?.id}
              onClick={() => setActiveTabId(tab.id)}
            >
              {tab.label}
            </button>
          ))}
        </div>

        <section className="rag-chat__dialog-body" role="tabpanel" aria-label={activeTab?.label}>
          {!message.retrievalData && message.isLoadingRetrievalData ? (
            <p className="rag-chat__muted">Loading full retrieval details...</p>
          ) : null}
          {!message.retrievalData && !message.isLoadingRetrievalData && activeTab?.id !== "request" && activeTab?.id !== "metadata" && activeTab?.id !== "diagnostics" ? (
            <p className="rag-chat__muted">Full retrieval data has not been loaded yet.</p>
          ) : null}
          {renderDetailValue(activeTab, activeValue)}
        </section>
      </div>
    </div>,
    document.body
  );
}
