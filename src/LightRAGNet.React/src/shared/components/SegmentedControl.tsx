type SegmentedControlOption<TValue extends string> = {
  value: TValue;
  label: string;
  disabled?: boolean;
};

type SegmentedControlProps<TValue extends string> = {
  ariaLabel: string;
  value: TValue;
  options: Array<SegmentedControlOption<TValue>>;
  onChange: (value: TValue) => void;
  className?: string;
};

export function SegmentedControl<TValue extends string>({
  ariaLabel,
  value,
  options,
  onChange,
  className
}: SegmentedControlProps<TValue>) {
  return (
    <div className={['lrn-segmented-control', className].filter(Boolean).join(' ')} role="group" aria-label={ariaLabel}>
      {options.map((option) => (
        <button
          className="lrn-segmented-control__item"
          key={option.value}
          type="button"
          aria-pressed={option.value === value}
          disabled={option.disabled}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}
