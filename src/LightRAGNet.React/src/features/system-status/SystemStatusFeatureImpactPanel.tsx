import { ExternalLink, RadioTower } from 'lucide-react';

import type { SystemHealthFeatureImpact } from '@/api/systemStatusApi';
import { Panel } from '@/shared/components/Panel';
import { StatusPill } from '@/shared/components/StatusPill';
import { getStatusTone } from './systemStatusPresentation';

type SystemStatusFeatureImpactPanelProps = {
  items: SystemHealthFeatureImpact[];
};

export function SystemStatusFeatureImpactPanel({ items }: SystemStatusFeatureImpactPanelProps) {
  return (
    <Panel as="section" className="system-status__feature-impact-list" aria-label="Feature impact">
      <div className="system-status__section-heading">
        <RadioTower aria-hidden="true" size={18} />
        <h2>Feature impact</h2>
      </div>

      {items.length === 0 ? (
        <p className="system-status__empty">No feature impacts reported.</p>
      ) : (
        <div className="system-status__impact-list">
          {items.map((item) => (
            <article className="system-status__impact" key={getImpactKey(item)}>
              <div className="system-status__impact-header">
                <h3>{item.feature}</h3>
                <StatusPill tone={getStatusTone(item.status)}>{item.status}</StatusPill>
              </div>
              <p>{item.reason}</p>
              <dl className="system-status__meta">
                <div>
                  <dt>Affected by</dt>
                  <dd>{formatList(item.affectedBy)}</dd>
                </div>
              </dl>
              {item.links.length > 0 ? (
                <div className="system-status__links">
                  {item.links.map((link) => (
                    <a href={link.href} key={`${item.feature}-${link.label}-${link.href}`}>
                      {link.label}
                      <ExternalLink aria-hidden="true" size={13} />
                    </a>
                  ))}
                </div>
              ) : null}
            </article>
          ))}
        </div>
      )}
    </Panel>
  );
}

function formatList(values: string[]): string {
  return values.length > 0 ? values.join(', ') : 'None';
}

function getImpactKey(item: SystemHealthFeatureImpact): string {
  return [
    item.feature,
    item.status,
    item.affectedBy.join('|'),
    item.links.map((link) => `${link.label}:${link.href}`).join('|')
  ].join('::');
}
