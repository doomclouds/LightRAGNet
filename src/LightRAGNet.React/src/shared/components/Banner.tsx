import { AlertCircle, CheckCircle2, Info, TriangleAlert, type LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';

type BannerTone = 'info' | 'success' | 'warning' | 'danger';

type BannerProps = {
  title?: string;
  children: ReactNode;
  tone?: BannerTone;
  role?: 'alert' | 'status' | 'note' | 'none';
};

const toneIcons: Record<BannerTone, LucideIcon> = {
  info: Info,
  success: CheckCircle2,
  warning: TriangleAlert,
  danger: AlertCircle
};

export function Banner({ title, children, tone = 'info', role }: BannerProps) {
  const Icon = toneIcons[tone];
  const resolvedRole = role ?? (tone === 'danger' || tone === 'warning' ? 'alert' : 'status');
  const roleProps = resolvedRole === 'none' ? {} : { role: resolvedRole };

  return (
    <div className={`lrn-banner lrn-banner--${tone}`} {...roleProps}>
      <Icon className="lrn-banner__icon" size={18} aria-hidden="true" />
      <div className="lrn-banner__body">
        {title ? <strong>{title}</strong> : null}
        <div className="lrn-banner__content">{children}</div>
      </div>
    </div>
  );
}
