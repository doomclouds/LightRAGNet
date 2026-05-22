import { SigmaContainer, useLoadGraph, useRegisterEvents, useSetSettings, useSigma } from "@react-sigma/core";
import "@react-sigma/core/lib/style.css";
import { useLayoutForceAtlas2 } from "@react-sigma/layout-forceatlas2";
import { createEdgeCurveProgram, EdgeCurvedArrowProgram } from "@sigma/edge-curve";
import { NodeBorderProgram } from "@sigma/node-border";
import type { MultiUndirectedGraph } from "graphology";
import type { ReactNode } from "react";
import { useEffect, useMemo, useState } from "react";
import { EdgeArrowProgram } from "sigma/rendering";

import { useGraphSettingsStore } from "../../stores/graphSettingsStore";
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

const CurvedNoArrowProgram = createEdgeCurveProgram();
const NodeProgramClasses = {
  border: NodeBorderProgram
};
const EdgeProgramClasses = {
  arrow: EdgeArrowProgram,
  curvedArrow: EdgeCurvedArrowProgram,
  curvedNoArrow: CurvedNoArrowProgram
};
const LabelColor = { color: "#172026", attribute: "labelColor" };
const EdgeLabelColor = { color: "#172026", attribute: "labelColor" };

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
      },
      mousedown: (event: { original: MouseEvent | TouchEvent }) => {
        const original = event.original;
        if ("buttons" in original && original.buttons !== 0 && !sigma.getCustomBBox()) {
          sigma.setCustomBBox(sigma.getBBox());
        }
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
  const layoutIterations = useGraphSettingsStore((state) => state.layoutIterations);
  const { assign } = useLayoutForceAtlas2({
    iterations: layoutIterations,
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
  const settings = useGraphSettingsStore();
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
      renderEdgeLabels: settings.showEdgeLabels,
      renderLabels: settings.showNodeLabels,
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
          if (settings.hideUnselectedEdges) {
            nextData.hidden = true;
          } else {
            nextData.color = "#d1d9d5";
            nextData.size = Math.max(Number(data.size ?? 1.75) * 0.65, 0.8);
          }
        }

        if (selectedEdgeKey && edge !== selectedEdgeKey) {
          if (settings.hideUnselectedEdges) {
            nextData.hidden = true;
          } else {
            nextData.color = "#d1d9d5";
          }
        }

        return nextData;
      }
    });
  }, [focusedEdgeKey, focusedNodeId, selectedEdgeKey, selectedNodeId, setSettings, settings, sigma]);

  return null;
}

function GraphCameraFocus() {
  const sigma = useSigma();
  const selectedNodeId = useGraphStore((state) => state.selectedNode?.id ?? null);
  const moveToSelectedNode = useGraphStore((state) => state.moveToSelectedNode);

  useEffect(() => {
    if (!moveToSelectedNode) {
      return;
    }

    if (!selectedNodeId) {
      sigma.setCustomBBox(null);
      useGraphStore.setMoveToSelectedNode(false);
      return;
    }

    if (!sigma.getGraph().hasNode(selectedNodeId)) {
      useGraphStore.setMoveToSelectedNode(false);
      return;
    }

    const frame = window.requestAnimationFrame(() => {
      const nodeDisplayData = sigma.getNodeDisplayData(selectedNodeId);
      if (nodeDisplayData) {
        sigma.setCustomBBox(null);
        sigma.getCamera().animate({ x: nodeDisplayData.x, y: nodeDisplayData.y }, { duration: 420 });
      }

      useGraphStore.setMoveToSelectedNode(false);
    });

    return () => window.cancelAnimationFrame(frame);
  }, [moveToSelectedNode, selectedNodeId, sigma]);

  return null;
}

export function GraphCanvas({ graph, isFetching, errorMessage, children }: GraphCanvasProps) {
  const settings = useGraphSettingsStore();
  const sigmaSettings = useMemo(
    () => ({
      allowInvalidContainer: true,
      defaultNodeType: "border",
      defaultEdgeType: "curvedNoArrow",
      enableEdgeEvents: settings.enableEdgeEvents,
      edgeProgramClasses: EdgeProgramClasses,
      nodeProgramClasses: NodeProgramClasses,
      labelColor: LabelColor,
      edgeLabelColor: EdgeLabelColor,
      labelDensity: 0.42,
      labelGridCellSize: 60,
      labelRenderedSizeThreshold: 10,
      labelSize: 12,
      edgeLabelSize: 8
    }),
    [settings.enableEdgeEvents]
  );
  const graphologyGraph = useMemo(
    () =>
      createGraphologyGraph(graph, undefined, undefined, {
        minEdgeSize: settings.minEdgeSize,
        maxEdgeSize: settings.maxEdgeSize
      }),
    [graph, settings.maxEdgeSize, settings.minEdgeSize]
  );
  const isEmpty = !graph || graph.nodes.length === 0;

  return (
    <section className="graph-workbench__canvas" aria-label="Graph canvas">
      <SigmaContainer
        className="graph-workbench__sigma"
        settings={sigmaSettings}
      >
        <GraphReducers />
        <GraphLoader graph={graphologyGraph} />
        <GraphAutoLayout graph={graphologyGraph} />
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
