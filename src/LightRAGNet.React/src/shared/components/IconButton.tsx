import { forwardRef, type ButtonHTMLAttributes, type ComponentType } from 'react';
import type { LucideProps } from 'lucide-react';

type IconButtonTone = 'neutral' | 'primary' | 'danger' | 'warning';

type IconButtonProps = Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'type' | 'aria-label' | 'title'> & {
  icon: ComponentType<LucideProps>;
  label: string;
  tone?: IconButtonTone;
};

export const IconButton = forwardRef<HTMLButtonElement, IconButtonProps>(function IconButton(
  { icon: Icon, label, tone = 'neutral', className, ...props },
  ref
) {
  return (
    <button
      {...props}
      ref={ref}
      className={['lrn-icon-button', `lrn-icon-button--${tone}`, className].filter(Boolean).join(' ')}
      type="button"
      aria-label={label}
      title={label}
    >
      <Icon size={16} aria-hidden="true" />
    </button>
  );
});
