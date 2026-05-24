import type { ReactNode } from 'react';

type StatusPillTone = 'neutral' | 'success' | 'warning' | 'danger' | 'accent';

type StatusPillProps = {
  children: ReactNode;
  tone?: StatusPillTone;
};

export function StatusPill({ children, tone = 'neutral' }: StatusPillProps) {
  return <span className={`lrn-status-pill lrn-status-pill--${tone}`}>{children}</span>;
}
