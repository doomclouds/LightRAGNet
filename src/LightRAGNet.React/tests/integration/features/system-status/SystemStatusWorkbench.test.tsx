import { resolve } from "node:path";
import { readFileSync } from "node:fs";
import { afterEach, describe, expect, test, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

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

const degradedHealth: SystemHealthResponse = {
  status: "Degraded",
  generatedAt: "2026-05-24T10:00:00Z",
  durationMs: 84,
  summary: {
    healthy: 1,
    degraded: 1,
    unhealthy: 0,
    notMeasured: 1
  },
  checks: [
    {
      id: "qdrant-connectivity",
      name: "Qdrant connectivity",
      category: "Vector storage",
      status: "Healthy",
      message: "Vector storage accepted a health probe.",
      evidence: {
        endpoint: "http://localhost:6333",
        collections: 12
      },
      remediation: "",
      affects: ["Vector search"],
      durationMs: 18
    },
    {
      id: "neo4j-connectivity",
      name: "Neo4j connectivity",
      category: "Graph storage",
      status: "Degraded",
      message: "Graph storage responded slowly.",
      evidence: {
        latencyMs: 242,
        thresholdMs: 100
      },
      remediation: "Inspect Neo4j logs and restart graph storage if latency persists.",
      affects: ["Knowledge graph", "Graph retrieval"],
      durationMs: 242
    },
    {
      id: "rerank-provider",
      name: "Rerank provider",
      category: "AI providers",
      status: "NotMeasured",
      message: "Rerank provider probe is disabled.",
      evidence: {
        configured: false
      },
      remediation: "Enable rerank probe credentials before production rollout.",
      affects: ["Answer ranking"],
      durationMs: 0
    }
  ],
  fixFirst: [
    {
      checkId: "neo4j-connectivity",
      title: "Stabilize graph storage",
      status: "Degraded",
      remediation: "Inspect Neo4j logs and restart graph storage if latency persists.",
      affects: ["Knowledge graph", "Graph retrieval"]
    }
  ],
  featureImpacts: [
    {
      feature: "Graph retrieval",
      status: "Degraded",
      reason: "Graph responses are slow enough to reduce retrieval confidence.",
      affectedBy: ["neo4j-connectivity"],
      links: [{ label: "Open graph view", href: "/graph-view" }]
    }
  ]
};

afterEach(() => {
  getSystemHealth.mockReset();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  document.body.innerHTML = "";
});

describe("SystemStatusWorkbench source guard", () => {
  test("uses server-provided health aggregation fields without local aggregation", () => {
    const source = readFileSync(systemStatusWorkbenchPath, "utf8");

    expect(source).toContain("health.status");
    expect(source).toContain("health.summary");
    expect(source).toContain("health.checks");
    expect(source).toContain("health.fixFirst");
    expect(source).toContain("health.featureImpacts");
    expect(source).not.toMatch(/\b(?:const|let|var)\s+fixFirst\s*=/);
    expect(source).not.toMatch(/\b(?:const|let|var)\s+overallStatus\s*=/);
  });
});

describe("SystemStatusWorkbench compact diagnostics workbench", () => {
  test("renders server-provided summary, checks, fix-first priorities, feature impacts, and raw JSON", async () => {
    getSystemHealth.mockResolvedValue(degradedHealth);

    render(<SystemStatusWorkbench apiBase="/api-root" />);

    const summary = await screen.findByRole("region", { name: "System summary" });
    expect(screen.getByRole("heading", { name: "System Status" })).toBeInTheDocument();
    expect(screen.getByText("Diagnostics workbench")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Copy JSON" })).toBeInTheDocument();

    expect(summary).toHaveTextContent(/Healthy\s*1/);
    expect(summary).toHaveTextContent(/Degraded\s*1/);
    expect(summary).toHaveTextContent(/Unhealthy\s*0/);
    expect(summary).toHaveTextContent(/Not measured\s*1/);

    const checks = screen.getByRole("region", { name: "Health checks" });
    expect(within(checks).getByText("Qdrant connectivity")).toBeInTheDocument();
    expect(within(checks).getByText("Vector storage")).toBeInTheDocument();
    expect(within(checks).getByText("Healthy")).toBeInTheDocument();
    expect(within(checks).getByText("Vector storage accepted a health probe.")).toBeInTheDocument();
    expect(within(checks).getByText("18 ms")).toBeInTheDocument();
    expect(within(checks).getByText("Neo4j connectivity")).toBeInTheDocument();
    expect(within(checks).getByText("Graph storage")).toBeInTheDocument();
    expect(within(checks).getByText("Degraded")).toBeInTheDocument();
    expect(within(checks).getByText("Graph storage responded slowly.")).toBeInTheDocument();
    expect(within(checks).getByText("242 ms")).toBeInTheDocument();
    expect(within(checks).getByText("Inspect Neo4j logs and restart graph storage if latency persists.")).toBeInTheDocument();
    expect(within(checks).getByText("latencyMs")).toBeInTheDocument();
    expect(within(checks).getByText("242")).toBeInTheDocument();

    const fixFirst = screen.getByRole("region", { name: "Fix first" });
    expect(within(fixFirst).getByText("Stabilize graph storage")).toBeInTheDocument();
    expect(within(fixFirst).getByText("Inspect Neo4j logs and restart graph storage if latency persists.")).toBeInTheDocument();
    expect(within(fixFirst).getByText("Knowledge graph, Graph retrieval")).toBeInTheDocument();

    const featureImpact = screen.getByRole("region", { name: "Feature impact" });
    expect(within(featureImpact).getByText("Graph retrieval")).toBeInTheDocument();
    expect(within(featureImpact).getByText("Degraded")).toBeInTheDocument();
    expect(within(featureImpact).getByText("Graph responses are slow enough to reduce retrieval confidence.")).toBeInTheDocument();
    expect(within(featureImpact).getByText("neo4j-connectivity")).toBeInTheDocument();
    expect(within(featureImpact).getByRole("link", { name: "Open graph view" })).toHaveAttribute("href", "/graph-view");

    expect(screen.getByText(/"status": "Degraded"/)).toBeInTheDocument();
  });

  test("copies the pretty JSON payload and confirms the action", async () => {
    const writeText = vi.fn<Clipboard["writeText"]>().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", {
      ...navigator,
      clipboard: {
        writeText
      }
    });
    getSystemHealth.mockResolvedValue(degradedHealth);

    render(<SystemStatusWorkbench apiBase="/api-root" />);

    await screen.findByText("Stabilize graph storage");
    await userEvent.click(screen.getByRole("button", { name: "Copy JSON" }));

    expect(writeText).toHaveBeenCalledWith(JSON.stringify(degradedHealth, null, 2));
    expect(await screen.findByText("Copied.")).toBeInTheDocument();
  });
});

describe("SystemStatusWorkbench request states", () => {
  test("renders the loading state while health is loading", () => {
    getSystemHealth.mockReturnValue(new Promise<SystemHealthResponse>(() => undefined));

    render(<SystemStatusWorkbench apiBase="/api-root" />);

    expect(screen.getByText("Loading system status...")).toBeInTheDocument();
  });

  test("renders an API error message", async () => {
    getSystemHealth.mockRejectedValue(new Error("System health endpoint failed."));

    render(<SystemStatusWorkbench apiBase="/api-root" />);

    expect(await screen.findByText("System health endpoint failed.")).toBeInTheDocument();
  });
});
