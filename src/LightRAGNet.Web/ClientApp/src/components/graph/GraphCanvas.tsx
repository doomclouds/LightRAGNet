import { SigmaContainer, useLoadGraph, useRegisterEvents } from "@react-sigma/core";
import "@react-sigma/core/lib/style.css";
import type { MultiUndirectedGraph } from "graphology";
import { useEffect, useMemo } from "react";

import { useGraphStore } from "../../stores/graphStore";
import type { GraphViewDto } from "../../types/graph";
import { createGraphologyGraph, type SigmaGraphAttributes } from "./graphologyAdapter";

type GraphCanvasProps = {
  graph: GraphViewDto | null;
  isFetching: boolean;
  errorMessage?: string | null;
};

type SigmaEventWithNode = {
  node: string;
};

type SigmaEventWithEdge = {
  edge: string;
};

function GraphEvents() {
  const registerEvents = useRegisterEvents();

  useEffect(() => {
    registerEvents({
      clickNode: (event: SigmaEventWithNode) => useGraphStore.selectNode(event.node),
      clickEdge: (event: SigmaEventWithEdge) => useGraphStore.selectEdge(event.edge),
      clickStage: () => useGraphStore.resetSelection()
    });
  }, [registerEvents]);

  return null;
}

function GraphLoader({ graph }: { graph: MultiUndirectedGraph<SigmaGraphAttributes, SigmaGraphAttributes> }) {
  const loadGraph = useLoadGraph();

  useEffect(() => {
    loadGraph(graph, true);
  }, [graph, loadGraph]);

  return null;
}

export function GraphCanvas({ graph, isFetching, errorMessage }: GraphCanvasProps) {
  const selectedNodeId = useGraphStore((state) => state.selectedNode?.id);
  const selectedEdgeId = useGraphStore((state) => state.selectedEdge?.id);
  const graphologyGraph = useMemo(
    () => createGraphologyGraph(graph, selectedNodeId, selectedEdgeId),
    [graph, selectedNodeId, selectedEdgeId]
  );
  const isEmpty = !graph || graph.nodes.length === 0;

  return (
    <section className="graph-workbench__canvas" aria-label="Graph canvas">
      <SigmaContainer
        className="graph-workbench__sigma"
        settings={{
          allowInvalidContainer: true,
          defaultEdgeType: "line",
          enableEdgeEvents: true,
          labelDensity: 0.08,
          labelRenderedSizeThreshold: 9,
          renderEdgeLabels: true
        }}
      >
        <GraphLoader graph={graphologyGraph} />
        <GraphEvents />
      </SigmaContainer>

      {(isFetching || errorMessage || isEmpty) && (
        <div className="graph-workbench__canvas-overlay" aria-live="polite">
          {isFetching ? <strong>Loading graph...</strong> : null}
          {!isFetching && errorMessage ? <strong>{errorMessage}</strong> : null}
          {!isFetching && !errorMessage && isEmpty ? (
            <>
              <strong>No graph loaded</strong>
              <span>Adjust the query controls and press Load.</span>
            </>
          ) : null}
        </div>
      )}
    </section>
  );
}
