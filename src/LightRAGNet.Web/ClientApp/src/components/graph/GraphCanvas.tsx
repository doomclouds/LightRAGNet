import { SigmaContainer, useLoadGraph, useRegisterEvents } from "@react-sigma/core";
import "@react-sigma/core/lib/style.css";
import { MultiUndirectedGraph } from "graphology";
import { useEffect, useMemo } from "react";

import { useGraphStore } from "../../stores/graphStore";
import type { GraphEdgeDto, GraphNodeDto, GraphViewDto, JsonValue } from "../../types/graph";

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

type SigmaGraphAttributes = Record<string, JsonValue | undefined>;

function readString(value: JsonValue | undefined): string | null {
  return typeof value === "string" && value.trim().length > 0 ? value : null;
}

function getNodeLabel(node: GraphNodeDto): string {
  return readString(node.properties.entity_id) ?? readString(node.properties.entity_name) ?? node.label ?? node.id;
}

function getEdgeLabel(edge: GraphEdgeDto): string {
  return readString(edge.properties?.description) ?? edge.type ?? `${edge.source} - ${edge.target}`;
}

function createGraphologyGraph(graph: GraphViewDto | null, selectedNodeId?: string, selectedEdgeId?: string) {
  const sigmaGraph = new MultiUndirectedGraph<SigmaGraphAttributes, SigmaGraphAttributes>();

  if (!graph) {
    return sigmaGraph;
  }

  const count = graph.nodes.length;
  const radius = Math.max(3, count * 0.9);

  graph.nodes.forEach((node, index) => {
    const angle = count === 0 ? 0 : (index / count) * Math.PI * 2;
    const isSelected = selectedNodeId === node.id;

    sigmaGraph.addNode(node.id, {
      x: Math.cos(angle) * radius,
      y: Math.sin(angle) * radius,
      label: getNodeLabel(node),
      size: isSelected ? Math.max(node.size + 4, 13) : Math.max(node.size, 8),
      color: isSelected ? "#0f766e" : node.color,
      type: node.type ?? undefined,
      properties: node.properties
    });
  });

  graph.edges.forEach((edge) => {
    if (!sigmaGraph.hasNode(edge.source) || !sigmaGraph.hasNode(edge.target) || sigmaGraph.hasEdge(edge.id)) {
      return;
    }

    const isSelected = selectedEdgeId === edge.id;
    sigmaGraph.addUndirectedEdgeWithKey(edge.id, edge.source, edge.target, {
      label: getEdgeLabel(edge),
      size: isSelected ? Math.max(edge.size + 2, 4) : Math.max(edge.size, 1),
      color: isSelected ? "#dc2626" : edge.color,
      type: edge.type ?? undefined,
      properties: edge.properties ?? {}
    });
  });

  return sigmaGraph;
}

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
