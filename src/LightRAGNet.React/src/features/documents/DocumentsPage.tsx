import { useEffect, useState } from 'react';
import { getApiBase } from '@/api/http';
import {
  addToRagSystem,
  cancelDocumentPipeline,
  deleteMarkdownDocument,
  getMarkdownDocument,
  getMarkdownDocuments,
  retryDocument
} from '@/api/documentsApi';
import { DocumentPreviewPanel } from './DocumentPreviewPanel';
import { formatDateTime, formatFileSize } from './documentFormatters';
import type {
  DocumentPipelineActionResult,
  MarkdownDocumentDeleteClientResult,
  MarkdownDocumentDto,
  PagedResult
} from './documentTypes';

type DocumentsQuery = {
  page: number;
  pageSize: number;
  status?: string;
};

type LoadDocumentsFn = (apiBase: string, query: DocumentsQuery) => Promise<PagedResult<MarkdownDocumentDto>>;
type LoadDocumentFn = (apiBase: string, id: number) => Promise<MarkdownDocumentDto>;
type AddToRagFn = (apiBase: string, id: number) => Promise<MarkdownDocumentDto>;
type PipelineActionResult = DocumentPipelineActionResult & {
  currentStage?: string | null;
  progress?: number | null;
  errorMessage?: string | null;
};
type PipelineActionFn = (apiBase: string, id: number) => Promise<PipelineActionResult>;
type RemoveDocumentFn = (apiBase: string, id: number) => Promise<MarkdownDocumentDeleteClientResult>;

type DocumentsPageProps = {
  apiBase?: string;
  loadDocuments?: LoadDocumentsFn;
  loadDocument?: LoadDocumentFn;
  addToRag?: AddToRagFn;
  retry?: PipelineActionFn;
  cancelPipeline?: PipelineActionFn;
  removeDocument?: RemoveDocumentFn;
};

const pageSize = 10;
const statusOptions = ['Queued', 'Processing', 'Completed', 'Failed', 'Cancelled'];

