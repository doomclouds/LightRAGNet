import { Copy, Download } from 'lucide-react';

import type { SystemHealthResponse } from '@/api/systemStatusApi';
import { SystemStatusMiniButton, SystemStatusPanel } from './SystemStatusPrimitives';
import { formatHealthJson } from './systemStatusPresentation';

type SystemStatusRawJsonPanelProps = {
  health: SystemHealthResponse;
  onCopy: () => void;
  onDownload: () => void;
};

export function SystemStatusRawJsonPanel({ health, onCopy, onDownload }: SystemStatusRawJsonPanelProps) {
  return (
    <SystemStatusPanel
      title="Raw Data (JSON)"
      className="system-status__raw-json"
      actions={
        <div className="system-status__raw-toolbar">
          <SystemStatusMiniButton aria-label="Copy Raw JSON" onClick={onCopy}>
            <Copy aria-hidden="true" size={13} />
            Copy
          </SystemStatusMiniButton>
          <SystemStatusMiniButton aria-label="Download Raw JSON" onClick={onDownload}>
            <Download aria-hidden="true" size={13} />
            Download
          </SystemStatusMiniButton>
        </div>
      }
    >
      <pre className="system-status__raw-code">{formatHealthJson(health)}</pre>
    </SystemStatusPanel>
  );
}
