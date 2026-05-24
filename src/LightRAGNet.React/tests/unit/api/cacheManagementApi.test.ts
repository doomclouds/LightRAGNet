import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

import { clearCachePlan, getCacheManagementOverview } from "@/api/cacheManagementApi";

function jsonResponse(body: unknown, init?: ResponseInit): Response {
  return new Response(JSON.stringify(body), {
    headers: { "content-type": "application/json" },
    status: init?.status ?? 200,
    statusText: init?.statusText,
    ...init
  });
}

function textResponse(body: string, init?: ResponseInit): Response {
  return new Response(body, {
    headers: { "content-type": "text/html" },
    status: init?.status ?? 200,
    statusText: init?.statusText,
    ...init
  });
}

describe("cacheManagementApi", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  test("getCacheManagementOverview calls overview endpoint with encoded workspace and window", async () => {
    const overview = {
      workspace: "team / a",
      window: "24h",
      generatedAt: "2026-05-24T10:00:00Z",
      summary: {
        overallHitRate: 0.75,
        providerCallsAvoided: 12,
        estimatedLatencySavedMs: 45000,
        staleOrRiskyEntries: 3,
        measured: true
      },
      families: [],
      trend: [],
      insights: [],
      clearPlan: [],
      entrySamples: []
    };
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse(overview));

    await expect(getCacheManagementOverview("/api-root/", "team / a", "24h")).resolves.toEqual(overview);

    expect(fetch).toHaveBeenCalledWith(
      "/api-root/api/cache-management/overview?workspace=team+%2F+a&window=24h",
      expect.objectContaining({ method: "GET" })
    );
  });

  test("clearCachePlan posts selected plan and confirmation flag", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(
      jsonResponse({
        succeeded: true,
        deletedEntries: 19,
        cacheTypes: ["query"],
        message: "Deleted 19 entries.",
        revisionAfter: 5
      })
    );

    const result = await clearCachePlan("/api-root", "workspace-a", "stale-query-cache", true);

    expect(result.deletedEntries).toBe(19);
    expect(fetch).toHaveBeenCalledWith(
      "/api-root/api/cache-management/clear",
      expect.objectContaining({
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          workspace: "workspace-a",
          planId: "stale-query-cache",
          confirm: true
        })
      })
    );
  });

  test("clearCachePlan throws server message for HTTP errors and failed response bodies", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse({ message: "Plan id is required." }, { status: 400, statusText: "Bad Request" }))
      .mockResolvedValueOnce(
        jsonResponse({
          succeeded: false,
          deletedEntries: 0,
          cacheTypes: ["summary"],
          message: "Confirmation is required.",
          revisionAfter: null
        })
      );

    await expect(clearCachePlan("", "_", "", false)).rejects.toThrow("Plan id is required.");
    await expect(clearCachePlan("", "_", "summary-cache-review", false)).rejects.toThrow("Confirmation is required.");
  });

  test("readJson throws readable status error for non-ok non-json responses", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(textResponse("<html>server error</html>", { status: 500 }));

    await expect(getCacheManagementOverview("", "_", "24h")).rejects.toThrow("Request failed with status 500");
  });

  test("readJson throws explicit invalid response error for ok malformed JSON", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(textResponse("{not-json", { status: 200 }));

    await expect(getCacheManagementOverview("", "_", "24h")).rejects.toThrow("Invalid cache management response");
  });

  test("readJson throws explicit empty response error for ok empty body", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(textResponse("", { status: 200 }));

    await expect(clearCachePlan("", "_", "stale-query-cache", false)).rejects.toThrow("Empty cache management response");
  });
});
