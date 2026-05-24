import type { SystemHealthResponse, SystemHealthStatus } from "../api/systemStatusApi";

type SystemStatusSummaryProps = {
  health: SystemHealthResponse;
};

export function statusClass(status: SystemHealthStatus): string {
  return `system-status__status system-status__status--${status.toLowerCase()}`;
}

export function SystemStatusSummary({ health }: SystemStatusSummaryProps) {
  return (
    <section className="system-status__panel system-status__summary" aria-label="System summary">
      <div className="system-status__panel-heading">
        <p className="system-status__eyebrow">Current status</p>
        <span className={statusClass(health.status)}>{health.status}</span>
      </div>
      <dl className="system-status__summary-counts">
        <div>
          <dt>Healthy</dt>
          <dd>{health.summary.healthy}</dd>
        </div>
        <div>
          <dt>Degraded</dt>
          <dd>{health.summary.degraded}</dd>
        </div>
        <div>
          <dt>Unhealthy</dt>
          <dd>{health.summary.unhealthy}</dd>
        </div>
        <div>
          <dt>Not measured</dt>
          <dd>{health.summary.notMeasured}</dd>
        </div>
      </dl>
      <dl className="system-status__meta">
        <div>
          <dt>Generated</dt>
          <dd>{health.generatedAt}</dd>
        </div>
        <div>
          <dt>Duration</dt>
          <dd>{health.durationMs} ms</dd>
        </div>
      </dl>
    </section>
  );
}
