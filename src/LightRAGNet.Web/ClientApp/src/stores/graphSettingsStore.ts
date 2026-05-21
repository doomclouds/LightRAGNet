import { useSyncExternalStore } from "react";

export type GraphSettingsSnapshot = {
  queryLabel: string;
  maxDepth: number;
  maxNodes: number;
};

export type GraphSettingsStoreApi = {
  getState: () => GraphSettingsSnapshot;
  subscribe: (listener: () => void) => () => void;
  setQueryLabel: (queryLabel: string) => void;
  setMaxDepth: (maxDepth: number) => void;
  setMaxNodes: (maxNodes: number) => void;
};

const defaultSnapshot: GraphSettingsSnapshot = {
  queryLabel: "*",
  maxDepth: 2,
  maxNodes: 100
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
    setMaxNodes: (maxNodes) => setState({ ...state, maxNodes })
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
