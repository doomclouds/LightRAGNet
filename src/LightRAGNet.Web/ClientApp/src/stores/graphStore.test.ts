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
      properties: { entity_id: "ALPHA", entity_name: "Alpha", description: "old", weight: 1 }
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
      color: "#ccc",
      properties: { description: "old relation", weight: 0.5 }
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

  test("setRawGraph, selectNode, and selectEdge update exclusive selection state and notify subscribers", () => {
    const store = createGraphStoreState();
    const listener = vi.fn();

    store.subscribe(listener);
    store.setRawGraph(graph);
    store.selectNode("ALPHA");
    expect(store.getState().selectedNode?.id).toBe("ALPHA");
    expect(store.getState().selectedEdge).toBeNull();

    store.selectEdge("ALPHA-BETA");
    store.setIsFetching(true);

    expect(store.getState().selectedNode).toBeNull();
    expect(store.getState().selectedEdge?.id).toBe("ALPHA-BETA");
    expect(store.getState().isFetching).toBe(true);
    expect(listener).toHaveBeenCalledTimes(4);
  });

  test("selecting a node after an edge clears edge selection", () => {
    const store = createGraphStoreState({ rawGraph: graph });

    store.selectEdge("ALPHA-BETA");
    store.selectNode("BETA");

    expect(store.getState().selectedNode?.id).toBe("BETA");
    expect(store.getState().selectedEdge).toBeNull();
  });

  test("clearing node or edge selection only clears the requested selection", () => {
    const store = createGraphStoreState({ rawGraph: graph });

    store.selectNode("ALPHA");
    store.selectEdge(null);
    expect(store.getState().selectedNode?.id).toBe("ALPHA");
    expect(store.getState().selectedEdge).toBeNull();

    store.selectEdge("ALPHA-BETA");
    store.selectNode(null);
    expect(store.getState().selectedNode).toBeNull();
    expect(store.getState().selectedEdge?.id).toBe("ALPHA-BETA");

    store.resetSelection();
    expect(store.getState().selectedNode).toBeNull();
    expect(store.getState().selectedEdge).toBeNull();

    store.selectEdge("ALPHA-BETA");
    store.selectEdge(null);
    expect(store.getState().selectedNode).toBeNull();
    expect(store.getState().selectedEdge).toBeNull();
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

  test("renameNode synchronizes node identity, connected edges, and selectedNode", () => {
    const store = createGraphStoreState({ rawGraph: graph });

    store.selectNode("ALPHA");
    store.renameNode("ALPHA", "OMEGA");

    const state = store.getState();
    const renamedNode = state.rawGraph?.nodes.find((node) => node.id === "OMEGA");
    expect(renamedNode?.label).toBe("OMEGA");
    expect(renamedNode?.properties.entity_id).toBe("OMEGA");
    expect(renamedNode?.properties.entity_name).toBe("OMEGA");
    expect(state.rawGraph?.edges[0]?.source).toBe("OMEGA");
    expect(state.rawGraph?.edges[0]?.target).toBe("BETA");
    expect(state.selectedNode?.id).toBe("OMEGA");
    expect(state.selectedNode?.properties.entity_id).toBe("OMEGA");
    expect(graph.nodes[0]?.id).toBe("ALPHA");
    expect(graph.edges[0]?.source).toBe("ALPHA");
  });

  test("renameNode synchronizes selectedEdge endpoints and rawGraph edges", () => {
    const store = createGraphStoreState({ rawGraph: graph });

    store.selectEdge("ALPHA-BETA");
    store.renameNode("ALPHA", "OMEGA");

    const state = store.getState();
    expect(state.selectedEdge?.source).toBe("OMEGA");
    expect(state.selectedEdge?.target).toBe("BETA");
    expect(state.rawGraph?.edges[0]?.source).toBe("OMEGA");
    expect(state.rawGraph?.edges[0]?.target).toBe("BETA");
    expect(graph.edges[0]?.source).toBe("ALPHA");
  });

  test("updateEdgeProperty immutably updates rawGraph and selectedEdge", () => {
    const store = createGraphStoreState({ rawGraph: graph });

    store.selectEdge("ALPHA-BETA");
    store.updateEdgeProperty("ALPHA-BETA", "description", "new relation");

    const state = store.getState();
    expect(state.rawGraph?.edges[0]?.properties?.description).toBe("new relation");
    expect(state.selectedEdge?.properties?.description).toBe("new relation");
    expect(state.rawGraph).not.toBe(graph);
    expect(state.rawGraph?.edges[0]).not.toBe(graph.edges[0]);
    expect(graph.edges[0]?.properties?.description).toBe("old relation");
  });

  test("removeNode deletes the node, connected edges, and matching selection", () => {
    const store = createGraphStoreState({ rawGraph: graph });

    store.selectNode("ALPHA");
    store.removeNode("ALPHA");

    const state = store.getState();
    expect(state.rawGraph?.nodes.map((node) => node.id)).toEqual(["BETA"]);
    expect(state.rawGraph?.edges).toEqual([]);
    expect(state.selectedNode).toBeNull();
    expect(state.selectedEdge).toBeNull();
    expect(graph.nodes).toHaveLength(2);
    expect(graph.edges).toHaveLength(1);
  });

  test("removeEdge deletes the edge and clears matching selection", () => {
    const store = createGraphStoreState({ rawGraph: graph });

    store.selectEdge("ALPHA-BETA");
    store.removeEdge("ALPHA-BETA");

    const state = store.getState();
    expect(state.rawGraph?.nodes).toHaveLength(2);
    expect(state.rawGraph?.edges).toEqual([]);
    expect(state.selectedNode).toBeNull();
    expect(state.selectedEdge).toBeNull();
    expect(graph.edges).toHaveLength(1);
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
