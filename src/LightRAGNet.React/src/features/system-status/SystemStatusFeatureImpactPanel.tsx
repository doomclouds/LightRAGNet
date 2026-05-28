import { FileUp, MessageCircle, Search, Share2, type LucideIcon } from 'lucide-react';

import type { SystemHealthFeatureImpact } from '@/api/systemStatusApi';
import { SystemStatusBadge, SystemStatusPanel } from './SystemStatusPrimitives';

type SystemStatusFeatureImpactPanelProps = {
  items: SystemHealthFeatureImpact[];
};

export function SystemStatusFeatureImpactPanel({ items }: SystemStatusFeatureImpactPanelProps) {
  return (
    <SystemStatusPanel title="Feature Impact" className="system-status__feature-impact-list">
      {items.length === 0 ? (
        <p className="system-status__empty">No feature impacts reported.</p>
      ) : (
        <div className="system-status__list">
          {items.map((item) => (
            <article className="system-status__impact" key={getImpactKey(item)}>
              <div>
                <h3 className="system-status__impact-title">
                  <ImpactIcon feature={item.feature} />
                  {item.feature}
                </h3>
                <p>{item.reason}</p>
              </div>
              <SystemStatusBadge status={item.status} />
            </article>
          ))}
        </div>
      )}
    </SystemStatusPanel>
  );
}

function ImpactIcon({ feature }: { feature: string }) {
  const Icon = getImpactIcon(feature);
  return <Icon aria-hidden="true" size={15} />;
}

function getImpactIcon(feature: string): LucideIcon {
  const normalized = feature.toLowerCase();

  if (normalized.includes('document') || normalized.includes('ingestion')) {
    return FileUp;
  }

  if (normalized.includes('chat') || normalized.includes('rag')) {
    return MessageCircle;
  }

  if (normalized.includes('graph')) {
    return Share2;
  }

  return Search;
}

function getImpactKey(item: SystemHealthFeatureImpact): string {
  return [
    item.feature,
    item.status,
    item.affectedBy.join('|'),
    item.links.map((link) => `${link.label}:${link.href}`).join('|')
  ].join('::');
}
