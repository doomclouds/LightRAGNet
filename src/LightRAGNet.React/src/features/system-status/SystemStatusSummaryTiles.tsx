import { CheckCircle2, Clock3, Database, GitFork, Server } from 'lucide-react';

import type { SystemHealthResponse } from '@/api/systemStatusApi';
import { SystemStatusTile } from './SystemStatusPrimitives';
import { formatDurationMs, formatGeneratedAt } from './systemStatusPresentation';

type SystemStatusSummaryTilesProps = {
  health: SystemHealthResponse;
};

export function SystemStatusSummaryTiles({ health }: SystemStatusSummaryTilesProps) {
  const totalChecks = health.checks.length;
  const vectorStore = findCheckValue(health, ['qdrant', 'vector'], 'Qdrant');
  const graphStore = findCheckValue(health, ['neo4j', 'graph'], 'Neo4j');

  return (
    <section className="system-status__status-strip" aria-label="System summary">
      <SystemStatusTile
        icon={CheckCircle2}
        label="Overall Health"
        value={health.status}
        note={getOverallNote(health)}
        tone={health.status === 'Healthy' ? 'healthy' : 'warning'}
      />
      <SystemStatusTile
        icon={Server}
        label="Services"
        value={`${totalChecks} / ${totalChecks}`}
        note={`${health.summary.healthy} healthy, ${health.summary.degraded + health.summary.unhealthy + health.summary.notMeasured} needs attention`}
      />
      <SystemStatusTile icon={Database} label="Vector Store" value={vectorStore.value} note={vectorStore.note} />
      <SystemStatusTile icon={GitFork} label="Graph Store" value={graphStore.value} note={graphStore.note} />
      <SystemStatusTile
        icon={Clock3}
        label="Last Checked"
        value={formatGeneratedAt(health.generatedAt)}
        note={`Probe duration: ${formatDurationMs(health.durationMs)}`}
        tone="warning"
      />
    </section>
  );
}

function getOverallNote(health: SystemHealthResponse): string {
  if (health.status === 'Healthy') {
    return 'All critical systems operational';
  }

  if (health.status === 'Unhealthy') {
    return 'Critical systems need attention';
  }

  return 'Some systems need attention';
}

function findCheckValue(health: SystemHealthResponse, keywords: string[], fallback: string): { value: string; note: string } {
  const check = health.checks.find((item) => {
    const haystack = `${item.id} ${item.name} ${item.category}`.toLowerCase();
    return keywords.some((keyword) => haystack.includes(keyword));
  });

  if (!check) {
    return { value: fallback, note: 'Not reported' };
  }

  return { value: fallback, note: check.status === 'Healthy' ? 'Connected' : check.message };
}
