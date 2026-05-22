import { useSyncExternalStore } from "react";

import type { GraphEdgeDto, GraphNodeDto, GraphNodeProperties, GraphViewDto, JsonValue } from "../types/graph";

export type GraphStoreSnapshot = {
  rawGraph: GraphViewDto | null;
  selectedNode: GraphNodeDto | null;
  selectedEdge: GraphEdgeDto | null;
  selectedEdgeKey: string | null;
  focusedNode: GraphNodeDto | null;
  focusedEdge: GraphEdgeDto | null;
  focusedEdgeKey: string | null;
  isFetching: boolean;
  sigmaInstance: unknown | null;
};

export type GraphStoreApi = {
  getState: () => GraphStoreSnapshot;
  subscribe: (listener: () => void) => () => void;
  setRawGraph: (graph: GraphViewDto | null) => void;
  selectNode: (nodeId: string | null) => void;
  selectEdge: (edgeId: string | null) => void;
  focusNode: (nodeId: string | null) => void;
  focusEdge: (edgeId: string | null) => void;
  setSigmaInstance: (sigmaInstance: unknown | null) => void;
  setIsFetching: (isFetching: boolean) => void;
  updateNodeProperty: (nodeId: string, key: string, value: JsonValue) => void;
  renameNode: (oldId: string, newId: string) => void;
  removeNode: (nodeId: string) => void;
  updateEdgeProperty: (edgeId: string, key: string, value: JsonValue) => void;
  removeEdge: (edgeId: string) => void;
  resetSelection: () => void;
};

