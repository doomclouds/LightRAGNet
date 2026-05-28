import { useCallback, useEffect, useRef, useState } from "react";
import { Clipboard, LoaderCircle, RefreshCw } from "lucide-react";

import { clearCachePlan, getCacheManagementOverview } from "@/api/cacheManagementApi";
import type { CacheClearPlanDto, CacheOverviewResponse } from "@/types/cacheManagement";
import "@/features/cache-management/cache-management.css";
import { CacheClearPlan } from "./CacheClearPlan";
import { CacheClearPolicy } from "./CacheClearPolicy";
import { CacheEfficiencyTrend } from "./CacheEfficiencyTrend";
import { CacheFamilyTable } from "./CacheFamilyTable";
import { CacheInsights } from "./CacheInsights";
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

type OverviewRequestParams = {
  workspace: string;
  window: string;
};

type OverviewRequestSnapshot = OverviewRequestParams & {
  version: number;
};

const windowOptions = [
  { value: "1h", label: "1H" },
  { value: "6h", label: "6H" },
  { value: "24h", label: "24H" },
  { value: "7d", label: "7D" },
  { value: "30d", label: "30D" }
];

function normalizeWorkspace(workspace: string): string {
  return workspace.trim() || "_";
}

function sameOverviewParams(left: OverviewRequestParams, right: OverviewRequestParams): boolean {
  return left.workspace === right.workspace && left.window === right.window;
}

export function isCurrentOverviewRequest(
  request: OverviewRequestSnapshot,
  latestParams: OverviewRequestParams,
  currentVersion: number
): boolean {
  return request.version === currentVersion && sameOverviewParams(request, latestParams);
}

