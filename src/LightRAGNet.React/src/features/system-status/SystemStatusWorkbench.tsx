import { useCallback, useEffect, useRef, useState, type RefObject } from "react";
import { Download, LoaderCircle, RefreshCw } from "lucide-react";

import { getSystemHealth } from "@/api/systemStatusApi";
import type { SystemHealthResponse } from "@/api/systemStatusApi";
import { Button } from "@/shared/components/Button";
import "@/features/system-status/system-status.css";
import { SystemStatusEvidenceTable } from "./SystemStatusEvidenceTable";
import { SystemStatusFeatureImpactPanel } from "./SystemStatusFeatureImpactPanel";
import { SystemStatusRawJsonPanel } from "./SystemStatusRawJsonPanel";
import { SystemStatusRemediationPanel } from "./SystemStatusRemediationPanel";
import { SystemStatusSummaryTiles } from "./SystemStatusSummaryTiles";
import { formatHealthJson } from "./systemStatusPresentation";

type SystemStatusWorkbenchProps = {
  apiBase: string;
};

export function SystemStatusWorkbench({ apiBase }: SystemStatusWorkbenchProps) {
  const [health, setHealth] = useState<SystemHealthResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [copyMessage, setCopyMessage] = useState<string | null>(null);
  const latestRequestId = useRef(0);
  const currentApiBaseRef = useRef(apiBase);
  currentApiBaseRef.current = apiBase;

  const loadHealth = useCallback(async () => {
    const requestId = latestRequestId.current + 1;
    const requestApiBase = apiBase;
    latestRequestId.current = requestId;
    setIsLoading(true);
    setErrorMessage(null);

    try {
      const response = await getSystemHealth(requestApiBase);
      if (!isCurrentRequest(latestRequestId, currentApiBaseRef, requestId, requestApiBase)) {
        return;
      }

      setHealth(response);
    } catch (error) {
      if (!isCurrentRequest(latestRequestId, currentApiBaseRef, requestId, requestApiBase)) {
        return;
      }

      setErrorMessage(error instanceof Error ? error.message : "Unable to load system status.");
    } finally {
      if (isCurrentRequest(latestRequestId, currentApiBaseRef, requestId, requestApiBase)) {
        setIsLoading(false);
      }
    }
  }, [apiBase]);

  useEffect(() => {
    void loadHealth();
  }, [loadHealth]);

  async function copyJson() {
    if (!health) {
      return;
    }

    try {
      await navigator.clipboard.writeText(formatHealthJson(health));
      setCopyMessage("Copied.");
    } catch {
      setCopyMessage("Copy unavailable.");
    }
  }

  function downloadJson() {
    if (!health) {
      return;
    }

    const blob = new Blob([formatHealthJson(health)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = "system-status.json";
    anchor.click();
    URL.revokeObjectURL(url);
  }

  return (
    <section className="system-status" data-api-base={apiBase}>
      <header className="system-status__page-head">
        <div>
          <h1>System Status</h1>
          <p className="system-status__subtle">Real-time diagnostics and system operation overview</p>
        </div>
        <div className="system-status__actions">
          <span aria-live="polite" className="system-status__copy-message" role="status">
            {copyMessage}
          </span>
          <Button className="system-status__button" disabled={!health} onClick={copyJson}>
            <Download aria-hidden="true" size={16} />
            Export Report
          </Button>
          <Button className="system-status__button system-status__button--primary" disabled={isLoading} onClick={loadHealth} tone="primary">
            {isLoading ? <LoaderCircle aria-hidden="true" className="system-status__spin" size={16} /> : <RefreshCw aria-hidden="true" size={16} />}
            Refresh Now
          </Button>
        </div>
      </header>

      {errorMessage ? <p className="system-status__error">{errorMessage}</p> : null}
      {isLoading && !health ? <p className="system-status__loading">Loading system status...</p> : null}

      {health ? (
        <div className="system-status__workbench" data-status={health.status}>
          <SystemStatusSummaryTiles health={health} />
          <section className="system-status__grid">
            <SystemStatusEvidenceTable checks={health.checks} generatedAt={health.generatedAt} />
            <div className="system-status__side-stack">
              <SystemStatusRemediationPanel items={health.fixFirst} />
              <SystemStatusFeatureImpactPanel items={health.featureImpacts} />
            </div>
            <SystemStatusRawJsonPanel health={health} onCopy={copyJson} onDownload={downloadJson} />
          </section>
        </div>
      ) : null}
    </section>
  );
}

function isCurrentRequest(
  latestRequestId: RefObject<number>,
  currentApiBaseRef: RefObject<string>,
  requestId: number,
  requestApiBase: string
) {
  return latestRequestId.current === requestId && currentApiBaseRef.current === requestApiBase;
}
