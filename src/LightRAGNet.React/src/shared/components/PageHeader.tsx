import type { ReactNode } from 'react';

type PageHeaderProps = {
  title: string;
  description?: string;
  actions?: ReactNode;
  meta?: ReactNode;
};

export function PageHeader({ title, description, actions, meta }: PageHeaderProps) {
  return (
    <div className="lrn-page-header">
      <div>
        <h1>{title}</h1>
        {description ? <p>{description}</p> : null}
        {meta ? <div className="lrn-page-header__meta">{meta}</div> : null}
      </div>
      {actions ? <div className="lrn-page-header__actions">{actions}</div> : null}
    </div>
  );
}
