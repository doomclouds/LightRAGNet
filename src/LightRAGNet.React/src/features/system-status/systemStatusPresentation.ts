import type { SystemHealthResponse, SystemHealthStatus } from '@/api/systemStatusApi';

export type SystemStatusTone = 'neutral' | 'success' | 'warning' | 'danger';

const statusTones: Record<SystemHealthStatus, SystemStatusTone> = {
  Healthy: 'success',
  Degraded: 'warning',
  Unhealthy: 'danger',
  NotMeasured: 'neutral'
};

const statusIconNames: Record<SystemHealthStatus, string> = {
  Healthy: 'CircleCheck',
  Degraded: 'TriangleAlert',
  Unhealthy: 'CircleX',
  NotMeasured: 'CircleDashed'
};

export function getStatusTone(status: SystemHealthStatus): SystemStatusTone {
  return statusTones[status];
}

export function getStatusIconName(status: SystemHealthStatus): string {
  return statusIconNames[status];
}

export function formatDurationMs(value: number | null | undefined): string {
  if (value == null) {
    return 'Not measured';
  }

  if (value < 1000) {
    return `${Math.round(value)} ms`;
  }

  return `${(value / 1000).toFixed(1)} s`;
}

export function formatGeneratedAt(value: string): string {
  const date = new Date(value);

  if (!Number.isFinite(date.getTime())) {
    return 'Unknown';
  }

  const year = date.getFullYear();
  const month = padDatePart(date.getMonth() + 1);
  const day = padDatePart(date.getDate());
  const hours = padDatePart(date.getHours());
  const minutes = padDatePart(date.getMinutes());
  const seconds = padDatePart(date.getSeconds());

  return `${year}-${month}-${day} ${hours}:${minutes}:${seconds}`;
}

export function summarizeEvidence(evidence: Record<string, unknown> | null | undefined): string {
  if (evidence == null) {
    return 'No evidence';
  }

  const entries = Object.entries(evidence);

  if (entries.length === 0) {
    return 'No evidence';
  }

  return truncateText(
    entries.map(([key, value]) => `${key}=${formatEvidenceValue(value)}`).join(', '),
    180
  );
}

export function formatHealthJson(response: SystemHealthResponse): string {
  return JSON.stringify(response, null, 2);
}

function padDatePart(value: number): string {
  return value.toString().padStart(2, '0');
}

export function formatEvidenceValue(value: unknown): string {
  if (value == null) {
    return 'null';
  }

  if (typeof value === 'string') {
    return value;
  }

  if (typeof value === 'number' || typeof value === 'boolean' || typeof value === 'bigint') {
    return value.toString();
  }

  return truncateText(stringifyEvidenceValue(value), 80);
}

function truncateText(value: string, maxLength: number): string {
  if (value.length <= maxLength) {
    return value;
  }

  return `${value.slice(0, Math.max(0, maxLength - 3))}...`;
}

function stringifyEvidenceValue(value: unknown): string {
  const seen = new WeakSet<object>();
  const text = JSON.stringify(value, (_key, nestedValue: unknown) => {
    if (nestedValue !== null && typeof nestedValue === 'object') {
      if (seen.has(nestedValue)) {
        return '[Circular]';
      }

      seen.add(nestedValue);
    }

    return nestedValue;
  });

  return text ?? String(value);
}
