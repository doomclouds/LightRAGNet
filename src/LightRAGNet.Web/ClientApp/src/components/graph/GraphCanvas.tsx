import { SigmaContainer, useLoadGraph, useRegisterEvents, useSetSettings, useSigma } from "@react-sigma/core";
import "@react-sigma/core/lib/style.css";
import { useLayoutForceAtlas2 } from "@react-sigma/layout-forceatlas2";
import { createEdgeCurveProgram, EdgeCurvedArrowProgram } from "@sigma/edge-curve";
import { NodeBorderProgram } from "@sigma/node-border";
import type { MultiUndirectedGraph } from "graphology";
import type { ReactNode } from "react";
import { useEffect, useMemo, useState } from "react";
import { EdgeArrowProgram, NodeCircleProgram, NodePointProgram } from "sigma/rendering";

import { useGraphStore } from "../../stores/graphStore";
import type { GraphViewDto } from "../../types/graph";
import { createGraphologyGraph, type SigmaGraphAttributes } from "./graphologyAdapter";

type GraphCanvasProps = {
  graph: GraphViewDto | null;
  isFetching: boolean;
  errorMessage?: string | null;
  children?: ReactNode;
};

type SigmaEventWithNode = {
  node: string;
};

type SigmaEventWithEdge = {
  edge: string;
};

function GraphEvents() {
  const registerEvents = useRegisterEvents();
  const sigma = useSigma();
  const [draggedNode, setDraggedNode] = useState<string | null>(null);

  useEffect(() => {
    registerEvents({
      enterNode: (event: SigmaEventWithNode) => useGraphStore.focusNode(event.node),
      leaveNode: () => useGraphStore.focusNode(null),
      clickNode: (event: SigmaEventWithNode) => useGraphStore.selectNode(event.node),
      enterEdge: (event: SigmaEventWithEdge) => useGraphStore.focusEdge(event.edge),
      leaveEdge: () => useGraphStore.focusEdge(null),
      clickEdge: (event: SigmaEventWithEdge) => useGraphStore.selectEdge(event.edge),
      clickStage: () => useGraphStore.resetSelection(),
      downNode: (event: SigmaEventWithNode) => {
        setDraggedNode(event.node);
        sigma.getGraph().setNodeAttribute(event.node, "highlighted", true);
      },
      mousemovebody: (event: { preventSigmaDefault: () => void; original: Event; x: number; y: number }) => {
        if (!draggedNode) {
          return;
        }

        const position = sigma.viewportToGraph(event);
        sigma.getGraph().setNodeAttribute(draggedNode, "x", position.x);
        sigma.getGraph().setNodeAttribute(draggedNode, "y", position.y);
        event.preventSigmaDefault();
        event.original.preventDefault();
      },
      mouseup: () => {
        if (!draggedNode) {
          return;
        }

        sigma.getGraph().removeNodeAttribute(draggedNode, "highlighted");
        setDraggedNode(null);
      }
    });
  }, [draggedNode, registerEvents, sigma]);

  return null;
}

function GraphLoader({ graph }: { graph: MultiUndirectedGraph<SigmaGraphAttributes, SigmaGraphAttributes> }) {
  const loadGraph = useLoadGraph();

  useEffect(() => {
    loadGraph(graph, true);
  }, [graph, loadGraph]);

  return null;
}

function GraphAutoLayout({ graph }: { graph: MultiUndirectedGraph<SigmaGraphAttributes, SigmaGraphAttributes> }) {
  const { assign } = useLayoutForceAtlas2({
    iterations: 220,
    settings: {
      barnesHutOptimize: graph.order > 60,
      edgeWeightInfluence: 0.7,
      gravity: 0.04,
      linLogMode: true,
      scalingRatio: 28,
      slowDown: 2
    }
  });

  useEffect(() => {
    if (graph.order === 0) {
      return;
    }

    const frame = window.requestAnimationFrame(() => assign());
    return () => window.cancelAnimationFrame(frame);
  }, [assign, graph]);

  return null;
}

