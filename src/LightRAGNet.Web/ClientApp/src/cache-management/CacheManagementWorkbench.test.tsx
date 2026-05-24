import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, test } from "vitest";

import {
  CacheManagementWorkbenchView,
  formatHitRate,
  formatLatencySaved,
  getRiskTone,
  getValueTone
} from "./CacheManagementWorkbench";
import type { CacheOverviewResponse } from "../types/cacheManagement";

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

  test("renders cache evidence, clear plan, and safe entry samples without sensitive fields", () => {
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
    expect(html).toContain("Mix:query:af31...");
    expect(html).not.toMatch(/prompt|return_value|provider response|authorization|api key/i);
  });
});
