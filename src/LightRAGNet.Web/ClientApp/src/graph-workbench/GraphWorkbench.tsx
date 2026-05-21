import { useCallback, useEffect, useRef, useState } from "react";

import { queryGraph } from "../api/graphApi";
import { GraphCanvas } from "../components/graph/GraphCanvas";
import { GraphToolbar } from "../components/graph/GraphToolbar";
import { PropertiesPanel } from "../components/graph/PropertiesPanel";
import { useGraphSettingsStore } from "../stores/graphSettingsStore";
import { useGraphStore } from "../stores/graphStore";

type GraphWorkbenchProps = {
  apiBase: string;
};

export function GraphWorkbench({ apiBase }: GraphWorkbenchProps) {
  const settings = useGraphSettingsStore();
  const rawGraph = useGraphStore((state) => state.rawGraph);
  const isFetching = useGraphStore((state) => state.isFetching);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const initialLoadStarted = useRef(false);

  const loadGraph = useCallback(async () => {
    useGraphStore.setIsFetching(true);
    setErrorMessage(null);

    try {
      const graph = await queryGraph(apiBase, settings.queryLabel, settings.maxDepth, settings.maxNodes);
      useGraphStore.setRawGraph(graph);
    } catch (error) {
      const message = error instanceof Error ? error.message : "Failed to load graph.";
      setErrorMessage(message);
    } finally {
      useGraphStore.setIsFetching(false);
    }
  }, [apiBase, settings.maxDepth, settings.maxNodes, settings.queryLabel]);

  useEffect(() => {
    if (initialLoadStarted.current) {
      return;
    }

    initialLoadStarted.current = true;
    void loadGraph();
  }, [loadGraph]);

  return (
    <main className="graph-workbench" data-api-base={apiBase}>
      <GraphToolbar isFetching={isFetching} onLoad={() => void loadGraph()} />

      <div className="graph-workbench__main">
        <GraphCanvas graph={rawGraph} isFetching={isFetching} errorMessage={errorMessage} />
        <PropertiesPanel apiBase={apiBase} />
      </div>
    </main>
  );
}
