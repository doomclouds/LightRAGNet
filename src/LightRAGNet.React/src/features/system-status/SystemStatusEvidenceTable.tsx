import {
  CircleCheck,
  CircleDashed,
  CircleX,
  ClipboardList,
  TriangleAlert,
  type LucideIcon
} from 'lucide-react';

import type { SystemHealthCheckResult, SystemHealthStatus } from '@/api/systemStatusApi';
import { DataTableSurface } from '@/shared/components/DataTable';
import { StatusPill } from '@/shared/components/StatusPill';
import { formatDurationMs, formatEvidenceValue, getStatusTone } from './systemStatusPresentation';

type SystemStatusEvidenceTableProps = {
  checks: SystemHealthCheckResult[];
};

const statusIcons: Record<SystemHealthStatus, LucideIcon> = {
  Healthy: CircleCheck,
  Degraded: TriangleAlert,
  Unhealthy: CircleX,
  NotMeasured: CircleDashed
};

export function SystemStatusEvidenceTable({ checks }: SystemStatusEvidenceTableProps) {
  return (
    <section className="system-status__checks-surface" aria-label="Health checks">
      <div className="system-status__section-heading">
        <ClipboardList aria-hidden="true" size={18} />
        <h2>Health checks</h2>
      </div>

      {checks.length === 0 ? (
        <p className="system-status__empty">No health checks reported.</p>
      ) : (
        <DataTableSurface className="system-status__checks-grid">
          <table className="lrn-data-table system-status__checks-rows" aria-label="Backend measurements">
            <thead>
              <tr>
                <th scope="col">Check</th>
                <th scope="col">Category</th>
                <th scope="col">Status</th>
                <th scope="col">Message</th>
                <th scope="col">Duration</th>
                <th scope="col">Evidence</th>
                <th scope="col">Remediation</th>
              </tr>
            </thead>
            <tbody>
              {checks.map((check) => {
                const StatusIcon = statusIcons[check.status];

                return (
                  <tr key={check.id}>
                    <th scope="row">
                      <span className="system-status__check-name">
                        <StatusIcon aria-hidden="true" size={15} />
                        {check.name}
                      </span>
                    </th>
                    <td>{check.category}</td>
                    <td>
                      <StatusPill tone={getStatusTone(check.status)}>{check.status}</StatusPill>
                    </td>
                    <td>{check.message}</td>
                    <td>{formatDurationMs(check.durationMs)}</td>
                    <td>
                      <EvidenceSummary evidence={check.evidence} />
                    </td>
                    <td>{check.remediation || 'None'}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </DataTableSurface>
      )}
    </section>
  );
}

function EvidenceSummary({ evidence }: { evidence: Record<string, unknown> | null | undefined }) {
  const entries = evidence == null ? [] : Object.entries(evidence);

  if (entries.length === 0) {
    return <>No evidence</>;
  }

  return (
    <dl className="system-status__evidence-summary">
      {entries.map(([key, value]) => (
        <div key={key}>
          <dt>{key}</dt>
          <dd>{formatEvidenceValue(value)}</dd>
        </div>
      ))}
    </dl>
  );
}
