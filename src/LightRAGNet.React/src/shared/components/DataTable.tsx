import type { ReactNode } from 'react';

type DataTableSurfaceProps = {
  children: ReactNode;
  className?: string;
};

export function DataTableSurface({ children, className }: DataTableSurfaceProps) {
  return (
    <div className={['lrn-data-table-surface', className].filter(Boolean).join(' ')}>
      {children}
    </div>
  );
}
