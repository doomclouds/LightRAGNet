import { useCallback, useEffect, useRef, useState } from "react";
import { Clipboard, RefreshCw } from "lucide-react";

import { clearCachePlan, getCacheManagementOverview } from "../api/cacheManagementApi";
import type { CacheClearPlanDto, CacheOverviewResponse } from "../types/cacheManagement";
import { CacheClearPlan } from "./CacheClearPlan";
import { CacheEfficiencyTrend } from "./CacheEfficiencyTrend";
import { CacheEntryDrilldown } from "./CacheEntryDrilldown";
import { CacheFamilyTable } from "./CacheFamilyTable";
import { CacheInsights } from "./CacheInsights";
import { CacheMeasurementContract } from "./CacheMeasurementContract";
import { CacheSummaryCards } from "./CacheSummaryCards";
import { formatDateTime, formatHitRate, formatLatencySaved } from "./formatters";

export { formatHitRate, formatLatencySaved, getRiskTone, getValueTone } from "./formatters";

type Props = {
  apiBase: string;
};

type ViewProps = {
  apiBase: string;
  workspace: string;
  window: string;
  overview: CacheOverviewResponse | null;
  isLoading: boolean;
  errorMessage: string | null;
  actionMessage: string | null;
  pendingPlanId: string | null;
  confirmingPlanId: string | null;
  onWorkspaceChange: (workspace: string) => void;
  onWindowChange: (window: string) => void;
  onRefresh: () => void;
  onCopyJson: () => void;
  onBeginClear: (plan: CacheClearPlanDto) => void;
  onCancelClear: () => void;
  onConfirmClear: (plan: CacheClearPlanDto) => void;
};

const windowOptions = [
  { value: "24h", label: "24h" },
  { value: "7d", label: "7d" }
];

export function CacheManagementWorkbench({ apiBase }: Props) {
  const [workspace, setWorkspace] = useState("_");
  const [window, setWindow] = useState("24h");
  const [overview, setOverview] = useState<CacheOverviewResponse | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [pendingPlanId, setPendingPlanId] = useState<string | null>(null);
  const [confirmingPlanId, setConfirmingPlanId] = useState<string | null>(null);
  const requestVersion = useRef(0);

  const loadOverview = useCallback(async () => {
    const version = requestVersion.current + 1;
    requestVersion.current = version;
    setIsLoading(true);
    setErrorMessage(null);

    try {
      const response = await getCacheManagementOverview(apiBase, workspace.trim() || "_", window);

      if (requestVersion.current === version) {
        setOverview(response);
      }
    } catch (error) {
      if (requestVersion.current === version) {
        setErrorMessage(error instanceof Error ? error.message : "Failed to load cache overview.");
      }
    } finally {
      if (requestVersion.current === version) {
        setIsLoading(false);
      }
    }
  }, [apiBase, window, workspace]);

  useEffect(() => {
    void loadOverview();
  }, [loadOverview]);

  const copyJson = useCallback(() => {
    if (!overview) {
      return;
    }

    const payload = JSON.stringify(overview, null, 2);
    void navigator.clipboard
      ?.writeText(payload)
      .then(() => setActionMessage("Overview JSON copied."))
      .catch(() => setActionMessage("Copy failed."));
  }, [overview]);

  const executeClear = useCallback(
    async (plan: CacheClearPlanDto, confirm: boolean) => {
      setPendingPlanId(plan.id);
      setErrorMessage(null);
      setActionMessage(null);

      try {
        const response = await clearCachePlan(apiBase, workspace.trim() || "_", plan.id, confirm);
        setActionMessage(response.message || `Deleted ${response.deletedEntries} entries.`);
        setConfirmingPlanId(null);
        await loadOverview();
      } catch (error) {
        setErrorMessage(error instanceof Error ? error.message : "Cache clear failed.");
      } finally {
        setPendingPlanId(null);
      }
    },
    [apiBase, loadOverview, workspace]
  );

  const beginClear = useCallback(
    (plan: CacheClearPlanDto) => {
      if (plan.requiresConfirmation) {
        setConfirmingPlanId(plan.id);
        return;
      }

      void executeClear(plan, false);
    },
    [executeClear]
  );

  return (
    <CacheManagementWorkbenchView
      apiBase={apiBase}
      workspace={workspace}
      window={window}
      overview={overview}
      isLoading={isLoading}
      errorMessage={errorMessage}
      actionMessage={actionMessage}
      pendingPlanId={pendingPlanId}
      confirmingPlanId={confirmingPlanId}
      onWorkspaceChange={setWorkspace}
      onWindowChange={setWindow}
      onRefresh={() => void loadOverview()}
      onCopyJson={copyJson}
      onBeginClear={beginClear}
      onCancelClear={() => setConfirmingPlanId(null)}
      onConfirmClear={(plan) => void executeClear(plan, true)}
    />
  );
}

