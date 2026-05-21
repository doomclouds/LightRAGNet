import { useSyncExternalStore } from "react";

import type { GraphEdgeDto, GraphNodeDto, GraphNodeProperties, GraphViewDto, JsonValue } from "../types/graph";

export type GraphStoreSnapshot = {
  rawGraph: GraphViewDto | null;
  selectedNode: GraphNodeDto | null;
  selectedEdge: GraphEdgeDto | null;
  isFetching: boolean;
};

export type GraphStoreApi = {
  getState: () => GraphStoreSnapshot;
  subscribe: (listener: () => void) => () => void;
  setRawGraph: (graph: GraphViewDto | null) => void;
  selectNode: (nodeId: string | null) => void;
  selectEdge: (edgeId: string | null) => void;
  setIsFetching: (isFetching: boolean) => void;
  updateNodeProperty: (nodeId: string, key: string, value: JsonValue) => void;
  resetSelection: () => void;
};

const defaultSnapshot: GraphStoreSnapshot = {
  rawGraph: null,
  selectedNode: null,
  selectedEdge: null,
  isFetching: false
};

export function createGraphStoreState(initialState: Partial<GraphStoreSnapshot> = {}): GraphStoreApi {
  let state: GraphStoreSnapshot = {
    ...defaultSnapshot,
    ...initialState
  };
  const listeners = new Set<() => void>();

  function emit() {
    for (const listener of listeners) {
      listener();
    }
  }

  function setState(nextState: GraphStoreSnapshot) {
    state = nextState;
    emit();
  }

  function findNode(graph: GraphViewDto | null, nodeId: string | null): GraphNodeDto | null {
    return graph?.nodes.find((node) => node.id === nodeId) ?? null;
  }

  function findEdge(graph: GraphViewDto | null, edgeId: string | null): GraphEdgeDto | null {
    return graph?.edges.find((edge) => edge.id === edgeId) ?? null;
  }

  return {
    getState: () => state,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    setRawGraph: (graph) => {
      setState({
        ...state,
        rawGraph: graph,
        selectedNode: findNode(graph, state.selectedNode?.id ?? null),
        selectedEdge: findEdge(graph, state.selectedEdge?.id ?? null)
      });
    },
    selectNode: (nodeId) => {
      setState({
        ...state,
        selectedNode: findNode(state.rawGraph, nodeId)
      });
    },
    selectEdge: (edgeId) => {
      setState({
        ...state,
        selectedEdge: findEdge(state.rawGraph, edgeId)
      });
    },
    setIsFetching: (isFetching) => {
      setState({ ...state, isFetching });
    },
    updateNodeProperty: (nodeId, key, value) => {
      if (!state.rawGraph) {
        return;
      }

      let updatedNode: GraphNodeDto | null = null;
      const nodes = state.rawGraph.nodes.map((node) => {
        if (node.id !== nodeId) {
          return node;
        }

        const properties: GraphNodeProperties = {
          ...node.properties,
          [key]: value
        };
        updatedNode = { ...node, properties };
        return updatedNode;
      });

      if (!updatedNode) {
        return;
      }

      setState({
        ...state,
        rawGraph: { ...state.rawGraph, nodes },
        selectedNode: state.selectedNode?.id === nodeId ? updatedNode : state.selectedNode
      });
    },
    resetSelection: () => {
      setState({ ...state, selectedNode: null, selectedEdge: null });
    }
  };
}

const graphStore = createGraphStoreState();

function useGraphStoreHook<T>(selector: (state: GraphStoreSnapshot) => T): T;
function useGraphStoreHook(): GraphStoreSnapshot;
function useGraphStoreHook<T = GraphStoreSnapshot>(selector?: (state: GraphStoreSnapshot) => T): T | GraphStoreSnapshot {
  return useSyncExternalStore(
    graphStore.subscribe,
    () => (selector ? selector(graphStore.getState()) : graphStore.getState()),
    () => (selector ? selector(graphStore.getState()) : graphStore.getState())
  );
}

export const useGraphStore = Object.assign(useGraphStoreHook, graphStore);
