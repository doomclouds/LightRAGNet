import { useSyncExternalStore } from "react";

export type GraphSettingsSnapshot = {
  queryLabel: string;
  maxDepth: number;
  maxNodes: number;
  showNodeLabels: boolean;
  showEdgeLabels: boolean;
  enableEdgeEvents: boolean;
  hideUnselectedEdges: boolean;
  minEdgeSize: number;
  maxEdgeSize: number;
  layoutIterations: number;
};

export type GraphSettingsStoreApi = {
  getState: () => GraphSettingsSnapshot;
  subscribe: (listener: () => void) => () => void;
  setQueryLabel: (queryLabel: string) => void;
  setMaxDepth: (maxDepth: number) => void;
  setMaxNodes: (maxNodes: number) => void;
  setShowNodeLabels: (showNodeLabels: boolean) => void;
  setShowEdgeLabels: (showEdgeLabels: boolean) => void;
  setEnableEdgeEvents: (enableEdgeEvents: boolean) => void;
  setHideUnselectedEdges: (hideUnselectedEdges: boolean) => void;
  setMinEdgeSize: (minEdgeSize: number) => void;
  setMaxEdgeSize: (maxEdgeSize: number) => void;
  setLayoutIterations: (layoutIterations: number) => void;
};

const defaultSnapshot: GraphSettingsSnapshot = {
  queryLabel: "*",
  maxDepth: 2,
  maxNodes: 100,
  showNodeLabels: true,
  showEdgeLabels: false,
  enableEdgeEvents: true,
  hideUnselectedEdges: false,
  minEdgeSize: 1.25,
  maxEdgeSize: 5,
  layoutIterations: 240
};

export function createGraphSettingsStoreState(
  initialState: Partial<GraphSettingsSnapshot> = {}
): GraphSettingsStoreApi {
  let state: GraphSettingsSnapshot = {
    ...defaultSnapshot,
    ...initialState
  };
  const listeners = new Set<() => void>();

  function setState(nextState: GraphSettingsSnapshot) {
    state = nextState;
    for (const listener of listeners) {
      listener();
    }
  }

  return {
    getState: () => state,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    setQueryLabel: (queryLabel) => setState({ ...state, queryLabel }),
    setMaxDepth: (maxDepth) => setState({ ...state, maxDepth }),
    setMaxNodes: (maxNodes) => setState({ ...state, maxNodes }),
    setShowNodeLabels: (showNodeLabels) => setState({ ...state, showNodeLabels }),
    setShowEdgeLabels: (showEdgeLabels) => setState({ ...state, showEdgeLabels }),
    setEnableEdgeEvents: (enableEdgeEvents) => setState({ ...state, enableEdgeEvents }),
    setHideUnselectedEdges: (hideUnselectedEdges) => setState({ ...state, hideUnselectedEdges }),
    setMinEdgeSize: (minEdgeSize) => setState({ ...state, minEdgeSize }),
    setMaxEdgeSize: (maxEdgeSize) => setState({ ...state, maxEdgeSize }),
    setLayoutIterations: (layoutIterations) => setState({ ...state, layoutIterations })
  };
}

const graphSettingsStore = createGraphSettingsStoreState();

function useGraphSettingsStoreHook<T>(selector: (state: GraphSettingsSnapshot) => T): T;
function useGraphSettingsStoreHook(): GraphSettingsSnapshot;
function useGraphSettingsStoreHook<T = GraphSettingsSnapshot>(
  selector?: (state: GraphSettingsSnapshot) => T
): T | GraphSettingsSnapshot {
  return useSyncExternalStore(
    graphSettingsStore.subscribe,
    () => (selector ? selector(graphSettingsStore.getState()) : graphSettingsStore.getState()),
    () => (selector ? selector(graphSettingsStore.getState()) : graphSettingsStore.getState())
  );
}

export const useGraphSettingsStore = Object.assign(useGraphSettingsStoreHook, graphSettingsStore);
