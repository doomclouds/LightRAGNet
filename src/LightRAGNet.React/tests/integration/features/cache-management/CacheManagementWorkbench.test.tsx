import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, describe, expect, test, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const { clearCachePlan, getCacheManagementOverview } = vi.hoisted(() => ({
  clearCachePlan: vi.fn(),
  getCacheManagementOverview: vi.fn()
}));

vi.mock("@/api/cacheManagementApi", () => ({
  clearCachePlan,
  getCacheManagementOverview
}));

import {
  CacheManagementWorkbench,
  CacheManagementWorkbenchView,
  createSafeOverviewExport,
  formatHitRate,
  formatLatencySaved,
  isCurrentOverviewRequest,
  getRiskTone,
  getValueTone
} from "@/features/cache-management/CacheManagementWorkbench";
import type { CacheOverviewResponse } from "@/types/cacheManagement";

afterEach(() => {
  vi.restoreAllMocks();
  document.body.innerHTML = "";
});

const overview: CacheOverviewResponse = {
  workspace: "_",
  window: "24h",
  generatedAt: "2026-05-24T10:00:00Z",
  summary: {
    overallHitRate: 0.784,
    providerCallsAvoided: 1248,
    estimatedLatencySavedMs: 2_520_000,
    staleOrRiskyEntries: 37,
    measured: true
  },
  families: [
    {
      cacheType: "query",
      displayName: "Query answer",
      hitRate: 0.64,
      hits: 192,
      misses: 108,
      attempts: 300,
      entryCount: 84,
      valueLevel: "High",
      riskLevel: "Revision sensitive",
      providerCallsAvoided: 192,
      estimatedLatencySavedMs: 840_000,
      message: "Current revision is healthy."
    }
  ],
  trend: [{ timestamp: "2026-05-24T09:00:00Z", hitRate: 0.75, savedCalls: 21 }],
  insights: [{ title: "Keep extract cache", message: "High hit rate.", level: "good" }],
  clearPlan: [
    {
      id: "all-llm-cache",
      title: "Clear all LLM cache",
      cacheTypes: ["query", "keywords", "extract", "summary"],
      entryCount: 245,
      risk: "High",
      impact: "Drops repeated query efficiency.",
      requiresConfirmation: true
    }
  ],
  entrySamples: [
    {
      cacheKeyPrefix: "Mix:query:af31...",
      cacheType: "query",
      lastHit: "2026-05-24T09:52:00Z",
      state: "current revision"
    }
  ]
};

