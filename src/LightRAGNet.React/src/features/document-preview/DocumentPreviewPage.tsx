import { useEffect, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { getDocumentPreviewContent, type DocumentPreviewContent } from '@/api/documentPreviewApi';
import { EmptyState } from '@/shared/components/EmptyState';
import { ErrorState } from '@/shared/components/ErrorState';
import { PageHeader } from '@/shared/components/PageHeader';
import { Panel } from '@/shared/components/Panel';
import { StatusPill } from '@/shared/components/StatusPill';
import './document-preview.css';

type LoadPreviewFn = (apiBase: string, documentId: number) => Promise<DocumentPreviewContent>;

type DocumentPreviewPageProps = {
  apiBase: string;
  documentId?: number;
  loadPreview?: LoadPreviewFn;
};

export function DocumentPreviewPage({
  apiBase,
  documentId,
  loadPreview = getDocumentPreviewContent
}: DocumentPreviewPageProps) {
  const [preview, setPreview] = useState<DocumentPreviewContent | null>(null);
  const [isLoading, setIsLoading] = useState(() => Boolean(documentId));
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!documentId) {
      setPreview(null);
      setIsLoading(false);
      setErrorMessage(null);
      return undefined;
    }

    let isActive = true;
    setPreview(null);
    setIsLoading(true);
    setErrorMessage(null);

    loadPreview(apiBase, documentId)
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
  }, [apiBase, documentId, loadPreview]);

  const hasContent = Boolean(preview?.content?.trim());

  return (
    <section className="document-preview-page" aria-label="Document Preview">
      <PageHeader
        title="Document Preview"
        description="Review safe preview content served by the document preview API."
        meta={
          <>
            <StatusPill tone="accent">Reading workspace</StatusPill>
            <StatusPill tone={documentId ? 'accent' : 'neutral'}>
              {documentId ? `Document ${documentId}` : 'No document selected'}
            </StatusPill>
            {preview?.fileName ? <StatusPill tone="neutral">{preview.fileName}</StatusPill> : null}
            {preview?.contentType ? <StatusPill tone="neutral">{preview.contentType}</StatusPill> : null}
          </>
        }
      />

      {!documentId ? (
        <EmptyState
          title="No document selected"
          description="Open a document from Documents or a RAG Chat reference."
        />
      ) : null}

      {isLoading ? <Panel className="document-preview-page__state">Loading preview</Panel> : null}

      {errorMessage ? (
        <ErrorState message={errorMessage} />
      ) : null}

      {documentId && preview && !isLoading && !errorMessage ? (
        <Panel as="article" className="document-preview-page__content" aria-label="Document preview content">
          {hasContent ? (
            <ReactMarkdown remarkPlugins={[remarkGfm]}>{preview?.content}</ReactMarkdown>
          ) : (
            <p className="document-preview-page__empty">No preview content available.</p>
          )}
        </Panel>
      ) : null}
    </section>
  );
}
