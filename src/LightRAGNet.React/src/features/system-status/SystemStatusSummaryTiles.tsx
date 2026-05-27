import {
  Activity,
  CircleCheck,
  CircleDashed,
  CircleX,
  Timer,
  TriangleAlert,
  type LucideIcon
} from 'lucide-react';

import type { SystemHealthResponse } from '@/api/systemStatusApi';
import { Panel } from '@/shared/components/Panel';
import { StatusPill } from '@/shared/components/StatusPill';
import { formatDurationMs, formatGeneratedAt, getStatusTone } from './systemStatusPresentation';

type SystemStatusSummaryTilesProps = {
  health: SystemHealthResponse;
};

type SummaryTile = {
  label: string;
  value: string | number;
  icon: LucideIcon;
};

export function SystemStatusSummaryTiles({ health }: SystemStatusSummaryTilesProps) {
  const summaryTiles: SummaryTile[] = [
    { label: 'Healthy', value: health.summary.healthy, icon: CircleCheck },
    { label: 'Degraded', value: health.summary.degraded, icon: TriangleAlert },
    { label: 'Unhealthy', value: health.summary.unhealthy, icon: CircleX },
    { label: 'Not measured', value: health.summary.notMeasured, icon: CircleDashed }
  ];

  return (
    <Panel as="section" className="system-status__summary-tiles" aria-label="System summary">
      <div className="system-status__summary-hero">
        <Activity aria-hidden="true" size={18} />
        <div>
          <p className="system-status__eyebrow">Overall health</p>
          <StatusPill tone={getStatusTone(health.status)}>{health.status}</StatusPill>
        </div>
      </div>

      <dl className="system-status__summary-counts">
        {summaryTiles.map((tile) => {
          const Icon = tile.icon;

          return (
            <div className="system-status__summary-metric" key={tile.label}>
              <dt>
                <Icon aria-hidden="true" size={16} />
                {tile.label}
              </dt>
              <dd>{tile.value}</dd>
            </div>
          );
        })}
      </dl>

      <dl className="system-status__meta">
        <div>
          <dt>Generated</dt>
          <dd>{formatGeneratedAt(health.generatedAt)}</dd>
        </div>
        <div>
          <dt>Duration</dt>
          <dd>
            <Timer aria-hidden="true" size={14} />
            {formatDurationMs(health.durationMs)}
          </dd>
        </div>
      </dl>
    </Panel>
  );
}
