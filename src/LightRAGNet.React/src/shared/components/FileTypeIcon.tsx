import { FileText } from 'lucide-react';

type FileTypeIconProps = {
  type: string;
  label?: string;
  size?: 'sm' | 'md' | 'lg';
  className?: string;
};

export function FileTypeIcon({ type, label, size = 'md', className }: FileTypeIconProps) {
  const normalizedType = normalizeFileType(type);
  const displayLabel = label ?? getFileTypeLabel(normalizedType);

  return (
    <span
      className={['lrn-file-type-icon', `lrn-file-type-icon--${normalizedType}`, `lrn-file-type-icon-size--${size}`, className].filter(Boolean).join(' ')}
      aria-hidden="true"
    >
      <FileText size={size === 'lg' ? 22 : 18} strokeWidth={1.8} />
      <span>{displayLabel}</span>
    </span>
  );
}

function normalizeFileType(type: string): string {
  const value = type.trim().toLowerCase();

  if (value === 'pdf') {
    return 'pdf';
  }

  if (value === 'markdown' || value === 'md') {
    return 'md';
  }

  if (value === 'docx' || value === 'doc') {
    return 'docx';
  }

  if (value === 'pptx' || value === 'ppt') {
    return 'pptx';
  }

  if (value === 'txt' || value === 'text') {
    return 'txt';
  }

  return 'file';
}

function getFileTypeLabel(type: string): string {
  if (type === 'file') {
    return '';
  }

  return type.toUpperCase();
}
