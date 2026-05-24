import type { QueryMetadataEvent, RagQueryDataResponse, RagQueryEvent, RagQueryRequest } from "../types/ragChat";

type ErrorLikeResponse = {
  message?: string;
  error?: string;
  title?: string;
};

export type RagStreamHandlers = {
  fetchImpl?: typeof fetch;
  signal?: AbortSignal;
  onChunk?: (chunk: string) => void;
  onMetadata?: (metadata: QueryMetadataEvent) => void;
};

export type RagQueryDataOptions = {
  fetchImpl?: typeof fetch;
  signal?: AbortSignal;
};

function buildUrl(apiBase: string, path: string): string {
  const trimmedBase = apiBase.replace(/\/+$/, "");
  return `${trimmedBase}${path}`;
}

export async function queryRagStream(
  apiBase: string,
  request: RagQueryRequest,
  handlers: RagStreamHandlers = {}
): Promise<void> {
  const fetchImpl = handlers.fetchImpl ?? fetch;
  const response = await fetchImpl(buildUrl(apiBase, "/api/RagQuery/query"), {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(request),
    signal: handlers.signal
  });

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, "RAG query failed"));
  }

  if (!response.body) {
    throw new Error("RAG query response body is empty.");
  }

  await readSseStream(response.body, handlers);
}

export async function getRagQueryData(
  apiBase: string,
  request: RagQueryRequest,
  options: RagQueryDataOptions = {}
): Promise<RagQueryDataResponse> {
  const fetchImpl = options.fetchImpl ?? fetch;
  const response = await fetchImpl(buildUrl(apiBase, "/api/RagQuery/data"), {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(request),
    signal: options.signal
  });

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, "RAG query data failed"));
  }

  return (await response.json()) as RagQueryDataResponse;
}

async function readSseStream(body: ReadableStream<Uint8Array>, handlers: RagStreamHandlers): Promise<void> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { done, value } = await reader.read();

    if (done) {
      buffer += decoder.decode();
      break;
    }

    buffer += decoder.decode(value, { stream: true });
    buffer = dispatchCompleteSseParts(buffer, handlers);
  }

  dispatchCompleteSseParts(`${buffer}\n\n`, handlers);
}

function dispatchCompleteSseParts(buffer: string, handlers: RagStreamHandlers): string {
  const normalized = buffer.replace(/\r\n/g, "\n");
  const parts = normalized.split("\n\n");
  const remainder = parts.pop() ?? "";

  for (const part of parts) {
    const event = parseSsePart(part);

    if (!event) {
      continue;
    }

    dispatchRagEvent(event, handlers);
  }

  return remainder;
}

function dispatchRagEvent(event: RagQueryEvent, handlers: RagStreamHandlers): void {
  if (event.type === "text_chunk") {
    handlers.onChunk?.(event.chunk);
    return;
  }

  if (event.type === "metadata") {
    handlers.onMetadata?.(event);
    return;
  }

  if (event.type === "error") {
    throw new Error(event.message || event.error || "RAG query stream failed.");
  }
}

function parseSsePart(part: string): RagQueryEvent | null {
  const dataLines = part
    .split("\n")
    .map((line) => line.trimEnd())
    .filter((line) => line.startsWith("data:"))
    .map((line) => line.slice("data:".length).trimStart());

  if (dataLines.length === 0) {
    return null;
  }

  try {
    return JSON.parse(dataLines.join("\n")) as RagQueryEvent;
  } catch (error) {
    throw new Error(`Invalid RAG query stream event: ${error instanceof Error ? error.message : "unknown error"}`);
  }
}

async function readErrorMessage(response: Response, prefix: string): Promise<string> {
  const statusMessage = response.statusText || `Request failed with status ${response.status}`;
  const fallback = `${prefix}: ${response.status} ${statusMessage}`;
  const text = await response.text();

  if (text.trim().length === 0) {
    return fallback;
  }

  try {
    const body = JSON.parse(text) as ErrorLikeResponse;
    return body.message ?? body.error ?? body.title ?? fallback;
  } catch {
    return `${fallback}: ${text.trim()}`;
  }
}
