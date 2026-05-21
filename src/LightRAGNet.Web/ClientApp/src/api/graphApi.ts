import type {
  GraphCurationResponse,
  GraphEntityExistsResponse,
  GraphNodeProperties,
  GraphViewDto
} from "../types/graph";

type ErrorLikeResponse = {
  message?: string;
  error?: string;
  title?: string;
};

function buildUrl(apiBase: string, path: string): string {
  const trimmedBase = apiBase.replace(/\/+$/, "");
  return `${trimmedBase}${path}`;
}

async function readJson<T>(response: Response): Promise<T> {
  const text = await response.text();
  const body = text.length > 0 ? (JSON.parse(text) as T & ErrorLikeResponse) : undefined;

  if (!response.ok) {
    const message = body?.message ?? body?.error ?? body?.title ?? response.statusText;
    throw new Error(message || `Request failed with status ${response.status}`);
  }

  return body as T;
}

function jsonRequest(method: "PATCH", body: unknown): RequestInit {
  return {
    method,
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body)
  };
}

export async function queryGraph(
  apiBase: string,
  label: string,
  maxDepth: number,
  maxNodes: number
): Promise<GraphViewDto> {
  const query = new URLSearchParams({
    nodeLabel: label,
    maxDepth: String(maxDepth),
    maxNodes: String(maxNodes)
  });
  const response = await fetch(buildUrl(apiBase, `/api/GraphView?${query.toString()}`), { method: "GET" });
  return readJson<GraphViewDto>(response);
}

export async function getGraphLabels(apiBase: string): Promise<string[]> {
  const response = await fetch(buildUrl(apiBase, "/api/graph/labels"), { method: "GET" });
  return readJson<string[]>(response);
}

export async function checkEntityNameExists(apiBase: string, name: string): Promise<boolean> {
  const query = new URLSearchParams({ name });
  const response = await fetch(buildUrl(apiBase, `/api/graph/entity/exists?${query.toString()}`), { method: "GET" });
  const result = await readJson<GraphEntityExistsResponse>(response);
  return result.exists;
}

export async function editEntity(
  apiBase: string,
  entityName: string,
  updatedData: GraphNodeProperties,
  allowRename: boolean,
  allowMerge: boolean
): Promise<GraphCurationResponse> {
  const encodedName = encodeURIComponent(entityName);
  const response = await fetch(
    buildUrl(apiBase, `/api/graph/entity/${encodedName}`),
    jsonRequest("PATCH", { updatedData, allowRename, allowMerge })
  );
  return readJson<GraphCurationResponse>(response);
}

export async function editRelation(
  apiBase: string,
  sourceEntity: string,
  targetEntity: string,
  updatedData: GraphNodeProperties
): Promise<GraphCurationResponse> {
  const response = await fetch(
    buildUrl(apiBase, "/api/graph/relation"),
    jsonRequest("PATCH", { sourceEntity, targetEntity, updatedData })
  );
  return readJson<GraphCurationResponse>(response);
}
