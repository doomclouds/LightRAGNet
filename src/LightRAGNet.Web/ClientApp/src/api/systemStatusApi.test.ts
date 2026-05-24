import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

import { getSystemHealth } from "./systemStatusApi";

const healthResponse = {
  status: "Degraded",
  generatedAt: "2026-05-24T12:00:00+08:00",
  durationMs: 42,
  summary: {
    healthy: 1,
    degraded: 1,
    unhealthy: 0,
    notMeasured: 0
  },
  checks: [
    {
      id: "rerank-config",
      name: "Rerank configuration",
      category: "Configuration",
      status: "Degraded",
      message: "Rerank configuration is missing.",
      evidence: { configured: false },
      remediation: "Configure rerank options.",
      affects: ["Hybrid retrieval"],
      durationMs: 3
    }
  ],
  fixFirst: [
    {
      checkId: "rerank-config",
      title: "Rerank configuration",
      status: "Degraded",
      remediation: "Configure rerank options.",
      affects: ["Hybrid retrieval"]
    }
  ],
  featureImpacts: [
    {
      feature: "Hybrid retrieval",
      status: "Degraded",
      reason: "Rerank configuration is missing.",
      affectedBy: ["rerank-config"],
      links: [{ label: "Open settings", href: "/settings" }]
    }
  ]
};

function jsonResponse(body: unknown, init?: ResponseInit): Response {
  return new Response(JSON.stringify(body), {
    headers: { "content-type": "application/json" },
    status: init?.status ?? 200,
    statusText: init?.statusText,
    ...init
  });
}

describe("systemStatusApi", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  test("getSystemHealth calls system health with GET and resolves payload", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse(healthResponse));

    const result = await getSystemHealth("/api-root/");

    expect(result).toEqual(healthResponse);
    expect(fetch).toHaveBeenCalledWith(
      "/api-root/api/system/health",
      expect.objectContaining({ method: "GET" })
    );
  });

  test("getSystemHealth throws server message on non-ok responses", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(
      jsonResponse({ message: "Server unavailable" }, { status: 503, statusText: "Service Unavailable" })
    );

    await expect(getSystemHealth("/api-root/")).rejects.toThrow("Server unavailable");
  });
});
