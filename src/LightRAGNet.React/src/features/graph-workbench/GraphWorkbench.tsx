import { useCallback, useEffect, useRef, useState } from "react";

import { getGraphConfig, queryGraph } from "@/api/graphApi";
import { GraphCanvas } from "@/components/graph/GraphCanvas";
import { GraphLayoutControls } from "@/components/graph/GraphLayoutControls";
import { GraphLegend } from "@/components/graph/GraphLegend";
import { GraphQueryControls } from "@/components/graph/GraphQueryControls";
import { GraphSearchBox } from "@/components/graph/GraphSearchBox";
import { GraphSettingsPanel } from "@/components/graph/GraphSettingsPanel";
import { GraphViewportControls } from "@/components/graph/GraphViewportControls";
import { PropertiesPanel } from "@/components/graph/PropertiesPanel";
import "@/features/graph-workbench/graph-workbench.css";
import { useGraphSettingsStore } from "@/stores/graphSettingsStore";
import { useGraphStore } from "@/stores/graphStore";

type GraphWorkbenchProps = {
  apiBase: string;
};

export function GraphWorkbench({ apiBase }: GraphWorkbenchProps) {
  const settings = useGraphSettingsStore();
  const rawGraph = useGraphStore((state) => state.rawGraph);
  const isFetching = useGraphStore((state) => state.isFetching);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [legendVisible, setLegendVisible] = useState(false);
  const initialLoadStarted = useRef(false);

  useEffect(() => {
    let cancelled = false;
    void getGraphConfig(apiBase)
      .then((config) => {
        if (!cancelled) {
          useGraphSettingsStore.setMaxNodesLimit(config.maxNodesLimit);
        }
      })
      .catch(() => {
        if (!cancelled) {
          useGraphSettingsStore.setMaxNodesLimit(2000);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [apiBase]);

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
    <section className="graph-workbench" data-api-base={apiBase}>
      <div className="graph-workbench__main">
        <GraphCanvas graph={rawGraph} isFetching={isFetching} errorMessage={errorMessage}>
          <div className="graph-workbench__top-left">
            <GraphQueryControls apiBase={apiBase} isFetching={isFetching} onLoad={() => void loadGraph()} />
            <GraphSearchBox />
          </div>
          <div className="graph-workbench__control-dock">
            <GraphLayoutControls />
            <GraphSettingsPanel />
            <GraphViewportControls
              legendVisible={legendVisible}
              onToggleLegend={() => setLegendVisible((visible) => !visible)}
            />
          </div>
          <GraphLegend visible={legendVisible} />
        </GraphCanvas>
        <PropertiesPanel apiBase={apiBase} />
      </div>
    </section>
  );
}
