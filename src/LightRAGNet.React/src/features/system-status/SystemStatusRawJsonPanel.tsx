import { Braces } from 'lucide-react';

import type { SystemHealthResponse } from '@/api/systemStatusApi';
import { Panel } from '@/shared/components/Panel';
import { formatHealthJson } from './systemStatusPresentation';

type SystemStatusRawJsonPanelProps = {
  health: SystemHealthResponse;
};

export function SystemStatusRawJsonPanel({ health }: SystemStatusRawJsonPanelProps) {
  return (
    <Panel as="section" className="system-status__raw-json" aria-label="Raw health JSON">
      <div className="system-status__section-heading">
        <Braces aria-hidden="true" size={18} />
        <h2>Raw health JSON</h2>
      </div>
      <pre className="system-status__raw-code">{formatHealthJson(health)}</pre>
    </Panel>
  );
}
