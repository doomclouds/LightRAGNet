import { describe, expect, test } from "vitest";

import { resolvePropertiesPanelSelection } from "@/components/graph/PropertiesPanel";
import type { GraphEdgeDto, GraphNodeDto } from "@/types/graph";

const node: GraphNodeDto = {
  id: "ALPHA",
  label: "Alpha",
  size: 1,
  color: "#999",
  type: "PERSON",
  properties: { entity_id: "ALPHA", entity_name: "Alpha" }
};

const edge: GraphEdgeDto = {
  id: "ALPHA-BETA",
  source: "ALPHA",
  target: "BETA",
  type: "related",
  size: 1,
  color: "#ccc",
  properties: { description: "knows" }
};

describe("resolvePropertiesPanelSelection", () => {
  test("does not open the properties panel for a selected edge", () => {
    const selection = resolvePropertiesPanelSelection(null, edge, node, null);

    expect(selection.target).toBeNull();
    expect(selection.currentNode).toBeNull();
    expect(selection.currentEdge).toBeNull();
    expect(selection.hasPinnedSelection).toBe(false);
  });

  test("keeps node selection available even when an edge is focused", () => {
    const selection = resolvePropertiesPanelSelection(node, null, null, edge);

    expect(selection.target).toBe("node");
    expect(selection.currentNode?.id).toBe("ALPHA");
    expect(selection.currentEdge).toBeNull();
    expect(selection.hasPinnedSelection).toBe(true);
  });

  test("does not open the properties panel for a focused edge", () => {
    const selection = resolvePropertiesPanelSelection(null, null, null, edge);

    expect(selection.target).toBeNull();
    expect(selection.currentNode).toBeNull();
    expect(selection.currentEdge).toBeNull();
    expect(selection.hasPinnedSelection).toBe(false);
  });
});