function GraphReducers() {
  const sigma = useSigma();
  const setSettings = useSetSettings();
  const selectedNodeId = useGraphStore((state) => state.selectedNode?.id ?? null);
  const focusedNodeId = useGraphStore((state) => state.focusedNode?.id ?? null);
  const selectedEdgeKey = useGraphStore((state) => state.selectedEdgeKey ?? state.selectedEdge?.id ?? null);
  const focusedEdgeKey = useGraphStore((state) => state.focusedEdgeKey ?? state.focusedEdge?.id ?? null);

  useEffect(() => {
    useGraphStore.setSigmaInstance(sigma);
    return () => useGraphStore.setSigmaInstance(null);
  }, [sigma]);

  useEffect(() => {
    setSettings({
      nodeReducer: (node, data) => {
        const graph = sigma.getGraph();
        const activeNode = focusedNodeId ?? selectedNodeId;
        const activeEdge = focusedEdgeKey ?? selectedEdgeKey;
        const nextData = { ...data };

        if (selectedNodeId === node) {
          nextData.borderColor = "#0f172a";
          nextData.highlighted = true;
          nextData.forceLabel = true;
        }

        if (activeNode && graph.hasNode(activeNode)) {
          const isNeighbor = node === activeNode || graph.neighbors(activeNode).includes(node);
          if (isNeighbor) {
            nextData.highlighted = true;
            nextData.forceLabel = node === activeNode;
          } else {
            nextData.color = "#cbd5d1";
            nextData.labelColor = "#87958f";
          }
        }

        if (activeEdge && graph.hasEdge(activeEdge) && graph.extremities(activeEdge).includes(node)) {
          nextData.highlighted = true;
          nextData.forceLabel = true;
        }

        return nextData;
      },
      edgeReducer: (edge, data) => {
        const graph = sigma.getGraph();
        const activeNode = focusedNodeId ?? selectedNodeId;
        const nextData = { ...data };

        if (edge === selectedEdgeKey) {
          nextData.color = "#dc2626";
          nextData.size = Math.max(Number(data.size ?? 1.75), 4);
        } else if (edge === focusedEdgeKey) {
          nextData.color = "#0891b2";
          nextData.size = Math.max(Number(data.size ?? 1.75), 3);
        }

        if (activeNode && graph.hasNode(activeNode) && !graph.extremities(edge).includes(activeNode)) {
          nextData.color = "#d1d9d5";
          nextData.size = Math.max(Number(data.size ?? 1.75) * 0.65, 0.8);
        }

        if (selectedEdgeKey && edge !== selectedEdgeKey) {
          nextData.color = "#d1d9d5";
        }

        return nextData;
      }
    });
  }, [focusedEdgeKey, focusedNodeId, selectedEdgeKey, selectedNodeId, setSettings, sigma]);

  return null;
}

function GraphCameraFocus() {
  const sigma = useSigma();
  const focusedNodeId = useGraphStore((state) => state.focusedNode?.id ?? null);
  const selectedNodeId = useGraphStore((state) => state.selectedNode?.id ?? null);
  const nodeId = focusedNodeId ?? selectedNodeId;

  useEffect(() => {
    if (!nodeId) {
      return;
    }

    const graph = sigma.getGraph();
    if (!graph.hasNode(nodeId)) {
      return;
    }

    const x = graph.getNodeAttribute(nodeId, "x");
    const y = graph.getNodeAttribute(nodeId, "y");
    if (typeof x !== "number" || typeof y !== "number") {
      return;
    }

    sigma.getCamera().animate({ x, y, ratio: 0.55 }, { duration: 420 });
  }, [nodeId, sigma]);

  return null;
}

export function GraphCanvas({ graph, isFetching, errorMessage, children }: GraphCanvasProps) {
  const graphologyGraph = useMemo(() => createGraphologyGraph(graph), [graph]);
  const isEmpty = !graph || graph.nodes.length === 0;

  return (
    <section className="graph-workbench__canvas" aria-label="Graph canvas">
      <SigmaContainer
        className="graph-workbench__sigma"
        settings={{
          allowInvalidContainer: true,
          defaultNodeType: "default",
          defaultEdgeType: "curvedNoArrow",
          enableEdgeEvents: true,
          edgeProgramClasses: {
            arrow: EdgeArrowProgram,
            curvedArrow: EdgeCurvedArrowProgram,
            curvedNoArrow: createEdgeCurveProgram()
          },
          nodeProgramClasses: {
            default: NodeBorderProgram,
            circle: NodeCircleProgram,
            point: NodePointProgram
          },
          labelColor: { color: "#172026", attribute: "labelColor" },
          edgeLabelColor: { color: "#172026", attribute: "labelColor" },
          labelDensity: 0.42,
          labelGridCellSize: 60,
          labelRenderedSizeThreshold: 10,
          labelSize: 12,
          edgeLabelSize: 8,
          renderEdgeLabels: false,
          renderLabels: true
        }}
      >
        <GraphLoader graph={graphologyGraph} />
        <GraphAutoLayout graph={graphologyGraph} />
        <GraphReducers />
        <GraphCameraFocus />
        <GraphEvents />
        {children}
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
