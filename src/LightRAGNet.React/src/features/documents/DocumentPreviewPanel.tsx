import { useEffect, useRef, type KeyboardEvent } from 'react';
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
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const downloadHref = getDownloadHref(apiBase, document.fileUrl);
  const hasContent = Boolean(document.content?.trim());
  const fullPreviewHref = `/document-preview/${document.id}`;

  useEffect(() => {
    closeButtonRef.current?.focus();
  }, []);

  function handleKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'Escape') {
      event.preventDefault();
      onClose();
      return;
    }

    if (event.key === 'Tab') {
      trapTabFocus(event);
    }
  }

  return (
    <>
      <div className="lrn-scrim document-preview__scrim" aria-hidden="true" onClick={onClose} />
      <aside
        className="lrn-drawer document-preview"
        role="dialog"
        aria-modal="true"
        aria-label={`Preview ${document.fileName}`}
        onKeyDown={handleKeyDown}
      >
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
            <a href={fullPreviewHref}>Open full preview</a>
            {downloadHref ? (
              <a href={downloadHref} download aria-label={`Download ${document.fileName}`}>
                Download
              </a>
            ) : null}
            <button ref={closeButtonRef} type="button" onClick={onClose} aria-label="Close preview">
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
    </>
  );
}

function trapTabFocus(event: KeyboardEvent<HTMLElement>) {
  const focusableElements = getFocusableElements(event.currentTarget);

  if (focusableElements.length === 0) {
    event.preventDefault();
    return;
  }

  const firstElement = focusableElements[0];
  const lastElement = focusableElements[focusableElements.length - 1];
  const activeElement = document.activeElement;

  if (event.shiftKey) {
    if (activeElement === firstElement || !event.currentTarget.contains(activeElement)) {
      event.preventDefault();
      lastElement.focus();
    }
    return;
  }

  if (activeElement === lastElement || !event.currentTarget.contains(activeElement)) {
    event.preventDefault();
    firstElement.focus();
  }
}

function getFocusableElements(container: HTMLElement): HTMLElement[] {
  return Array.from(
    container.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'
    )
  ).filter((element) => !element.hasAttribute('disabled') && element.getAttribute('aria-hidden') !== 'true');
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
