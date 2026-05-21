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
  renameNode: (oldId: string, newId: string) => void;
  updateEdgeProperty: (edgeId: string, key: string, value: JsonValue) => void;
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
      const selectedNode = findNode(state.rawGraph, nodeId);
      setState({
        ...state,
        selectedNode,
        selectedEdge: selectedNode ? null : state.selectedEdge
      });
    },
    selectEdge: (edgeId) => {
      const selectedEdge = findEdge(state.rawGraph, edgeId);
      setState({
        ...state,
        selectedNode: selectedEdge ? null : state.selectedNode,
        selectedEdge
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
    renameNode: (oldId, newId) => {
      if (!state.rawGraph || oldId === newId) {
        return;
      }

      let updatedNode: GraphNodeDto | null = null;
      const nodes = state.rawGraph.nodes.map((node) => {
        if (node.id !== oldId) {
          return node;
        }

        const properties: GraphNodeProperties = {
          ...node.properties,
          entity_id: newId,
          entity_name: newId
        };
        updatedNode = {
          ...node,
          id: newId,
          label: newId,
          properties
        };
        return updatedNode;
      });

      if (!updatedNode) {
        return;
      }

      const edges = state.rawGraph.edges.map((edge) => {
        if (edge.source !== oldId && edge.target !== oldId) {
          return edge;
        }

        return {
          ...edge,
          source: edge.source === oldId ? newId : edge.source,
          target: edge.target === oldId ? newId : edge.target
        };
      });

      const selectedEdge = state.selectedEdge
        ? edges.find((edge) => edge.id === state.selectedEdge?.id) ?? state.selectedEdge
        : null;

      setState({
        ...state,
        rawGraph: { ...state.rawGraph, nodes, edges },
        selectedNode: state.selectedNode?.id === oldId ? updatedNode : state.selectedNode,
        selectedEdge
      });
    },
    updateEdgeProperty: (edgeId, key, value) => {
      if (!state.rawGraph) {
        return;
      }

      let updatedEdge: GraphEdgeDto | null = null;
      const edges = state.rawGraph.edges.map((edge) => {
        if (edge.id !== edgeId) {
          return edge;
        }

        const properties: GraphNodeProperties = {
          ...(edge.properties ?? {}),
          [key]: value
        };
        updatedEdge = { ...edge, properties };
        return updatedEdge;
      });

      if (!updatedEdge) {
        return;
      }

      setState({
        ...state,
        rawGraph: { ...state.rawGraph, edges },
        selectedEdge: state.selectedEdge?.id === edgeId ? updatedEdge : state.selectedEdge
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
