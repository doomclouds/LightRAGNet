import { resolve } from "node:path";
import { readFileSync } from "node:fs";
import { afterEach, describe, expect, test, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const { getSystemHealth } = vi.hoisted(() => ({
  getSystemHealth: vi.fn()
}));

vi.mock("@/api/systemStatusApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/api/systemStatusApi")>()),
  getSystemHealth
}));

import { SystemStatusWorkbench } from "@/features/system-status/SystemStatusWorkbench";
import type { SystemHealthResponse } from "@/api/systemStatusApi";

const systemStatusWorkbenchPath = resolve(process.cwd(), "src/features/system-status/SystemStatusWorkbench.tsx");

afterEach(() => {
  vi.restoreAllMocks();
  document.body.innerHTML = "";
});

describe("SystemStatusWorkbench source guard", () => {
  test("uses server-provided health aggregation fields without local aggregation", () => {
    const source = readFileSync(systemStatusWorkbenchPath, "utf8");

    expect(source).toContain("health.status");
    expect(source).toContain("health.fixFirst");
    expect(source).toContain("health.featureImpacts");
    expect(source).not.toMatch(/\b(?:const|let|var)\s+fixFirst\s*=/);
    expect(source).not.toMatch(/\b(?:const|let|var)\s+overallStatus\s*=/);
  });

  test("renders server-provided status, fix-first priorities, and feature impacts", async () => {
    const health: SystemHealthResponse = {
      status: "Unhealthy",
      generatedAt: "2026-05-24T10:00:00Z",
      durationMs: 42,
      summary: {
        healthy: 1,
        degraded: 0,
        unhealthy: 1,
        notMeasured: 0
      },
      checks: [],
      fixFirst: [
        {
          checkId: "neo4j-connectivity",
          title: "Restore Neo4j",
          status: "Unhealthy",
          remediation: "Restart graph storage.",
          affects: ["Knowledge Graph"]
        }
      ],
      featureImpacts: [
        {
          feature: "Graph retrieval",
          status: "Unhealthy",
          reason: "Graph storage is offline.",
          affectedBy: ["neo4j-connectivity"],
          links: [{ label: "Graph", href: "/graph-view" }]
        }
      ]
    };
    getSystemHealth.mockResolvedValue(health);

    render(<SystemStatusWorkbench apiBase="/api-root" />);

    expect(await screen.findAllByText("Unhealthy")).not.toHaveLength(0);
    expect(screen.getByText("Restore Neo4j")).toBeInTheDocument();
    expect(screen.getByText("Restart graph storage.")).toBeInTheDocument();
    expect(screen.getByText("Graph retrieval")).toBeInTheDocument();
    expect(screen.getByText("Graph storage is offline.")).toBeInTheDocument();
  });
});
