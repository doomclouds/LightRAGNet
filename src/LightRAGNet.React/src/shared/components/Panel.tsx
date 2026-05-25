import type { HTMLAttributes, ReactNode } from 'react';

type PanelElement = 'section' | 'article' | 'div';

type PanelProps = HTMLAttributes<HTMLElement> & {
  as?: PanelElement;
  children: ReactNode;
};

export function Panel({ as: Component = 'div', className, children, ...props }: PanelProps) {
  return (
    <Component {...props} className={['lrn-panel', className].filter(Boolean).join(' ')}>
      {children}
    </Component>
  );
}