describe("CacheManagementWorkbench", () => {
  test("format helpers render operational values", () => {
    expect(formatHitRate(0.784)).toBe("78.4%");
    expect(formatHitRate(null)).toBe("N/A");
    expect(formatLatencySaved(2_520_000)).toBe("42 min");
    expect(formatLatencySaved(null)).toBe("N/A");
    expect(getValueTone("High")).toBe("good");
    expect(getRiskTone("High")).toBe("bad");
  });

  test("request guard rejects stale workspace refresh even when it has the newest version", () => {
    const staleClearRefresh = { workspace: "workspace-a", window: "24h", version: 3 };
    const latestControls = { workspace: "workspace-b", window: "24h" };

    expect(isCurrentOverviewRequest(staleClearRefresh, latestControls, 3)).toBe(false);
    expect(isCurrentOverviewRequest({ workspace: "workspace-b", window: "24h", version: 4 }, latestControls, 4)).toBe(
      true
    );
  });

  test("createSafeOverviewExport removes undeclared sensitive fields", () => {
    const unsafeOverview = {
      ...overview,
      original_prompt: "hidden prompt",
      return_value: "hidden response",
      authorization: "Bearer token",
      api_key: "sk-hidden",
      summary: {
        ...overview.summary,
        return_value: "hidden response"
      },
      families: [
        {
          ...overview.families[0],
          original_prompt: "hidden prompt"
        }
      ],
      entrySamples: [
        {
          ...overview.entrySamples[0],
          authorization: "Bearer token"
        }
      ]
    };

    const exportedJson = JSON.stringify(createSafeOverviewExport(unsafeOverview as CacheOverviewResponse));

    expect(exportedJson).toContain("Query answer");
    expect(exportedJson).not.toMatch(/original_prompt|return_value|authorization|api_key|hidden prompt|Bearer token/i);
  });

  test("renders cache evidence and clear plan without sensitive payload fields", () => {
    const html = renderToStaticMarkup(
      <CacheManagementWorkbenchView
        apiBase="/api-root"
        workspace="_"
        window="24h"
        overview={overview}
        isLoading={false}
        errorMessage={null}
        actionMessage={null}
        pendingPlanId={null}
        confirmingPlanId={null}
        onWorkspaceChange={() => undefined}
        onWindowChange={() => undefined}
        onRefresh={() => undefined}
        onCopyJson={() => undefined}
        onBeginClear={() => undefined}
        onCancelClear={() => undefined}
        onConfirmClear={() => undefined}
      />
    );

    expect(html).toContain("78.4%");
    expect(html).toContain("1,248");
    expect(html).toContain("42 min");
    expect(html).toContain("Clear all LLM cache");
    expect(html).not.toMatch(/prompt|return_value|provider response|authorization|api key/i);
  });

  test("renders the approved table-pages workbench sections", () => {
    const html = renderToStaticMarkup(
      <CacheManagementWorkbenchView
        apiBase="/api-root"
        workspace="_"
        window="24h"
        overview={overview}
        isLoading={false}
        errorMessage={null}
        actionMessage={null}
        pendingPlanId={null}
        confirmingPlanId={null}
        onWorkspaceChange={() => undefined}
        onWindowChange={() => undefined}
        onRefresh={() => undefined}
        onCopyJson={() => undefined}
        onBeginClear={() => undefined}
        onCancelClear={() => undefined}
        onConfirmClear={() => undefined}
      />
    );

    expect(html).toContain("Monitor cache performance and manage clear policies");
    expect(html).toContain("Cache Families");
    expect(html).toContain("Cache Insights");
    expect(html).toContain("Hit Rate Trend (24h)");
    expect(html).toContain("Clear Plan");
    expect(html).toContain("Clear Policy");
    expect(html).toContain("Preview Plan");
    expect(html).toContain("1H");
    expect(html).toContain("6H");
    expect(html).toContain("30D");
    expect(html).not.toContain("Entry samples");
    expect(html).not.toContain("Measurement");
  });

  test("renders explicit destructive clear confirmation before enabling confirm", () => {
    const html = renderToStaticMarkup(
      <CacheManagementWorkbenchView
        apiBase="/api-root"
        workspace="_"
        window="24h"
        overview={overview}
        isLoading={false}
        errorMessage={null}
        actionMessage={null}
        pendingPlanId={null}
        confirmingPlanId="all-llm-cache"
        onWorkspaceChange={() => undefined}
        onWindowChange={() => undefined}
        onRefresh={() => undefined}
        onCopyJson={() => undefined}
        onBeginClear={() => undefined}
        onCancelClear={() => undefined}
        onConfirmClear={() => undefined}
      />
    );

    expect(html).toContain("Confirm destructive clear");
    expect(html).toMatch(/<button[^>]*disabled=""[^>]*>[\s\S]*Confirm/);
  });

  test("clears stale destructive confirmation when workspace changes before the next overview loads", async () => {
    const user = userEvent.setup();
    getCacheManagementOverview.mockImplementation(
      (_apiBase: string, workspace: string) =>
        workspace === "workspace-b" ? new Promise<CacheOverviewResponse>(() => undefined) : Promise.resolve(overview)
    );

    render(<CacheManagementWorkbench apiBase="/api-root" />);

    await screen.findByText("Clear all LLM cache");
    await user.click(screen.getByRole("button", { name: /^review$/i }));
    expect(screen.getByText("Confirm destructive clear")).toBeInTheDocument();

    const workspaceInput = screen.getByLabelText("Workspace");
    await user.clear(workspaceInput);
    await user.type(workspaceInput, "workspace-b");

    await waitFor(() => expect(screen.queryByText("Confirm destructive clear")).not.toBeInTheDocument());
    expect(clearCachePlan).not.toHaveBeenCalled();
  });

  test("reports copy failure when Clipboard API is unavailable", async () => {
    const user = userEvent.setup();
    vi.spyOn(navigator, "clipboard", "get").mockReturnValue(undefined as unknown as Clipboard);
    getCacheManagementOverview.mockResolvedValue(overview);

    render(<CacheManagementWorkbench apiBase="/api-root" />);

    await screen.findByText("Clear all LLM cache");
    await user.click(screen.getByRole("button", { name: /copy json/i }));

    expect(await screen.findByText("Copy failed.")).toBeInTheDocument();
  });
});
