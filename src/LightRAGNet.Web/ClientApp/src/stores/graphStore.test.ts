import { describe, expect, test, vi } from "vitest";

import { createGraphStoreState } from "./graphStore";
import { createGraphSettingsStoreState } from "./graphSettingsStore";
import type { GraphViewDto } from "../types/graph";

const graph: GraphViewDto = {
  nodes: [
    {
      id: "ALPHA",
      label: "Alpha",
      size: 1,
      color: "#999",
      type: "PERSON",
      properties: { description: "old", weight: 1 }
    },
    {
      id: "BETA",
      label: "Beta",
      size: 1,
      color: "#999",
      type: "PLACE",
      properties: {}
    }
  ],
  edges: [
    {
      id: "ALPHA-BETA",
      source: "ALPHA",
      target: "BETA",
      type: "related",
      size: 1,
      color: "#ccc"
    }
  ],
  isTruncated: false
};

describe("graphStore", () => {
  test("createGraphStoreState exposes default graph selection state", () => {
    const store = createGraphStoreState();

    expect(store.getState()).toMatchObject({
      rawGraph: null,
      selectedNode: null,
      selectedEdge: null,
      isFetching: false
    });
  });

  test("setRawGraph, selectNode, and selectEdge update state and notify subscribers", () => {
    const store = createGraphStoreState();
    const listener = vi.fn();

    store.subscribe(listener);
    store.setRawGraph(graph);
    store.selectNode("ALPHA");
    store.selectEdge("ALPHA-BETA");
    store.setIsFetching(true);

    expect(store.getState().selectedNode?.id).toBe("ALPHA");
    expect(store.getState().selectedEdge?.id).toBe("ALPHA-BETA");
    expect(store.getState().isFetching).toBe(true);
    expect(listener).toHaveBeenCalledTimes(4);
  });

  test("updateNodeProperty immutably updates rawGraph and selectedNode", () => {
    const store = createGraphStoreState({ rawGraph: graph });

    store.selectNode("ALPHA");
    store.updateNodeProperty("ALPHA", "description", "new");

    const state = store.getState();
    expect(state.rawGraph?.nodes[0]?.properties.description).toBe("new");
    expect(state.selectedNode?.properties.description).toBe("new");
    expect(state.rawGraph).not.toBe(graph);
    expect(state.rawGraph?.nodes[0]).not.toBe(graph.nodes[0]);
    expect(graph.nodes[0]?.properties.description).toBe("old");
  });
});

describe("graphSettingsStore", () => {
  test("exposes default query settings and setters", () => {
    const store = createGraphSettingsStoreState();
    const listener = vi.fn();

    store.subscribe(listener);
    store.setQueryLabel("ALPHA");
    store.setMaxDepth(4);
    store.setMaxNodes(250);

    expect(store.getState()).toEqual({
      queryLabel: "ALPHA",
      maxDepth: 4,
      maxNodes: 250
    });
    expect(listener).toHaveBeenCalledTimes(3);
  });

  test("defaults match the initial graph query controls", () => {
    const store = createGraphSettingsStoreState();

    expect(store.getState()).toEqual({
      queryLabel: "*",
      maxDepth: 2,
      maxNodes: 100
    });
  });
});