export function DocumentsPage({
  apiBase = getApiBase(),
  loadDocuments = getMarkdownDocuments,
  loadDocument = getMarkdownDocument,
  addToRag = addToRagSystem,
  retry = retryDocument,
  cancelPipeline = cancelDocumentPipeline,
  removeDocument = deleteMarkdownDocument
}: DocumentsPageProps) {
  const [documents, setDocuments] = useState<MarkdownDocumentDto[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [status, setStatus] = useState<string>('');
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [previewDocument, setPreviewDocument] = useState<MarkdownDocumentDto | null>(null);

  useEffect(() => {
    let isActive = true;
    const queryStatus = status.length > 0 ? status : undefined;

    setIsLoading(true);
    setErrorMessage(null);

    loadDocuments(apiBase, { page, pageSize, status: queryStatus })
      .then((result) => {
        if (!isActive) {
          return;
        }

        setDocuments(result.items);
        setTotalPages(Math.max(1, result.totalPages));
        setTotalCount(result.totalCount);
      })
      .catch((error) => {
        if (!isActive) {
          return;
        }

        setDocuments([]);
        setTotalPages(1);
        setTotalCount(0);
        setErrorMessage(error instanceof Error ? error.message : 'Failed to load documents');
      })
      .finally(() => {
        if (isActive) {
          setIsLoading(false);
        }
      });

    return () => {
      isActive = false;
    };
  }, [apiBase, loadDocuments, page, status]);

  function handleStatusChange(event: React.ChangeEvent<HTMLSelectElement>) {
    setStatus(event.target.value);
    setPage(1);
  }

  function updateDocument(updatedDocument: MarkdownDocumentDto) {
    setDocuments((current) =>
      current.map((document) => (document.id === updatedDocument.id ? updatedDocument : document))
    );
    setPreviewDocument((current) => (current?.id === updatedDocument.id ? updatedDocument : current));
  }

  function patchDocument(id: number, patch: Partial<MarkdownDocumentDto>) {
    setDocuments((current) =>
      current.map((document) => (document.id === id ? { ...document, ...patch } : document))
    );
    setPreviewDocument((current) => (current?.id === id ? { ...current, ...patch } : current));
  }

  async function handleView(document: MarkdownDocumentDto) {
    setErrorMessage(null);

    try {
      const loadedDocument = await loadDocument(apiBase, document.id);
      updateDocument(loadedDocument);
      setPreviewDocument(loadedDocument);
    } catch (error) {
      setErrorMessage(getErrorMessage(error, 'Failed to load document preview'));
    }
  }

  async function handleAddToRag(document: MarkdownDocumentDto) {
    setErrorMessage(null);

    try {
      updateDocument(await addToRag(apiBase, document.id));
    } catch (error) {
      setErrorMessage(getErrorMessage(error, 'Failed to add document to RAG'));
    }
  }

  async function handleRetry(document: MarkdownDocumentDto) {
    setErrorMessage(null);

    try {
      const result = await retry(apiBase, document.id);
      if (result.accepted) {
        applyPipelineActionResult(document.id, result);
      }
    } catch (error) {
      setErrorMessage(getErrorMessage(error, 'Failed to retry document'));
    }
  }

  async function handleCancel(document: MarkdownDocumentDto) {
    setErrorMessage(null);

    try {
      const result = await cancelPipeline(apiBase, document.id);
      if (result.accepted) {
        applyPipelineActionResult(document.id, result);
      }
    } catch (error) {
      setErrorMessage(getErrorMessage(error, 'Failed to cancel document pipeline'));
    }
  }

  async function handleDelete(document: MarkdownDocumentDto) {
    if (!window.confirm(`Delete ${document.fileName}?`)) {
      return;
    }

    const previousDocument = document;
    setErrorMessage(null);
    patchDocument(document.id, { ragStatus: 'Deleting', ragCurrentStage: null, ragErrorMessage: null });

    try {
      const result = await removeDocument(apiBase, document.id);

      if (result.deletedImmediately) {
        setDocuments((current) => current.filter((item) => item.id !== document.id));
        setPreviewDocument((current) => (current?.id === document.id ? null : current));
        setTotalCount((current) => {
          const nextTotalCount = Math.max(0, current - 1);
          setTotalPages(Math.max(1, Math.ceil(nextTotalCount / pageSize)));
          return nextTotalCount;
        });
        return;
      }

      if (result.accepted) {
        return;
      }

      updateDocument(previousDocument);
      setErrorMessage(result.errorMessage ?? (result.conflict ? 'Document delete conflict' : 'Failed to delete document'));
    } catch (error) {
      updateDocument(previousDocument);
      setErrorMessage(getErrorMessage(error, 'Failed to delete document'));
    }
  }

  function applyPipelineActionResult(id: number, result: PipelineActionResult) {
    patchDocument(id, {
      ragStatus: result.status,
      ragCurrentStage: result.currentStage ?? null,
      ragErrorMessage: result.errorMessage ?? result.message ?? null,
      ragProgress: typeof result.progress === 'number' ? result.progress : 0
    });
  }

  return (
    <section className="document-list" aria-labelledby="document-list-title">
      <div className="document-list__header">
        <div>
          <h1 id="document-list-title">Documents</h1>
          <p>Review uploaded documents and their current RAG ingestion state.</p>
        </div>
        <a className="document-list__upload-link" href="/documents/upload">
          Upload
        </a>
      </div>

      <div className="document-list__toolbar">
        <label className="document-list__filter">
          <span>Status</span>
          <select aria-label="RAG status filter" value={status} onChange={handleStatusChange}>
            <option value="">All</option>
            {statusOptions.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </label>
      </div>

      {isLoading ? <p className="document-list__state">Loading documents...</p> : null}

      {errorMessage ? (
        <p className="document-list__feedback document-list__feedback--error" role="alert">
          {errorMessage}
        </p>
      ) : null}

      {!isLoading && !errorMessage && documents.length === 0 ? (
        <p className="document-list__state">No documents found.</p>
      ) : null}

      {!isLoading && documents.length > 0 ? (
        <div className="document-list__table-wrap">
          <table className="document-list__table">
            <thead>
              <tr>
                <th scope="col">File Name</th>
                <th scope="col">File Size</th>
                <th scope="col">Upload Time</th>
                <th scope="col">RAG Status</th>
                <th scope="col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {documents.map((document) => (
                <tr key={document.id}>
                  <td>{document.fileName}</td>
                  <td>{formatFileSize(document.fileSize)}</td>
                  <td>{formatDateTime(document.uploadTime)}</td>
                  <td>
                    <DocumentStatus document={document} />
                  </td>
                  <td>
                    <div className="document-list__actions">
                      <button type="button" aria-label={`View ${document.fileName}`} onClick={() => void handleView(document)}>
                        View
                      </button>
                      {canAddToRag(document) ? (
                        <button type="button" aria-label={`Add ${document.fileName} to RAG`} onClick={() => void handleAddToRag(document)}>
                          Add to RAG
                        </button>
                      ) : null}
                      {canRetry(document) ? (
                        <button type="button" aria-label={`Retry ${document.fileName}`} onClick={() => void handleRetry(document)}>
                          Retry
                        </button>
                      ) : null}
                      {canCancel(document) ? (
                        <button type="button" aria-label={`Cancel ${document.fileName}`} onClick={() => void handleCancel(document)}>
                          Cancel
                        </button>
                      ) : null}
                      <button type="button" aria-label={`Delete ${document.fileName}`} disabled={isBusy(document)} onClick={() => void handleDelete(document)}>
                        Delete
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}

      {previewDocument ? (
        <DocumentPreviewPanel apiBase={apiBase} document={previewDocument} onClose={() => setPreviewDocument(null)} />
      ) : null}

      <footer className="document-list__footer">
        <button type="button" disabled={page <= 1 || isLoading} onClick={() => setPage((current) => Math.max(1, current - 1))}>
          Previous
        </button>
        <span>
          Page {page} of {totalPages}
        </span>
        <button type="button" disabled={page >= totalPages || isLoading} onClick={() => setPage((current) => current + 1)}>
          Next
        </button>
      </footer>
    </section>
  );
}

function DocumentStatus({ document }: { document: MarkdownDocumentDto }) {
  const statusText = getStatusText(document);
  const progress = Math.max(0, Math.min(100, Math.round(document.ragProgress)));

  return (
    <div className="document-list__status">
      <span className="document-list__status-chip">{statusText}</span>
      {document.ragStatus === 'Processing' ? (
        <div className="document-list__progress" role="progressbar" aria-label={`Progress ${progress}%`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={progress}>
          <span style={{ width: `${progress}%` }} />
        </div>
      ) : null}
    </div>
  );
}

function getStatusText(document: MarkdownDocumentDto): string {
  if (!document.ragStatus) {
    return 'Not Added';
  }

  if (document.ragCurrentStage) {
    return `${document.ragStatus} / ${document.ragCurrentStage}`;
  }

  return document.ragStatus;
}

function canAddToRag(document: MarkdownDocumentDto): boolean {
  return !document.isInRagSystem && !isBusy(document);
}

function canRetry(document: MarkdownDocumentDto): boolean {
  return document.ragStatus === 'Failed' || document.ragStatus === 'Cancelled';
}

function canCancel(document: MarkdownDocumentDto): boolean {
  return document.ragStatus === 'Queued' || document.ragStatus === 'Processing' || document.ragStatus === 'Pending';
}

function isBusy(document: MarkdownDocumentDto): boolean {
  return document.ragStatus === 'Queued' || document.ragStatus === 'Processing' || document.ragStatus === 'Pending' || document.ragStatus === 'Deleting';
}

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}
