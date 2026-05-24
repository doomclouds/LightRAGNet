import { describe, expect, test } from "vitest";

import { createGraphologyGraph } from "@/components/graph/graphologyAdapter";
import type { GraphViewDto } from "@/types/graph";

describe("createGraphologyGraph", () => {
  test("does not seed loaded nodes on a single circular shell", () => {
    const graph: GraphViewDto = {
      nodes: Array.from({ length: 8 }, (_, index) => ({
        id: `NODE-${index}`,
        label: `Node ${index}`,
        size: 2,
        color: "#999999",
        type: "concept",
        properties: { entity_id: `NODE-${index}` }
      })),
      edges: Array.from({ length: 7 }, (_, index) => ({
        id: `EDGE-${index}`,
        source: `NODE-${index}`,
        target: `NODE-${index + 1}`,
        type: "related",
        size: 1,
        color: "#cccccc",
        properties: {}
      })),
      isTruncated: false
    };

    const sigmaGraph = createGraphologyGraph(graph);
    const radii = sigmaGraph.nodes().map((node) => {
      const x = sigmaGraph.getNodeAttribute(node, "x") as number;
      const y = sigmaGraph.getNodeAttribute(node, "y") as number;
      return Math.round(Math.hypot(x, y) * 1000) / 1000;
    });

    expect(new Set(radii).size).toBeGreaterThan(1);
  });

  test("keeps domain types separate from Sigma renderer type attributes", () => {
    const graph: GraphViewDto = {
      nodes: [
        {
          id: "CONCEPT",
          label: "Concept",
          size: 3,
          color: "#999999",
          type: "concept",
          properties: { entity_id: "CONCEPT" }
        }
      ],
      edges: [
        {
          id: "self",
          source: "CONCEPT",
          target: "CONCEPT",
          type: "related",
          size: 1,
          color: "#cccccc",
          properties: { description: "self relation" }
        }
      ],
      isTruncated: false
    };

    const sigmaGraph = createGraphologyGraph(graph);

    expect(sigmaGraph.getNodeAttribute("CONCEPT", "type")).toBeUndefined();
    expect(sigmaGraph.getNodeAttribute("CONCEPT", "domainType")).toBe("concept");
    expect(sigmaGraph.getEdgeAttribute("self", "type")).toBe("curvedNoArrow");
    expect(sigmaGraph.getEdgeAttribute("self", "domainType")).toBe("related");
  });

  test("renders multiple edges even when backend edge ids are blank", () => {
    const graph: GraphViewDto = {
      nodes: [
        { id: "A", label: "A", size: 2, color: "#999999", type: "concept", properties: {} },
        { id: "B", label: "B", size: 2, color: "#999999", type: "concept", properties: {} },
        { id: "C", label: "C", size: 2, color: "#999999", type: "concept", properties: {} }
      ],
      edges: [
        { id: "", source: "A", target: "B", type: "related", size: 1, color: "#cccccc", properties: {} },
        { id: "", source: "B", target: "C", type: "related", size: 1, color: "#cccccc", properties: {} }
      ],
      isTruncated: false
    };

    const sigmaGraph = createGraphologyGraph(graph);

    expect(sigmaGraph.size).toBe(2);
  });

  test("scales node size by relationship degree like Python LightRAG", () => {
    const graph: GraphViewDto = {
      nodes: [
        { id: "HUB", label: "Hub", size: 1, color: "#999999", type: "concept", properties: {} },
        { id: "A", label: "A", size: 1, color: "#999999", type: "concept", properties: {} },
        { id: "B", label: "B", size: 1, color: "#999999", type: "concept", properties: {} },
        { id: "C", label: "C", size: 1, color: "#999999", type: "concept", properties: {} }
      ],
      edges: [
        { id: "HUB-A", source: "HUB", target: "A", type: "related", size: 1, color: "#cccccc", properties: {} },
        { id: "HUB-B", source: "HUB", target: "B", type: "related", size: 1, color: "#cccccc", properties: {} },
        { id: "HUB-C", source: "HUB", target: "C", type: "related", size: 1, color: "#cccccc", properties: {} }
      ],
      isTruncated: false
    };

    const sigmaGraph = createGraphologyGraph(graph);

    expect(sigmaGraph.getNodeAttribute("HUB", "size")).toBe(20);
    expect(sigmaGraph.getNodeAttribute("A", "size")).toBe(4);
    expect(sigmaGraph.getNodeAttribute("B", "size")).toBe(4);
    expect(sigmaGraph.getNodeAttribute("C", "size")).toBe(4);
  });

  test("scales edge size by relation weight like Python LightRAG", () => {
    const graph: GraphViewDto = {
      nodes: [
        { id: "A", label: "A", size: 1, color: "#999999", type: "concept", properties: {} },
        { id: "B", label: "B", size: 1, color: "#999999", type: "concept", properties: {} },
        { id: "C", label: "C", size: 1, color: "#999999", type: "concept", properties: {} }
      ],
      edges: [
        { id: "low", source: "A", target: "B", type: "related", size: 1, color: "#cccccc", properties: { weight: 1 } },
        { id: "high", source: "A", target: "C", type: "related", size: 1, color: "#cccccc", properties: { weight: 9 } }
      ],
      isTruncated: false
    };

    const sigmaGraph = createGraphologyGraph(graph);

    expect(sigmaGraph.getEdgeAttribute("low", "size")).toBe(1.25);
    expect(sigmaGraph.getEdgeAttribute("high", "size")).toBe(5);
  });
});
