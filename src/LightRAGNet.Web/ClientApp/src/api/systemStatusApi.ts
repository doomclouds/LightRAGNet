export type SystemHealthStatus = "Healthy" | "Degraded" | "Unhealthy" | "NotMeasured";

export type SystemHealthSummary = {
  healthy: number;
  degraded: number;
  unhealthy: number;
  notMeasured: number;
};

export type SystemHealthCheckResult = {
  id: string;
  name: string;
  category: string;
  status: SystemHealthStatus;
  message: string;
  evidence: Record<string, unknown>;
  remediation: string;
  affects: string[];
  durationMs: number;
};

export type SystemHealthFixFirstItem = {
  checkId: string;
  title: string;
  status: SystemHealthStatus;
  remediation: string;
  affects: string[];
};

export type SystemHealthLink = {
  label: string;
  href: string;
};

export type SystemHealthFeatureImpact = {
  feature: string;
  status: SystemHealthStatus;
  reason: string;
  affectedBy: string[];
  links: SystemHealthLink[];
};

export type SystemHealthResponse = {
  status: SystemHealthStatus;
  generatedAt: string;
  durationMs: number;
  summary: SystemHealthSummary;
  checks: SystemHealthCheckResult[];
  fixFirst: SystemHealthFixFirstItem[];
  featureImpacts: SystemHealthFeatureImpact[];
};

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
  let body: (T & ErrorLikeResponse) | undefined;

  if (text.length > 0) {
    try {
      body = JSON.parse(text) as T & ErrorLikeResponse;
    } catch {
      body = undefined;
    }
  }

  if (!response.ok) {
    const message = body?.message ?? body?.error ?? body?.title ?? response.statusText;
    throw new Error(message || `Request failed with status ${response.status}`);
  }

  return body as T;
}

export async function getSystemHealth(apiBase: string): Promise<SystemHealthResponse> {
  const response = await fetch(buildUrl(apiBase, "/api/system/health"), { method: "GET" });
  return readJson<SystemHealthResponse>(response);
}
