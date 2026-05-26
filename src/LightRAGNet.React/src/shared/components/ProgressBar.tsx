type ProgressTone = 'neutral' | 'success' | 'warning' | 'danger';

type ProgressBarProps = {
  value: number;
  label?: string;
  tone?: ProgressTone;
  showValue?: boolean;
  className?: string;
};

export function ProgressBar({ value, label, tone = 'neutral', showValue = true, className }: ProgressBarProps) {
  const normalizedValue = Math.max(0, Math.min(100, Math.round(Number.isFinite(value) ? value : 0)));

  return (
    <div
      className={['lrn-progress', `lrn-progress--${tone}`, className].filter(Boolean).join(' ')}
      role="progressbar"
      aria-label={label ?? `Progress ${normalizedValue}%`}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={normalizedValue}
    >
      <span className="lrn-progress__bar" aria-hidden="true">
        <span className="lrn-progress__fill" style={{ width: `${normalizedValue}%` }} />
      </span>
      {showValue ? <span className="lrn-progress__value">{normalizedValue}%</span> : null}
    </div>
  );
}
