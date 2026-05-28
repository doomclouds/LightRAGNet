import {
  BrainCircuit,
  Database,
  HardDrive,
  Network,
  ScanSearch,
  Server,
  Workflow,
  TriangleAlert,
  type LucideIcon
} from 'lucide-react';

import type { SystemHealthCheckResult } from '@/api/systemStatusApi';
import { SystemStatusBadge, SystemStatusPanel, SystemStatusTabs } from './SystemStatusPrimitives';
import { formatGeneratedAt } from './systemStatusPresentation';

type SystemStatusEvidenceTableProps = {
  checks: SystemHealthCheckResult[];
  generatedAt: string;
};

export function SystemStatusEvidenceTable({ checks, generatedAt }: SystemStatusEvidenceTableProps) {
  return (
    <SystemStatusPanel className="system-status__checks-surface" ariaLabel="Evidence" actions={<SystemStatusTabs />}>
      {checks.length === 0 ? (
        <p className="system-status__empty">No health checks reported.</p>
      ) : (
        <div className="system-status__table-wrap">
          <table className="system-status__checks-rows" aria-label="Backend measurements">
            <thead>
              <tr>
                <th scope="col">Component</th>
                <th scope="col">Category</th>
                <th scope="col">Status</th>
                <th scope="col">Evidence</th>
                <th scope="col">Last Checked</th>
              </tr>
            </thead>
            <tbody>
              {checks.map((check) => {
                const CheckIcon = getCheckIcon(check);

                return (
                  <tr key={check.id}>
                    <th scope="row">
                      <span className="system-status__check-name">
                        <CheckIcon aria-hidden="true" size={15} />
                        {check.name}
                      </span>
                    </th>
                    <td>{check.category}</td>
                    <td>
                      <SystemStatusBadge status={check.status} />
                    </td>
                    <td>{check.message}</td>
                    <td>{formatGeneratedAt(generatedAt)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </SystemStatusPanel>
  );
}

function getCheckIcon(check: SystemHealthCheckResult): LucideIcon {
  const haystack = `${check.id} ${check.name} ${check.category}`.toLowerCase();

  if (haystack.includes('qdrant') || haystack.includes('vector')) {
    return Database;
  }

  if (haystack.includes('neo4j') || haystack.includes('graph')) {
    return Network;
  }

  if (haystack.includes('rerank') || haystack.includes('rank')) {
    return ScanSearch;
  }

  if (haystack.includes('embedding') || haystack.includes('model') || haystack.includes('ai')) {
    return BrainCircuit;
  }

  if (haystack.includes('worker') || haystack.includes('queue')) {
    return Workflow;
  }

  if (haystack.includes('sqlite') || haystack.includes('storage') || haystack.includes('metadata')) {
    return HardDrive;
  }

  if (check.status !== 'Healthy') {
    return TriangleAlert;
  }

  return Server;
}
