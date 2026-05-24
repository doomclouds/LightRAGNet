import { useEffect, useState } from 'react';
import { getApiBase } from '@/api/http';
import { getMarkdownDocuments } from '@/api/documentsApi';
import { formatDateTime, formatFileSize } from './documentFormatters';
import type { MarkdownDocumentDto, PagedResult } from './documentTypes';

type DocumentsQuery = {
  page: number;
  pageSize: number;
  status?: string;
};

type LoadDocumentsFn = (apiBase: string, query: DocumentsQuery) => Promise<PagedResult<MarkdownDocumentDto>>;

type DocumentsPageProps = {
  apiBase?: string;
  loadDocuments?: LoadDocumentsFn;
};

const pageSize = 10;
const statusOptions = ['Queued', 'Processing', 'Completed', 'Failed', 'Cancelled'];

export function DocumentsPage({
  apiBase = getApiBase(),
  loadDocuments = getMarkdownDocuments
}: DocumentsPageProps) {
  const [documents, setDocuments] = useState<MarkdownDocumentDto[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [status, setStatus] = useState<string>('');
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

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
      })
      .catch((error) => {
        if (!isActive) {
          return;
        }

        setDocuments([]);
        setTotalPages(1);
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

      {!errorMessage && documents.length > 0 ? (
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
                      <button type="button" aria-label={`View ${document.fileName}`}>
                        View
                      </button>
                      <button type="button" aria-label={`Add ${document.fileName} to RAG`}>
                        Add to RAG
                      </button>
                      <button type="button" aria-label={`Delete ${document.fileName}`}>
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
