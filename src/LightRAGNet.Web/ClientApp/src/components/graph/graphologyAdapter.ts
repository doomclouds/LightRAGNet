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

function hashString(value: string): number {
  let hash = 2166136261;

  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }

  return hash >>> 0;
}

function seededUnit(hash: number, salt: number): number {
  let value = hash + Math.imul(salt, 0x9e3779b9);
  value ^= value >>> 16;
  value = Math.imul(value, 0x7feb352d);
  value ^= value >>> 15;
  value = Math.imul(value, 0x846ca68b);
  value ^= value >>> 16;
  return (value >>> 0) / 4294967295;
}

function getInitialPosition(nodeId: string, count: number) {
  const hash = hashString(nodeId);
  const spread = Math.max(2, Math.sqrt(Math.max(count, 1)) * 3);

  return {
    x: (seededUnit(hash, 1) - 0.5) * spread,
    y: (seededUnit(hash, 2) - 0.5) * spread
  };
}

export function getGraphEdgeKey(edge: GraphEdgeDto, index: number): string {
  return edge.id.trim().length > 0 ? edge.id : `${edge.source}->${edge.target}:${index}`;
}

function getEdgeColor(edge: GraphEdgeDto): string {
  return edge.color.toLowerCase() === "#cccccc" ? "#8a98a8" : edge.color;
}

export function createGraphologyGraph(graph: GraphViewDto | null, selectedNodeId?: string, selectedEdgeId?: string) {
  const sigmaGraph = new MultiUndirectedGraph<SigmaGraphAttributes, SigmaGraphAttributes>();

  if (!graph) {
    return sigmaGraph;
  }

  const count = graph.nodes.length;

  graph.nodes.forEach((node) => {
    const position = getInitialPosition(node.id, count);
    const isSelected = selectedNodeId === node.id;

    sigmaGraph.addNode(node.id, {
      x: position.x,
      y: position.y,
      label: getNodeLabel(node),
      size: isSelected ? Math.max(node.size + 4, 13) : Math.max(node.size, 8),
      color: isSelected ? "#0f766e" : node.color,
      borderColor: isSelected ? "#0f172a" : "#ffffff",
      labelColor: "#172026",
      domainType: node.type ?? undefined,
      properties: node.properties
    });
  });

  graph.edges.forEach((edge, index) => {
    const edgeKey = getGraphEdgeKey(edge, index);

    if (!sigmaGraph.hasNode(edge.source) || !sigmaGraph.hasNode(edge.target) || sigmaGraph.hasEdge(edgeKey)) {
      return;
    }

    const isSelected = selectedEdgeId === edgeKey || selectedEdgeId === edge.id;
    sigmaGraph.addUndirectedEdgeWithKey(edgeKey, edge.source, edge.target, {
      label: getEdgeLabel(edge),
      size: isSelected ? Math.max(edge.size + 2, 4) : Math.max(edge.size, 1.75),
      color: isSelected ? "#dc2626" : getEdgeColor(edge),
      type: "curvedNoArrow",
      originalWeight: typeof edge.properties?.weight === "number" ? edge.properties.weight : edge.size,
      labelColor: "#172026",
      domainType: edge.type ?? undefined,
      properties: edge.properties ?? {}
    });
  });

  return sigmaGraph;
}
