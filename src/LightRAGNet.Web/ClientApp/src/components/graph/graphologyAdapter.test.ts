import { describe, expect, test } from "vitest";

import { createGraphologyGraph } from "./graphologyAdapter";
import type { GraphViewDto } from "../../types/graph";

describe("createGraphologyGraph", () => {
  test("keeps domain types away from Sigma renderer type attributes", () => {
    const graph: GraphViewDto = {
      nodes: [
        {
          id: "CONCEPT",
          label: "Concept",
          size: 3,
          color: "#999999",
          type: "concept",
          properties: { entity_id: "CONCEPT" }
        }
      ],
      edges: [
        {
          id: "self",
          source: "CONCEPT",
          target: "CONCEPT",
          type: "related",
          size: 1,
          color: "#cccccc",
          properties: { description: "self relation" }
        }
      ],
      isTruncated: false
    };

    const sigmaGraph = createGraphologyGraph(graph);

    expect(sigmaGraph.getNodeAttribute("CONCEPT", "type")).toBeUndefined();
    expect(sigmaGraph.getNodeAttribute("CONCEPT", "domainType")).toBe("concept");
    expect(sigmaGraph.getEdgeAttribute("self", "type")).toBeUndefined();
    expect(sigmaGraph.getEdgeAttribute("self", "domainType")).toBe("related");
  });
});
