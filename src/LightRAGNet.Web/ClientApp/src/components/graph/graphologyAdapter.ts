import { MultiUndirectedGraph } from "graphology";

import type { GraphEdgeDto, GraphNodeDto, GraphViewDto, JsonValue } from "../../types/graph";

export type SigmaGraphAttributes = Record<string, JsonValue | undefined>;

const minNodeSize = 4;
const maxNodeSize = 20;

type GraphologyGraphOptions = {
  minEdgeSize?: number;
  maxEdgeSize?: number;
};

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

function calculateNodeSizes(graph: GraphViewDto): Map<string, number> {
  const degreeByNode = new Map(graph.nodes.map((node) => [node.id, 0]));

  graph.edges.forEach((edge) => {
    if (!degreeByNode.has(edge.source) || !degreeByNode.has(edge.target)) {
      return;
    }

    degreeByNode.set(edge.source, (degreeByNode.get(edge.source) ?? 0) + 1);
    degreeByNode.set(edge.target, (degreeByNode.get(edge.target) ?? 0) + 1);
  });

  const degrees = [...degreeByNode.values()];
  const minDegree = degrees.length > 0 ? Math.min(...degrees) : 0;
  const maxDegree = degrees.length > 0 ? Math.max(...degrees) : 0;
  const range = maxDegree - minDegree;

  return new Map(
    graph.nodes.map((node) => {
      if (range <= 0) {
        return [node.id, Math.max(node.size, 10)];
      }

      const scale = maxNodeSize - minNodeSize;
      const degree = degreeByNode.get(node.id) ?? 0;
      const size = Math.round(minNodeSize + scale * Math.sqrt((degree - minDegree) / range));
      return [node.id, size];
    })
  );
}

function readEdgeWeight(edge: GraphEdgeDto): number {
  const weight = edge.properties?.weight;
  if (typeof weight === "number" && Number.isFinite(weight)) {
    return weight;
  }

  if (typeof weight === "string") {
    const parsed = Number(weight);
    if (Number.isFinite(parsed)) {
      return parsed;
    }
  }

  return 1;
}

function calculateEdgeSizes(edges: GraphEdgeDto[], minEdgeSize: number, maxEdgeSize: number): Map<GraphEdgeDto, number> {
  const weights = edges.map(readEdgeWeight);
  const minWeight = weights.length > 0 ? Math.min(...weights) : 1;
  const maxWeight = weights.length > 0 ? Math.max(...weights) : 1;
  const range = maxWeight - minWeight;

  return new Map(
    edges.map((edge, index) => {
      if (range <= 0) {
        return [edge, minEdgeSize];
      }

      const weight = weights[index] ?? 1;
      const size = minEdgeSize + (maxEdgeSize - minEdgeSize) * Math.sqrt((weight - minWeight) / range);
      return [edge, Math.round(size * 100) / 100];
    })
  );
}

export function createGraphologyGraph(
  graph: GraphViewDto | null,
  selectedNodeId?: string,
  selectedEdgeId?: string,
  options: GraphologyGraphOptions = {}
) {
  const sigmaGraph = new MultiUndirectedGraph<SigmaGraphAttributes, SigmaGraphAttributes>();

  if (!graph) {
    return sigmaGraph;
  }

  const count = graph.nodes.length;
  const edgeMinSize = options.minEdgeSize ?? 1.25;
  const edgeMaxSize = options.maxEdgeSize ?? 5;
  const nodeSizes = calculateNodeSizes(graph);
  const edgeSizes = calculateEdgeSizes(graph.edges, edgeMinSize, edgeMaxSize);

  graph.nodes.forEach((node) => {
    const position = getInitialPosition(node.id, count);
    const isSelected = selectedNodeId === node.id;
    const size = nodeSizes.get(node.id) ?? Math.max(node.size, 10);

    sigmaGraph.addNode(node.id, {
      x: position.x,
      y: position.y,
      label: getNodeLabel(node),
      size: isSelected ? Math.max(size + 4, 13) : size,
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
    const edgeSize = edgeSizes.get(edge) ?? edgeMinSize;
    sigmaGraph.addUndirectedEdgeWithKey(edgeKey, edge.source, edge.target, {
      label: getEdgeLabel(edge),
      size: isSelected ? Math.max(edgeSize + 2, 4) : edgeSize,
      color: isSelected ? "#dc2626" : getEdgeColor(edge),
      type: "curvedNoArrow",
      originalWeight: readEdgeWeight(edge),
      labelColor: "#172026",
      domainType: edge.type ?? undefined,
      properties: edge.properties ?? {}
    });
  });

  return sigmaGraph;
}
