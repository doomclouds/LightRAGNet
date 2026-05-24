import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { formatDateTime, formatFileSize } from './documentFormatters';
import type { MarkdownDocumentDto } from './documentTypes';

type DocumentPreviewPanelProps = {
  apiBase: string;
  document: MarkdownDocumentDto;
  onClose: () => void;
};

export function DocumentPreviewPanel({ apiBase, document, onClose }: DocumentPreviewPanelProps) {
  const downloadHref = getDownloadHref(apiBase, document.fileUrl);
  const hasContent = Boolean(document.content?.trim());

  return (
    <aside className="document-preview" aria-label={`Preview ${document.fileName}`}>
      <header className="document-preview__header">
        <div>
          <h2>{document.fileName}</h2>
          <dl className="document-preview__meta">
            <div>
              <dt>File Size</dt>
              <dd>{formatFileSize(document.fileSize)}</dd>
            </div>
            <div>
              <dt>Upload Time</dt>
              <dd>{formatDateTime(document.uploadTime)}</dd>
            </div>
          </dl>
        </div>
        <div className="document-preview__tools">
          {downloadHref ? (
            <a href={downloadHref} download aria-label={`Download ${document.fileName}`}>
              Download
            </a>
          ) : null}
          <button type="button" onClick={onClose} aria-label="Close preview">
            Close
          </button>
        </div>
      </header>

      <div className="document-preview__content">
        {hasContent ? (
          <ReactMarkdown remarkPlugins={[remarkGfm]}>{document.content}</ReactMarkdown>
        ) : (
          <p className="document-preview__empty">No preview content available.</p>
        )}
      </div>
    </aside>
  );
}

export function getDownloadHref(apiBase: string, fileUrl?: string | null): string | null {
  if (!fileUrl) {
    return null;
  }

  if (fileUrl.startsWith('/uploads/')) {
    return `${apiBase.replace(/\/+$/, '')}${fileUrl}`;
  }

  if (isSameOriginUploadUrl(apiBase, fileUrl)) {
    return fileUrl;
  }

  return null;
}

function isSameOriginUploadUrl(apiBase: string, value: string): boolean {
  try {
    const apiUrl = new URL(apiBase);
    const url = new URL(value);
    return url.origin === apiUrl.origin && url.pathname.startsWith('/uploads/');
  } catch {
    return false;
  }
}
