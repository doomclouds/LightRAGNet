import { ShieldAlert, Wrench } from 'lucide-react';

import type { SystemHealthFixFirstItem } from '@/api/systemStatusApi';
import { Panel } from '@/shared/components/Panel';
import { StatusPill } from '@/shared/components/StatusPill';
import { getStatusTone } from './systemStatusPresentation';

type SystemStatusRemediationPanelProps = {
  items: SystemHealthFixFirstItem[];
};

export function SystemStatusRemediationPanel({ items }: SystemStatusRemediationPanelProps) {
  return (
    <Panel as="section" className="system-status__remediation-priorities" aria-label="Fix first">
      <div className="system-status__section-heading">
        <Wrench aria-hidden="true" size={18} />
        <h2>Fix first priorities</h2>
      </div>

      {items.length === 0 ? (
        <p className="system-status__empty">No remediation priorities.</p>
      ) : (
        <ol className="system-status__priority-list">
          {items.map((item, index) => (
            <li className="system-status__priority-item" key={item.checkId}>
              <div className="system-status__priority-header">
                <span className="system-status__priority-rank">{index + 1}</span>
                <div>
                  <h3>
                    <ShieldAlert aria-hidden="true" size={15} />
                    {item.title}
                  </h3>
                  <p>{item.checkId}</p>
                </div>
                <StatusPill tone={getStatusTone(item.status)}>{item.status}</StatusPill>
              </div>
              <p className="system-status__remediation">{item.remediation}</p>
              <dl className="system-status__meta">
                <div>
                  <dt>Affects</dt>
                  <dd>{formatList(item.affects)}</dd>
                </div>
              </dl>
            </li>
          ))}
        </ol>
      )}
    </Panel>
  );
}

function formatList(values: string[]): string {
  return values.length > 0 ? values.join(', ') : 'None';
}
