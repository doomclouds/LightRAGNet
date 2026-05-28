import type { SystemHealthFixFirstItem } from '@/api/systemStatusApi';
import { SystemStatusMiniButton, SystemStatusPanel } from './SystemStatusPrimitives';

type SystemStatusRemediationPanelProps = {
  items: SystemHealthFixFirstItem[];
};

export function SystemStatusRemediationPanel({ items }: SystemStatusRemediationPanelProps) {
  return (
    <SystemStatusPanel title="Remediation Priorities" className="system-status__remediation-priorities">
      {items.length === 0 ? (
        <p className="system-status__empty">No remediation priorities.</p>
      ) : (
        <div className="system-status__list">
          {items.map((item, index) => (
            <article className="system-status__remediation" key={item.checkId}>
              <span className={index === 0 ? 'system-status__rank' : 'system-status__rank system-status__rank--warning'}>{index + 1}</span>
              <div>
                <h3>{item.title}</h3>
                <p>{item.remediation || formatList(item.affects)}</p>
              </div>
              <SystemStatusMiniButton aria-label={`View ${item.title}`}>View</SystemStatusMiniButton>
            </article>
          ))}
        </div>
      )}
    </SystemStatusPanel>
  );
}

function formatList(values: string[]): string {
  return values.length > 0 ? values.join(', ') : 'No affected features';
}
