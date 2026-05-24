import { describe, expect, test, vi } from "vitest";

import { getRagQueryData, queryRagStream } from "./ragChatApi";
import type { RagQueryRequest } from "../types/ragChat";

const request: RagQueryRequest = {
  query: "hello",
  mode: "Mix",
  stream: true,
  includeReferences: true,
  responseType: "Multiple Paragraphs",
  topK: 40,
  chunkTopK: 20,
  enableRerank: true,
  highLevelKeywords: [],
  lowLevelKeywords: [],
  onlyNeedContext: false,
  onlyNeedPrompt: false
};

function jsonResponse(body: unknown, init?: ResponseInit): Response {
  return new Response(JSON.stringify(body), {
    headers: { "content-type": "application/json" },
    status: init?.status ?? 200,
    statusText: init?.statusText,
    ...init
  });
}

function textResponse(body: string, init?: ResponseInit): Response {
  return new Response(body, {
    headers: { "content-type": "text/plain" },
    status: init?.status ?? 200,
    statusText: init?.statusText,
    ...init
  });
}

function sseResponse(body: BodyInit): Response {
  return new Response(body, {
    status: 200,
    headers: { "content-type": "text/event-stream" }
  });
}

function sseEvent(event: unknown): string {
  return `data: ${JSON.stringify(event)}\n\n`;
}

function streamFromChunks(chunks: string[]): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder();

  return new ReadableStream({
    start(controller) {
      for (const chunk of chunks) {
        controller.enqueue(encoder.encode(chunk));
      }

      controller.close();
    }
  });
}

describe("ragChatApi", () => {
  test("queryRagStream posts query request and emits chunks and metadata", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      sseResponse(
        [
          sseEvent({ type: "text_chunk", chunk: "hello" }),
          sseEvent({
            type: "metadata",
            mode: "Mix",
            stream: true,
            includeReferences: true,
            responseType: "Multiple Paragraphs",
            cachePolicy: "Streaming request",
            references: [
              {
                referenceId: "1",
                filePath: "/uploads/doc.md",
                fileName: "doc.md",
                previewUrl: "http://localhost/document-preview/1",
                openKind: "DocumentPreview"
              }
            ],
            highLevelKeywords: ["system"],
            lowLevelKeywords: ["queue"],
            diagnostics: { query_mode: "Mix" }
          }),
          sseEvent({ type: "done" })
        ].join("")
      )
    );
    const chunks: string[] = [];
    const metadataModes: string[] = [];

    await queryRagStream("http://localhost/", request, {
      fetchImpl: fetchMock,
      onChunk: (chunk) => chunks.push(chunk),
      onMetadata: (metadata) => metadataModes.push(metadata.mode)
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "http://localhost/api/RagQuery/query",
      expect.objectContaining({
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(request)
      })
    );
    expect(chunks).toEqual(["hello"]);
    expect(metadataModes).toEqual(["Mix"]);
  });

  test("queryRagStream parses split chunks and multiple events", async () => {
    const body = `${sseEvent({ type: "text_chunk", chunk: "hel" })}${sseEvent({ type: "text_chunk", chunk: "lo" })}`;
    const fetchMock = vi.fn().mockResolvedValue(sseResponse(streamFromChunks([body.slice(0, 18), body.slice(18)])));
    const chunks: string[] = [];

    await queryRagStream("", request, {
      fetchImpl: fetchMock,
      onChunk: (chunk) => chunks.push(chunk)
    });

    expect(chunks).toEqual(["hel", "lo"]);
  });

  test("queryRagStream throws error events", async () => {
    const fetchMock = vi.fn().mockResolvedValue(sseResponse(sseEvent({ type: "error", error: "rag_failed", message: "RAG failed" })));

    await expect(queryRagStream("", request, { fetchImpl: fetchMock })).rejects.toThrow("RAG failed");
  });

  test("queryRagStream throws readable HTTP errors with response text", async () => {
    const fetchMock = vi.fn().mockResolvedValue(textResponse("server exploded", { status: 500, statusText: "Internal Server Error" }));

    await expect(queryRagStream("", request, { fetchImpl: fetchMock })).rejects.toThrow(
      "RAG query failed: 500 Internal Server Error: server exploded"
    );
  });

  test("getRagQueryData posts request body and returns JSON", async () => {
    const response = {
      status: "success",
      message: "ok",
      data: { context: "ctx" },
      metadata: { mode: "Mix" }
    };
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(response));

    await expect(getRagQueryData("/api-root/", request, { fetchImpl: fetchMock })).resolves.toEqual(response);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api-root/api/RagQuery/data",
      expect.objectContaining({
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(request)
      })
    );
  });
});
