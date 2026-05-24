import { useCallback, useEffect, useState } from "react";

import { getSystemHealth } from "../api/systemStatusApi";
import type { SystemHealthResponse } from "../api/systemStatusApi";
import { SystemStatusChecks } from "./SystemStatusChecks";
import { SystemStatusFeatureImpact } from "./SystemStatusFeatureImpact";
import { SystemStatusFixFirst } from "./SystemStatusFixFirst";
import { SystemStatusSummary } from "./SystemStatusSummary";

type SystemStatusWorkbenchProps = {
  apiBase: string;
};

export function SystemStatusWorkbench({ apiBase }: SystemStatusWorkbenchProps) {
  const [health, setHealth] = useState<SystemHealthResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [copyMessage, setCopyMessage] = useState<string | null>(null);

  const loadHealth = useCallback(async () => {
    setIsLoading(true);
    setErrorMessage(null);

    try {
      const response = await getSystemHealth(apiBase);
      setHealth(response);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Unable to load system status.");
    } finally {
      setIsLoading(false);
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
      await navigator.clipboard.writeText(JSON.stringify(health, null, 2));
      setCopyMessage("Copied.");
    } catch {
      setCopyMessage("Copy unavailable.");
    }
  }

  return (
    <main className="system-status" data-api-base={apiBase}>
      <header className="system-status__header">
        <div>
          <p className="system-status__eyebrow">Operations</p>
          <h1>System Status</h1>
        </div>
        <div className="system-status__actions">
          {copyMessage ? <span className="system-status__copy-message">{copyMessage}</span> : null}
          <button className="system-status__button" disabled={!health} onClick={copyJson} type="button">
            Copy JSON
          </button>
          <button className="system-status__button system-status__button--primary" disabled={isLoading} onClick={loadHealth} type="button">
            <span aria-hidden="true" className={isLoading ? "system-status__spinner-dot system-status__spin" : "system-status__spinner-dot"} />
            Refresh
          </button>
        </div>
      </header>

      {errorMessage ? <p className="system-status__error">{errorMessage}</p> : null}
      {isLoading && !health ? <p className="system-status__loading">Loading system status...</p> : null}

      {health ? (
        <div className="system-status__grid" data-status={health.status}>
          <SystemStatusSummary health={health} />
          <SystemStatusFixFirst items={health.fixFirst} />
          <SystemStatusChecks checks={health.checks} />
          <SystemStatusFeatureImpact items={health.featureImpacts} />
        </div>
      ) : null}
    </main>
  );
}