export function CacheManagementWorkbenchView({
  apiBase,
  workspace,
  window,
  overview,
  isLoading,
  errorMessage,
  actionMessage,
  pendingPlanId,
  confirmingPlanId,
  onWorkspaceChange,
  onWindowChange,
  onRefresh,
  onCopyJson,
  onBeginClear,
  onCancelClear,
  onConfirmClear
}: ViewProps) {
  const isEmpty =
    overview !== null &&
    !overview.summary.measured &&
    overview.families.length === 0 &&
    overview.trend.length === 0 &&
    overview.clearPlan.length === 0 &&
    overview.entrySamples.length === 0;

  return (
    <main className="cache-workbench" data-api-base={apiBase}>
      <section className="cache-workbench__inner">
        <header className="cache-page-head">
          <div>
            <h1>Cache Management</h1>
            <div className="cache-page-meta">
              <span>Workspace {overview?.workspace ?? workspace}</span>
              <span>{overview ? formatDateTime(overview.generatedAt) : "Loading"}</span>
            </div>
          </div>

          <div className="cache-toolbar" aria-label="Cache controls">
            <label className="cache-field">
              <span>Workspace</span>
              <input
                value={workspace}
                onChange={(event) => onWorkspaceChange(event.target.value)}
                placeholder="_"
                aria-label="Workspace"
              />
            </label>

            <div className="cache-segmented" aria-label="Time window">
              {windowOptions.map((option) => (
                <button
                  className={option.value === window ? "is-active" : ""}
                  key={option.value}
                  type="button"
                  onClick={() => onWindowChange(option.value)}
                >
                  {option.label}
                </button>
              ))}
            </div>

            <button className="cache-button cache-button--accent" type="button" onClick={onRefresh} disabled={isLoading}>
              <RefreshCw aria-hidden="true" size={16} />
              {isLoading ? "Loading" : "Refresh"}
            </button>
            <button className="cache-button" type="button" onClick={onCopyJson} disabled={!overview}>
              <Clipboard aria-hidden="true" size={16} />
              Copy JSON
            </button>
          </div>
        </header>

        {errorMessage ? <div className="cache-banner cache-banner--error">{errorMessage}</div> : null}
        {actionMessage ? <div className="cache-banner cache-banner--success">{actionMessage}</div> : null}

        {isLoading && !overview ? (
          <div className="cache-panel cache-loading-state">Loading cache overview</div>
        ) : null}

        {isEmpty ? <div className="cache-panel cache-empty-state">No cache activity in this window</div> : null}

        {overview ? (
          <>
            <CacheSummaryCards summary={overview.summary} />

            <div className="cache-content-grid">
              <CacheFamilyTable families={overview.families} />
              <CacheInsights insights={overview.insights} />
            </div>

            <div className="cache-detail-grid">
              <CacheEfficiencyTrend trend={overview.trend} />
              <CacheClearPlan
                plans={overview.clearPlan}
                pendingPlanId={pendingPlanId}
                confirmingPlanId={confirmingPlanId}
                onBeginClear={onBeginClear}
                onCancelClear={onCancelClear}
                onConfirmClear={onConfirmClear}
              />
            </div>

            <div className="cache-detail-grid">
              <CacheEntryDrilldown entries={overview.entrySamples} />
              <CacheMeasurementContract />
            </div>
          </>
        ) : null}
      </section>
    </main>
  );
}
