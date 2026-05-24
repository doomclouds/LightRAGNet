import type { CacheClearResponse, CacheOverviewResponse } from "@/types/cacheManagement";

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
  const statusMessage = response.statusText || `Request failed with status ${response.status}`;

  if (text.trim().length === 0) {
    if (!response.ok) {
      throw new Error(statusMessage);
    }

    throw new Error("Empty cache management response");
  }

  let body: T & ErrorLikeResponse;

  try {
    body = JSON.parse(text) as T & ErrorLikeResponse;
  } catch {
    if (!response.ok) {
      throw new Error(statusMessage);
    }

    throw new Error("Invalid cache management response");
  }

  if (!response.ok) {
    const message = body.message ?? body.error ?? body.title ?? statusMessage;
    throw new Error(message);
  }

  return body;
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
