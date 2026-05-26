import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { Download, ExternalLink, X } from 'lucide-react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { getDocumentPreviewContent, type DocumentPreviewContent } from '@/api/documentPreviewApi';
import { FileTypeIcon } from '@/shared/components/FileTypeIcon';
import { IconButton } from '@/shared/components/IconButton';
import { DocumentStatusBadge } from './DocumentStatusBadge';
import { formatDateTime, formatFileSize } from './documentFormatters';
import type { MarkdownDocumentDto } from './documentTypes';

type LoadPreviewFn = (apiBase: string, documentId: number) => Promise<DocumentPreviewContent>;

type DocumentPreviewPanelProps = {
  apiBase: string;
  document: MarkdownDocumentDto;
  onClose: () => void;
  loadPreview?: LoadPreviewFn;
};

export function DocumentPreviewPanel({
  apiBase,
  document,
  onClose,
  loadPreview = getDocumentPreviewContent
}: DocumentPreviewPanelProps) {
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const [preview, setPreview] = useState<DocumentPreviewContent | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<'preview' | 'metadata'>('preview');
  const downloadHref = getDownloadHref(apiBase, document.fileUrl);
  const hasContent = Boolean(preview?.content?.trim());
  const fullPreviewHref = `/document-preview/${document.id}`;
  const fileType = getDocumentFileType(document, preview?.contentType);

  useEffect(() => {
    closeButtonRef.current?.focus();
  }, []);

  useEffect(() => {
    let isActive = true;
    setPreview(null);
    setIsLoading(true);
    setErrorMessage(null);

    loadPreview(apiBase, document.id)
      .then((nextPreview) => {
        if (isActive) {
          setPreview(nextPreview);
        }
      })
      .catch((error) => {
        if (isActive) {
          setErrorMessage(error instanceof Error ? error.message : 'Failed to load document preview.');
        }
      })
      .finally(() => {
        if (isActive) {
          setIsLoading(false);
        }
      });

    return () => {
      isActive = false;
    };
  }, [apiBase, document.id, loadPreview]);

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
          <div className="document-preview__file-summary">
            <FileTypeIcon type={fileType} size="lg" className="document-preview__file-icon" />
            <div>
              <h2>{preview?.fileName || document.fileName}</h2>
              <p>{fileType} · {formatFileSize(document.fileSize)}</p>
            </div>
          </div>
          <IconButton ref={closeButtonRef} icon={X} label="Close preview" onClick={onClose} />
        </header>

        <div className="document-preview__tabs" role="tablist" aria-label="Document preview views">
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'preview'}
            onClick={() => setActiveTab('preview')}
          >
            Preview
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'metadata'}
            onClick={() => setActiveTab('metadata')}
          >
            Metadata
          </button>
        </div>

        <div className="document-preview__body">
          {activeTab === 'preview' ? (
            <>
              <MetadataCard document={document} contentType={preview?.contentType} fileType={fileType} />
              <section className="document-preview__content-card" aria-label="Content Preview">
                <header>
                  <h3>Content Preview</h3>
                  <span>{preview?.fileName || document.fileName}</span>
                </header>
                <div className="document-preview__content">
                  {isLoading ? <p className="document-preview__empty">Loading preview...</p> : null}
                  {errorMessage ? (
                    <p className="document-preview__empty" role="alert">
                      {errorMessage}
                    </p>
                  ) : null}
                  {!isLoading && !errorMessage && hasContent ? (
                    <ReactMarkdown remarkPlugins={[remarkGfm]}>{preview?.content}</ReactMarkdown>
                  ) : null}
                  {!isLoading && !errorMessage && !hasContent ? (
                    <p className="document-preview__empty">No preview content available.</p>
                  ) : null}
                </div>
              </section>
            </>
          ) : (
            <MetadataCard document={document} contentType={preview?.contentType} fileType={fileType} expanded />
          )}
        </div>

        <footer className="document-preview__footer">
          <a className="lrn-button lrn-button--secondary" href={fullPreviewHref} aria-label="Open full preview">
            <ExternalLink size={15} aria-hidden="true" />
            Open Full Preview
          </a>
          {downloadHref ? (
            <a className="lrn-button lrn-button--primary" href={downloadHref} download aria-label={`Download ${document.fileName}`}>
              <Download size={15} aria-hidden="true" />
              Download
            </a>
          ) : null}
        </footer>
      </aside>
    </>
  );
}

function MetadataCard({
  document,
  contentType,
  fileType,
  expanded = false
}: {
  document: MarkdownDocumentDto;
  contentType?: string | null;
  fileType: string;
  expanded?: boolean;
}) {
  return (
    <section className="document-preview__metadata-card" aria-label="Document metadata">
      <header>
        <h3>Metadata</h3>
      </header>
      <dl className="document-preview__meta-grid">
        <div>
          <dt>Uploaded Time</dt>
          <dd>{formatDateTime(document.uploadTime)}</dd>
        </div>
        <div>
          <dt>RAG Status</dt>
          <dd><DocumentStatusBadge status={document.ragStatus} /></dd>
        </div>
        <div>
          <dt>Chunks</dt>
          <dd>{document.ragDocumentId ? 'Available' : '—'}</dd>
        </div>
        <div>
          <dt>File Type</dt>
          <dd>{fileType}</dd>
        </div>
        {expanded ? (
          <>
            <div>
              <dt>File Size</dt>
              <dd>{formatFileSize(document.fileSize)}</dd>
            </div>
            <div>
              <dt>Content Type</dt>
              <dd>{contentType || document.originalContentType || '—'}</dd>
            </div>
            <div>
              <dt>RAG Document ID</dt>
              <dd>{document.ragDocumentId || '—'}</dd>
            </div>
            <div>
              <dt>Track ID</dt>
              <dd>{document.trackId || '—'}</dd>
            </div>
          </>
        ) : null}
      </dl>
    </section>
  );
}

function getDocumentFileType(document: MarkdownDocumentDto, contentType?: string | null): string {
  const normalizedContentType = (contentType || document.originalContentType || '').toLowerCase();
  const extension = (document.originalFileName ?? document.fileName).split('.').pop()?.toLowerCase() ?? '';

  if (normalizedContentType.includes('pdf') || extension === 'pdf') {
    return 'PDF';
  }

  if (normalizedContentType.includes('word') || extension === 'docx' || extension === 'doc') {
    return 'DOCX';
  }

  if (normalizedContentType.includes('presentation') || extension === 'pptx' || extension === 'ppt') {
    return 'PPTX';
  }

  if (normalizedContentType.includes('markdown') || extension === 'md' || extension === 'markdown') {
    return 'Markdown';
  }

  if (normalizedContentType.includes('text') || extension === 'txt') {
    return 'TXT';
  }

  return extension.length > 0 ? extension.toUpperCase() : 'File';
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