const defaultSnapshot: GraphStoreSnapshot = {
  rawGraph: null,
  selectedNode: null,
  selectedEdge: null,
  selectedEdgeKey: null,
  focusedNode: null,
  focusedEdge: null,
  focusedEdgeKey: null,
  isFetching: false,
  sigmaInstance: null
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

  function getEdgeKey(edge: GraphEdgeDto, index: number): string {
    return edge.id.trim().length > 0 ? edge.id : `${edge.source}->${edge.target}:${index}`;
  }

  function findEdgeWithKey(
    graph: GraphViewDto | null,
    edgeId: string | null
  ): { edge: GraphEdgeDto | null; key: string | null } {
    if (!graph || !edgeId) {
      return { edge: null, key: null };
    }

    for (let index = 0; index < graph.edges.length; index += 1) {
      const edge = graph.edges[index];
      if (!edge) {
        continue;
      }

      const key = getEdgeKey(edge, index);
      if (edge.id === edgeId || key === edgeId) {
        return { edge, key };
      }
    }

    return { edge: null, key: null };
  }

  function edgeMatches(edge: GraphEdgeDto, index: number, edgeId: string | null): boolean {
    return edgeId !== null && (edge.id === edgeId || getEdgeKey(edge, index) === edgeId);
  }

  return {
    getState: () => state,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    setRawGraph: (graph) => {
      const selectedEdge = findEdgeWithKey(graph, state.selectedEdgeKey ?? state.selectedEdge?.id ?? null);
      const focusedEdge = findEdgeWithKey(graph, state.focusedEdgeKey ?? state.focusedEdge?.id ?? null);
      setState({
        ...state,
        rawGraph: graph,
        selectedNode: findNode(graph, state.selectedNode?.id ?? null),
        selectedEdge: selectedEdge.edge,
        selectedEdgeKey: selectedEdge.key,
        focusedNode: findNode(graph, state.focusedNode?.id ?? null),
        focusedEdge: focusedEdge.edge,
        focusedEdgeKey: focusedEdge.key
      });
    },
    selectNode: (nodeId) => {
      const selectedNode = findNode(state.rawGraph, nodeId);
      setState({
        ...state,
        selectedNode,
        selectedEdge: selectedNode ? null : state.selectedEdge,
        selectedEdgeKey: selectedNode ? null : state.selectedEdgeKey
      });
    },
    selectEdge: (edgeId) => {
      const selectedEdge = findEdgeWithKey(state.rawGraph, edgeId);
      setState({
        ...state,
        selectedNode: selectedEdge.edge ? null : state.selectedNode,
        selectedEdge: selectedEdge.edge,
        selectedEdgeKey: selectedEdge.key
      });
    },
    focusNode: (nodeId) => {
      setState({
        ...state,
        focusedNode: findNode(state.rawGraph, nodeId)
      });
    },
    focusEdge: (edgeId) => {
      const focusedEdge = findEdgeWithKey(state.rawGraph, edgeId);
      setState({
        ...state,
        focusedEdge: focusedEdge.edge,
        focusedEdgeKey: focusedEdge.key
      });
    },
    setSigmaInstance: (sigmaInstance) => {
      setState({ ...state, sigmaInstance });
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
        selectedNode: state.selectedNode?.id === nodeId ? updatedNode : state.selectedNode,
        focusedNode: state.focusedNode?.id === nodeId ? updatedNode : state.focusedNode
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

      const renamedGraph = { ...state.rawGraph, nodes, edges };
      const selectedEdge = state.selectedEdge
        ? findEdgeWithKey(renamedGraph, state.selectedEdgeKey ?? state.selectedEdge.id).edge ?? state.selectedEdge
        : null;
      const focusedEdge = state.focusedEdge
        ? findEdgeWithKey(renamedGraph, state.focusedEdgeKey ?? state.focusedEdge.id).edge ?? state.focusedEdge
        : null;

      setState({
        ...state,
        rawGraph: renamedGraph,
        selectedNode: state.selectedNode?.id === oldId ? updatedNode : state.selectedNode,
        selectedEdge,
        focusedNode: state.focusedNode?.id === oldId ? updatedNode : state.focusedNode,
        focusedEdge
      });
    },
    removeNode: (nodeId) => {
      if (!state.rawGraph) {
        return;
      }

      const nodes = state.rawGraph.nodes.filter((node) => node.id !== nodeId);
      if (nodes.length === state.rawGraph.nodes.length) {
        return;
      }

      const edges = state.rawGraph.edges.filter((edge) => edge.source !== nodeId && edge.target !== nodeId);
      const selectedEdgeStillExists =
        state.selectedEdge && edges.some((edge, index) => edgeMatches(edge, index, state.selectedEdgeKey ?? state.selectedEdge?.id ?? null))
          ? state.selectedEdge
          : null;
      const focusedEdgeStillExists =
        state.focusedEdge && edges.some((edge, index) => edgeMatches(edge, index, state.focusedEdgeKey ?? state.focusedEdge?.id ?? null))
          ? state.focusedEdge
          : null;

      setState({
        ...state,
        rawGraph: { ...state.rawGraph, nodes, edges },
        selectedNode: state.selectedNode?.id === nodeId ? null : state.selectedNode,
        selectedEdge: selectedEdgeStillExists,
        selectedEdgeKey: selectedEdgeStillExists ? state.selectedEdgeKey : null,
        focusedNode: state.focusedNode?.id === nodeId ? null : state.focusedNode,
        focusedEdge: focusedEdgeStillExists,
        focusedEdgeKey: focusedEdgeStillExists ? state.focusedEdgeKey : null
      });
    },
    updateEdgeProperty: (edgeId, key, value) => {
      if (!state.rawGraph) {
        return;
      }

      let updatedEdge: GraphEdgeDto | null = null;
      const target = findEdgeWithKey(state.rawGraph, edgeId);
      if (!target.edge) {
        return;
      }

      const edges = state.rawGraph.edges.map((edge, index) => {
        if (!edgeMatches(edge, index, target.key)) {
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
        selectedEdge: state.selectedEdgeKey === target.key || state.selectedEdge?.id === edgeId ? updatedEdge : state.selectedEdge,
        focusedEdge: state.focusedEdgeKey === target.key || state.focusedEdge?.id === edgeId ? updatedEdge : state.focusedEdge
      });
    },
    removeEdge: (edgeId) => {
      if (!state.rawGraph) {
        return;
      }

      const target = findEdgeWithKey(state.rawGraph, edgeId);
      if (!target.edge) {
        return;
      }

      const edges = state.rawGraph.edges.filter((edge, index) => !edgeMatches(edge, index, target.key));
      if (edges.length === state.rawGraph.edges.length) {
        return;
      }

      setState({
        ...state,
        rawGraph: { ...state.rawGraph, edges },
        selectedEdge: state.selectedEdgeKey === target.key || state.selectedEdge?.id === edgeId ? null : state.selectedEdge,
        selectedEdgeKey: state.selectedEdgeKey === target.key || state.selectedEdge?.id === edgeId ? null : state.selectedEdgeKey,
        focusedEdge: state.focusedEdgeKey === target.key || state.focusedEdge?.id === edgeId ? null : state.focusedEdge,
        focusedEdgeKey: state.focusedEdgeKey === target.key || state.focusedEdge?.id === edgeId ? null : state.focusedEdgeKey
      });
    },
    resetSelection: () => {
      setState({
        ...state,
        selectedNode: null,
        selectedEdge: null,
        selectedEdgeKey: null,
        focusedNode: null,
        focusedEdge: null,
        focusedEdgeKey: null
      });
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
