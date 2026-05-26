import type { ComponentType, ReactNode } from 'react';
import type { LucideProps } from 'lucide-react';

type MetricTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info';

type MetricCardProps = {
  icon: ComponentType<LucideProps>;
  label: string;
  value: ReactNode;
  detail?: ReactNode;
  badge?: ReactNode;
  tone?: MetricTone;
  className?: string;
};

export function MetricCard({ icon: Icon, label, value, detail, badge, tone = 'neutral', className }: MetricCardProps) {
  return (
    <article className={['lrn-metric-card', `lrn-metric-card--${tone}`, className].filter(Boolean).join(' ')}>
      <header className="lrn-metric-card__topline">
        <span className="lrn-metric-card__icon" aria-hidden="true">
          <Icon size={18} />
        </span>
        <p className="lrn-metric-card__label">{label}</p>
      </header>
      <div className="lrn-metric-card__value-row">
        <strong className="lrn-metric-card__value">{value}</strong>
        {badge ? <span className="lrn-metric-card__badge">{badge}</span> : null}
      </div>
      {detail ? <p className="lrn-metric-card__detail">{detail}</p> : null}
    </article>
  );
}
