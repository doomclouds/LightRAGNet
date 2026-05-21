import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

import {
  checkEntityNameExists,
  editEntity,
  editRelation,
  getGraphLabels,
  queryGraph
} from "./graphApi";

const graphResponse = {
  nodes: [
    {
      id: "ALPHA",
      label: "ALPHA",
      size: 2,
      color: "#999",
      type: "PERSON",
      properties: { description: "seed" }
    }
  ],
  edges: [],
  isTruncated: false
};

function jsonResponse(body: unknown, init?: ResponseInit): Response {
  return new Response(JSON.stringify(body), {
    headers: { "content-type": "application/json" },
    status: init?.status ?? 200,
    statusText: init?.statusText,
    ...init
  });
}

describe("graphApi", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  test("queryGraph calls GraphView with encoded query parameters", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse(graphResponse));

    const result = await queryGraph("/base/", "Entity / ? #", 3, 25);

    expect(result).toEqual(graphResponse);
    expect(fetch).toHaveBeenCalledWith(
      "/base/api/GraphView?nodeLabel=Entity+%2F+%3F+%23&maxDepth=3&maxNodes=25",
      expect.objectContaining({ method: "GET" })
    );
  });

  test("getGraphLabels and checkEntityNameExists read camelCase responses", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse(["ALPHA", "BETA"]))
      .mockResolvedValueOnce(jsonResponse({ exists: true }));

    await expect(getGraphLabels("")).resolves.toEqual(["ALPHA", "BETA"]);
    await expect(checkEntityNameExists("", "ALPHA/BETA")).resolves.toBe(true);

    expect(fetch).toHaveBeenNthCalledWith(
      1,
      "/api/graph/labels",
      expect.objectContaining({ method: "GET" })
    );
    expect(fetch).toHaveBeenNthCalledWith(
      2,
      "/api/graph/entity/exists?name=ALPHA%2FBETA",
      expect.objectContaining({ method: "GET" })
    );
  });

  test("editEntity encodes path names and sends curation body", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(
      jsonResponse({
        succeeded: true,
        status: "ok",
        message: "Entity edited.",
        data: { entity_name: "Beta" },
        operationSummary: null,
        failureStage: null
      })
    );

    const result = await editEntity(
      "/api-root",
      "Alpha/Beta?#",
      { entity_name: "Beta", description: "updated" },
      false,
      true
    );

    expect(result.succeeded).toBe(true);
    expect(fetch).toHaveBeenCalledWith(
      "/api-root/api/graph/entity/Alpha%2FBeta%3F%23",
      expect.objectContaining({
        method: "PATCH",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          updatedData: { entity_name: "Beta", description: "updated" },
          allowRename: false,
          allowMerge: true
        })
      })
    );
  });

  test("editRelation sends source, target, and updatedData in the body", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(
      jsonResponse({
        succeeded: true,
        status: "ok",
        message: "Relation edited.",
        data: null,
        operationSummary: null,
        failureStage: null
      })
    );

    await editRelation("", "ALPHA", "BETA", { description: "knows" });

    expect(fetch).toHaveBeenCalledWith(
      "/api/graph/relation",
      expect.objectContaining({
        method: "PATCH",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          sourceEntity: "ALPHA",
          targetEntity: "BETA",
          updatedData: { description: "knows" }
        })
      })
    );
  });

  test("readJson throws server message on non-ok responses", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(
      jsonResponse(
        {
          succeeded: false,
          status: "validation_error",
          message: "Entity name is required."
        },
        { status: 400, statusText: "Bad Request" }
      )
    );

    await expect(checkEntityNameExists("", "")).rejects.toThrow("Entity name is required.");
  });
});
