import type { HTMLAttributes, ReactNode } from 'react';

type ToolbarProps = HTMLAttributes<HTMLDivElement> & {
  label: string;
  children: ReactNode;
};

export function Toolbar({ label, children, className, ...props }: ToolbarProps) {
  return (
    <div {...props} className={['lrn-toolbar', className].filter(Boolean).join(' ')} role="toolbar" aria-label={label}>
      {children}
    </div>
  );
}
