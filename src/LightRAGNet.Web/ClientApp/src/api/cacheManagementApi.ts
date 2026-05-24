import type { CacheClearResponse, CacheOverviewResponse } from "../types/cacheManagement";

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

export async function getCacheManagementOverview(
  apiBase: string,
  workspace: string,
  window: string
): Promise<CacheOverviewResponse> {
  const query = new URLSearchParams({
    workspace,
    window
  });
  const response = await fetch(buildUrl(apiBase, `/api/cache-management/overview?${query.toString()}`), {
    method: "GET"
  });
  return readJson<CacheOverviewResponse>(response);
}

export async function clearCachePlan(
  apiBase: string,
  workspace: string,
  planId: string,
  confirm: boolean
): Promise<CacheClearResponse> {
  const response = await fetch(buildUrl(apiBase, "/api/cache-management/clear"), {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      workspace,
      planId,
      confirm
    })
  });
  const body = await readJson<CacheClearResponse>(response);

  if (!body.succeeded) {
    throw new Error(body.message || `Cache clear plan failed: ${planId}`);
  }

  return body;
}