export function createSafeOverviewExport(overview: CacheOverviewResponse): CacheOverviewResponse {
  return {
    workspace: overview.workspace,
    window: overview.window,
    generatedAt: overview.generatedAt,
    summary: {
      overallHitRate: overview.summary.overallHitRate,
      providerCallsAvoided: overview.summary.providerCallsAvoided,
      estimatedLatencySavedMs: overview.summary.estimatedLatencySavedMs,
      staleOrRiskyEntries: overview.summary.staleOrRiskyEntries,
      measured: overview.summary.measured
    },
    families: overview.families.map((family) => ({
      cacheType: family.cacheType,
      displayName: family.displayName,
      hitRate: family.hitRate,
      hits: family.hits,
      misses: family.misses,
      attempts: family.attempts,
      entryCount: family.entryCount,
      valueLevel: family.valueLevel,
      riskLevel: family.riskLevel,
      providerCallsAvoided: family.providerCallsAvoided,
      estimatedLatencySavedMs: family.estimatedLatencySavedMs,
      message: family.message
    })),
    trend: overview.trend.map((point) => ({
      timestamp: point.timestamp,
      hitRate: point.hitRate,
      savedCalls: point.savedCalls
    })),
    insights: overview.insights.map((insight) => ({
      title: insight.title,
      message: insight.message,
      level: insight.level
    })),
    clearPlan: overview.clearPlan.map((plan) => ({
      id: plan.id,
      title: plan.title,
      cacheTypes: [...plan.cacheTypes],
      entryCount: plan.entryCount,
      risk: plan.risk,
      impact: plan.impact,
      requiresConfirmation: plan.requiresConfirmation
    })),
    entrySamples: overview.entrySamples.map((entry) => ({
      cacheKeyPrefix: entry.cacheKeyPrefix,
      cacheType: entry.cacheType,
      lastHit: entry.lastHit,
      state: entry.state
    }))
  };
}

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
  const latestParams = useRef<OverviewRequestParams>({ workspace: "_", window: "24h" });

  const loadOverview = useCallback(async (requestedParams?: OverviewRequestParams) => {
    const params = requestedParams ?? { workspace: normalizeWorkspace(workspace), window };

    if (!sameOverviewParams(params, latestParams.current)) {
      return;
    }

    const version = requestVersion.current + 1;
    requestVersion.current = version;
    const request = { ...params, version };
    setIsLoading(true);
    setErrorMessage(null);

    try {
      const response = await getCacheManagementOverview(apiBase, params.workspace, params.window);

      if (isCurrentOverviewRequest(request, latestParams.current, requestVersion.current)) {
        setOverview(response);
      }
    } catch (error) {
      if (isCurrentOverviewRequest(request, latestParams.current, requestVersion.current)) {
        setErrorMessage(error instanceof Error ? error.message : "Failed to load cache overview.");
      }
    } finally {
      if (isCurrentOverviewRequest(request, latestParams.current, requestVersion.current)) {
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

    if (!navigator.clipboard?.writeText) {
      setActionMessage("Copy failed.");
      return;
    }

    const payload = JSON.stringify(createSafeOverviewExport(overview), null, 2);
    void navigator.clipboard
      .writeText(payload)
      .then(() => setActionMessage("Overview JSON copied."))
      .catch(() => setActionMessage("Copy failed."));
  }, [overview]);

  const executeClear = useCallback(
    async (plan: CacheClearPlanDto, confirm: boolean) => {
      if (!overview) {
        return;
      }

      const clearParams = { workspace: overview.workspace, window: overview.window };

      if (!sameOverviewParams(clearParams, latestParams.current)) {
        return;
      }

      setPendingPlanId(plan.id);
      setErrorMessage(null);
      setActionMessage(null);

      try {
        const response = await clearCachePlan(apiBase, clearParams.workspace, plan.id, confirm);

        if (sameOverviewParams(clearParams, latestParams.current)) {
          setActionMessage(response.message || `Deleted ${response.deletedEntries} entries.`);
          setConfirmingPlanId(null);
          await loadOverview(clearParams);
        }
      } catch (error) {
        if (sameOverviewParams(clearParams, latestParams.current)) {
          setErrorMessage(error instanceof Error ? error.message : "Cache clear failed.");
        }
      } finally {
        setPendingPlanId(null);
      }
    },
    [apiBase, loadOverview, overview]
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
      onWorkspaceChange={(nextWorkspace) => {
        latestParams.current = { ...latestParams.current, workspace: normalizeWorkspace(nextWorkspace) };
        setOverview(null);
        setConfirmingPlanId(null);
        setPendingPlanId(null);
        setActionMessage(null);
        setWorkspace(nextWorkspace);
      }}
      onWindowChange={(nextWindow) => {
        latestParams.current = { ...latestParams.current, window: nextWindow };
        setOverview(null);
        setConfirmingPlanId(null);
        setPendingPlanId(null);
        setActionMessage(null);
        setWindow(nextWindow);
      }}
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
    <section className="cache-workbench" data-api-base={apiBase}>
      <section className="cache-workbench__inner">
        <header className="cache-page-head">
          <div>
            <h1>Cache Management</h1>
            <p className="cache-page-subtle">Monitor cache performance and manage clear policies</p>
          </div>

          <div className="cache-toolbar" aria-label="Cache controls">
            <label className="cache-field cache-field--workspace">
              <span>Workspace</span>
              <input value={workspace} onChange={(event) => onWorkspaceChange(event.target.value)} placeholder="_" aria-label="Workspace" />
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

            <button className="cache-icon-button" type="button" onClick={onRefresh} disabled={isLoading} aria-label="Refresh cache metrics">
              {isLoading ? <LoaderCircle aria-hidden="true" className="cache-spin" size={16} /> : <RefreshCw aria-hidden="true" size={16} />}
            </button>
            <button className="cache-button" type="button" onClick={onCopyJson} disabled={!overview}>
              <Clipboard aria-hidden="true" size={16} />
              Copy JSON
            </button>
          </div>
        </header>

        <div className="cache-page-meta">
          <span>Workspace {overview?.workspace ?? workspace}</span>
          <span>{overview ? formatDateTime(overview.generatedAt) : "Loading"}</span>
        </div>

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
              <div className="cache-side-stack">
                <CacheInsights insights={overview.insights} />
                <CacheEfficiencyTrend trend={overview.trend} window={window} />
              </div>
            </div>

            <div className="cache-bottom-grid">
              <CacheClearPlan
                plans={overview.clearPlan}
                pendingPlanId={pendingPlanId}
                confirmingPlanId={confirmingPlanId}
                onBeginClear={onBeginClear}
                onCancelClear={onCancelClear}
                onConfirmClear={onConfirmClear}
              />
              <CacheClearPolicy planCount={overview.clearPlan.length} />
            </div>
          </>
        ) : null}
      </section>
    </section>
  );
}
