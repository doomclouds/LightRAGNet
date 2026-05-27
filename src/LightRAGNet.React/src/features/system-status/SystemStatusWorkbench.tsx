import { useCallback, useEffect, useRef, useState, type RefObject } from "react";
import { Copy, LoaderCircle, RefreshCw } from "lucide-react";

import { getSystemHealth } from "@/api/systemStatusApi";
import type { SystemHealthResponse } from "@/api/systemStatusApi";
import { Button } from "@/shared/components/Button";
import { PageHeader } from "@/shared/components/PageHeader";
import { StatusPill } from "@/shared/components/StatusPill";
import "@/features/system-status/system-status.css";
import { SystemStatusEvidenceTable } from "./SystemStatusEvidenceTable";
import { SystemStatusFeatureImpactPanel } from "./SystemStatusFeatureImpactPanel";
import { SystemStatusRawJsonPanel } from "./SystemStatusRawJsonPanel";
import { SystemStatusRemediationPanel } from "./SystemStatusRemediationPanel";
import { SystemStatusSummaryTiles } from "./SystemStatusSummaryTiles";
import { formatHealthJson, getStatusTone } from "./systemStatusPresentation";

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

  return (
    <section className="system-status" data-api-base={apiBase}>
      <PageHeader
        title="System Status"
        description="Diagnostics workbench"
        meta={
          health ? (
            <>
              <StatusPill tone={getStatusTone(health.status)}>{health.status}</StatusPill>
              <span>
                Checks: {health.summary.healthy} healthy, {health.summary.degraded} degraded, {health.summary.unhealthy} unhealthy,{" "}
                {health.summary.notMeasured} not measured
              </span>
            </>
          ) : null
        }
        actions={
          <>
            <span aria-live="polite" className="system-status__copy-message" role="status">
              {copyMessage}
            </span>
            <Button disabled={!health} onClick={copyJson}>
              <Copy aria-hidden="true" size={16} />
              Copy JSON
            </Button>
            <Button disabled={isLoading} onClick={loadHealth} tone="primary">
              {isLoading ? <LoaderCircle aria-hidden="true" className="system-status__spin" size={16} /> : <RefreshCw aria-hidden="true" size={16} />}
              Refresh
            </Button>
          </>
        }
      />

      {errorMessage ? <p className="system-status__error">{errorMessage}</p> : null}
      {isLoading && !health ? <p className="system-status__loading">Loading system status...</p> : null}

      {health ? (
        <div className="system-status__compact-workbench" data-status={health.status}>
          <div className="system-status__diagnostic-main">
            <SystemStatusSummaryTiles health={health} />
            <SystemStatusEvidenceTable checks={health.checks} />
            <SystemStatusRawJsonPanel health={health} />
          </div>
          <div className="system-status__diagnostic-side">
            <SystemStatusRemediationPanel items={health.fixFirst} />
            <SystemStatusFeatureImpactPanel items={health.featureImpacts} />
          </div>
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
