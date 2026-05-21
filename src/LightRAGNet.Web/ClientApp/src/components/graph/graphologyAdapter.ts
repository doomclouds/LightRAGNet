import { MultiUndirectedGraph } from "graphology";

import type { GraphEdgeDto, GraphNodeDto, GraphViewDto, JsonValue } from "../../types/graph";

export type SigmaGraphAttributes = Record<string, JsonValue | undefined>;

function readString(value: JsonValue | undefined): string | null {
  return typeof value === "string" && value.trim().length > 0 ? value : null;
}

function getNodeLabel(node: GraphNodeDto): string {
  return readString(node.properties.entity_id) ?? readString(node.properties.entity_name) ?? node.label ?? node.id;
}

function getEdgeLabel(edge: GraphEdgeDto): string {
  return readString(edge.properties?.description) ?? edge.type ?? `${edge.source} - ${edge.target}`;
}

export function createGraphologyGraph(graph: GraphViewDto | null, selectedNodeId?: string, selectedEdgeId?: string) {
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
      domainType: node.type ?? undefined,
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
      domainType: edge.type ?? undefined,
      properties: edge.properties ?? {}
    });
  });

  return sigmaGraph;
}
