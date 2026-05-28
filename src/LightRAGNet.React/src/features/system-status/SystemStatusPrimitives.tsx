import type { ButtonHTMLAttributes, ComponentType, ReactNode } from 'react';
import type { LucideProps } from 'lucide-react';
import type { SystemHealthStatus } from '@/api/systemStatusApi';

type SystemStatusPanelProps = {
  title?: string;
  className?: string;
  ariaLabel?: string;
  actions?: ReactNode;
  children: ReactNode;
};

type SystemStatusTileProps = {
  icon: ComponentType<LucideProps>;
  label: string;
  value: string;
  note: string;
  tone?: 'healthy' | 'warning' | 'neutral';
};

type SystemStatusMiniButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  children: ReactNode;
};

export function SystemStatusPanel({ title, className, ariaLabel, actions, children }: SystemStatusPanelProps) {
  return (
    <section className={['system-status__panel', className].filter(Boolean).join(' ')} aria-label={ariaLabel ?? title}>
      {title || actions ? (
        <div className="system-status__panel-head">
          {title ? <h2>{title}</h2> : <span />}
          {actions}
        </div>
      ) : null}
      {children}
    </section>
  );
}

export function SystemStatusTabs() {
  const tabs = ['Evidence', 'Remediation', 'Feature Impact', 'Raw Data'];

  return (
    <div className="system-status__tabs" role="tablist" aria-label="System status sections">
      {tabs.map((tab, index) => (
        <button
          aria-selected={index === 0}
          className={index === 0 ? 'system-status__tab system-status__tab--active' : 'system-status__tab'}
          key={tab}
          role="tab"
          type="button"
        >
          {tab}
        </button>
      ))}
    </div>
  );
}

export function SystemStatusTile({ icon: Icon, label, value, note, tone = 'neutral' }: SystemStatusTileProps) {
  return (
    <section className="system-status__tile">
      <div className={`system-status__tile-icon system-status__tile-icon--${tone}`}>
        <Icon aria-hidden="true" size={18} />
      </div>
      <div>
        <p className="system-status__tile-label">{label}</p>
        <p className="system-status__tile-value">{value}</p>
        <p className="system-status__tile-note">{note}</p>
      </div>
    </section>
  );
}

export function SystemStatusMiniButton({ children, className, ...props }: SystemStatusMiniButtonProps) {
  return (
    <button className={['system-status__mini-button', className].filter(Boolean).join(' ')} type="button" {...props}>
      {children}
    </button>
  );
}

export function SystemStatusBadge({ status }: { status: SystemHealthStatus }) {
  return <span className={`system-status__pill system-status__pill--${getStatusClass(status)}`}>{status}</span>;
}

function getStatusClass(status: SystemHealthStatus): string {
  if (status === 'Healthy') {
    return 'healthy';
  }

  if (status === 'Degraded' || status === 'NotMeasured') {
    return 'warning';
  }

  return 'critical';
}
