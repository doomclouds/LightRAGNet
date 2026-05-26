import { CircleCheck, CircleMinus, CircleX, Clock3 } from 'lucide-react';
import type { ComponentType } from 'react';
import type { LucideProps } from 'lucide-react';
import type { MarkdownDocumentDto } from './documentTypes';

type DocumentStatusBadgeProps = {
  status: MarkdownDocumentDto['ragStatus'];
  className?: string;
};

type StatusView = {
  label: string;
  tone: 'neutral' | 'success' | 'warning' | 'danger';
  icon: ComponentType<LucideProps>;
};

export function DocumentStatusBadge({ status, className }: DocumentStatusBadgeProps) {
  const view = getDocumentStatusView(status);
  const Icon = view.icon;

  return (
    <span className={['document-status-badge', `document-status-badge--${view.tone}`, className].filter(Boolean).join(' ')}>
      <Icon size={13} strokeWidth={2} aria-hidden="true" />
      <span>{view.label}</span>
    </span>
  );
}

export function getDocumentStatusLabel(status: MarkdownDocumentDto['ragStatus']): string {
  return getDocumentStatusView(status).label;
}

function getDocumentStatusView(status: MarkdownDocumentDto['ragStatus']): StatusView {
  if (status === 'Completed') {
    return { label: 'Indexed', tone: 'success', icon: CircleCheck };
  }

  if (status === 'Failed' || status === 'DeletionFailed') {
    return { label: 'Failed', tone: 'danger', icon: CircleX };
  }

  if (status === 'Processing' || status === 'Queued' || status === 'Pending' || status === 'Deleting') {
    return { label: status === 'Pending' ? 'Pending' : 'Processing', tone: 'warning', icon: Clock3 };
  }

  if (status === 'Cancelled') {
    return { label: 'Skipped', tone: 'neutral', icon: CircleMinus };
  }

  return { label: 'Not Added', tone: 'neutral', icon: CircleMinus };
}
