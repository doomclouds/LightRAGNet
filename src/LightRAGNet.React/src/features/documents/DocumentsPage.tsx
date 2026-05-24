import { useCallback, useEffect, useRef, useState } from 'react';
import { getDocumentPreviewContent, type DocumentPreviewContent } from '@/api/documentPreviewApi';
import { getApiBase } from '@/api/http';
import {
  addToRagSystem,
  cancelDocumentPipeline,
  deleteMarkdownDocument,
  getMarkdownDocuments,
  retryDocument
} from '@/api/documentsApi';
import { PageHeader } from '@/shared/components/PageHeader';
import { PageTabs, type PageTabItem } from '@/shared/components/PageTabs';
import { StatusPill } from '@/shared/components/StatusPill';
import { DocumentPreviewPanel, getDownloadHref } from './DocumentPreviewPanel';
import { formatDateTime, formatFileSize } from './documentFormatters';
import {
  getShortErrorMessage,
  shouldRefreshForMissingTaskStatus,
  shouldRefreshForTaskStatus
} from './documentStatus';
import type {
  DocumentPipelineActionResult,
  MarkdownDocumentDeleteClientResult,
  MarkdownDocumentDto,
  PagedResult,
  TaskStatusUpdate
} from './documentTypes';

type DocumentsQuery = {
  page: number;
  pageSize: number;
  status?: string;
};

type LoadDocumentsFn = (apiBase: string, query: DocumentsQuery) => Promise<PagedResult<MarkdownDocumentDto>>;
type LoadPreviewFn = (apiBase: string, id: number) => Promise<DocumentPreviewContent>;
type AddToRagFn = (apiBase: string, id: number) => Promise<MarkdownDocumentDto>;
type PipelineActionFn = (apiBase: string, id: number) => Promise<DocumentPipelineActionResult>;
type RemoveDocumentFn = (apiBase: string, id: number) => Promise<MarkdownDocumentDeleteClientResult>;
type TaskUpdateSubscriptionFn = (handler: (update: TaskStatusUpdate) => void) => () => void;
type DataClearedSubscriptionFn = (handler: () => void) => () => void;

type DocumentsPageProps = {
  apiBase?: string;
  loadDocuments?: LoadDocumentsFn;
  loadPreview?: LoadPreviewFn;
  addToRag?: AddToRagFn;
  retry?: PipelineActionFn;
  cancelPipeline?: PipelineActionFn;
  removeDocument?: RemoveDocumentFn;
  subscribeToTaskUpdates?: TaskUpdateSubscriptionFn;
  subscribeToDataCleared?: DataClearedSubscriptionFn;
};

const pageSize = 10;
const statusOptions = ['Queued', 'Processing', 'Completed', 'Failed', 'Cancelled'];
const statusTabs: PageTabItem[] = [
  { id: 'all', label: 'All Documents', href: '/documents' },
  ...statusOptions.map((option) => ({
    id: option.toLowerCase(),
    label: option,
    href: `/documents?status=${encodeURIComponent(option)}`
  }))
];
const statusOptionSet = new Set(statusOptions);

export function DocumentsPage({
  apiBase = getApiBase(),
  loadDocuments = getMarkdownDocuments,
  loadPreview = getDocumentPreviewContent,
  addToRag = addToRagSystem,
  retry = retryDocument,
  cancelPipeline = cancelDocumentPipeline,
  removeDocument = deleteMarkdownDocument,
  subscribeToTaskUpdates,
  subscribeToDataCleared
}: DocumentsPageProps) {
  const [documents, setDocuments] = useState<MarkdownDocumentDto[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [status, setStatus] = useState<string>(() => getStatusFromLocation());
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [previewDocument, setPreviewDocument] = useState<MarkdownDocumentDto | null>(null);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [documentActionTokens, setDocumentActionTokens] = useState<Record<number, string>>({});
  const documentActionTokensRef = useRef<Record<number, string>>({});
  const documentsRef = useRef<MarkdownDocumentDto[]>([]);
  const pageRef = useRef(page);
  const totalCountRef = useRef(totalCount);
  const previewTriggerRef = useRef<HTMLElement | null>(null);
  const refreshTimerRef = useRef<number | undefined>(undefined);

  const scheduleRefresh = useCallback(() => {
    window.clearTimeout(refreshTimerRef.current);
    refreshTimerRef.current = window.setTimeout(() => {
      setRefreshVersion((current) => current + 1);
    }, 240);
  }, []);

  const refreshNow = useCallback(() => {
    window.clearTimeout(refreshTimerRef.current);
    setRefreshVersion((current) => current + 1);
  }, []);

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

        documentsRef.current = result.items;
        totalCountRef.current = result.totalCount;
        setDocuments(result.items);
        setTotalPages(Math.max(1, result.totalPages));
        setTotalCount(result.totalCount);
      })
      .catch((error) => {
        if (!isActive) {
          return;
        }

        documentsRef.current = [];
        totalCountRef.current = 0;
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
  }, [apiBase, loadDocuments, page, refreshVersion, status]);

  useEffect(() => {
    documentsRef.current = documents;
  }, [documents]);

  useEffect(() => {
    pageRef.current = page;
  }, [page]);

  useEffect(() => {
    totalCountRef.current = totalCount;
  }, [totalCount]);

  useEffect(() => {
    return () => {
      window.clearTimeout(refreshTimerRef.current);
    };
  }, []);

  useEffect(() => {
    function handlePopState() {
      setStatus(getStatusFromLocation());
      setPage(1);
    }

    window.addEventListener('popstate', handlePopState);

    return () => {
      window.removeEventListener('popstate', handlePopState);
    };
  }, []);

  const applyTaskStatusUpdate = useCallback((update: TaskStatusUpdate): { found: boolean; oldStatus?: string | null } => {
    const targetDocument = documentsRef.current.find((document) => document.id === update.documentId);

    if (!targetDocument) {
      return { found: false };
    }

    const oldStatus = targetDocument.ragStatus;
    const nextDocuments = documentsRef.current.map((document) => (
      document.id === update.documentId ? applyTaskUpdateToDocument(document, update) : document
    ));

    documentsRef.current = nextDocuments;
    setDocuments(nextDocuments);
    setPreviewDocument((current) => (
      current?.id === update.documentId ? applyTaskUpdateToDocument(current, update) : current
    ));

    return { found: true, oldStatus };
  }, []);

  const removeDeletedDocumentFromCurrentPage = useCallback((documentId: number) => {
    const currentPage = pageRef.current;
    const nextTotalCount = Math.max(0, totalCountRef.current - 1);
    const nextTotalPages = Math.max(1, Math.ceil(nextTotalCount / pageSize));
    const nextPage = Math.min(currentPage, nextTotalPages);
    const nextDocuments = documentsRef.current.filter((document) => document.id !== documentId);

    documentsRef.current = nextDocuments;
    totalCountRef.current = nextTotalCount;
    pageRef.current = nextPage;
    setDocuments(nextDocuments);
    setPreviewDocument((current) => (current?.id === documentId ? null : current));
    setTotalCount(nextTotalCount);
    setTotalPages(nextTotalPages);

    if (nextPage !== currentPage) {
      setPage(nextPage);
    } else {
      scheduleRefresh();
    }
  }, [scheduleRefresh]);

  useEffect(() => {
    if (!subscribeToTaskUpdates) {
      return undefined;
    }

    return subscribeToTaskUpdates((update) => {
      const { found, oldStatus } = applyTaskStatusUpdate(update);

      if (!found) {
        if (shouldRefreshForMissingTaskStatus(update, status)) {
          scheduleRefresh();
        }
        return;
      }

      if (update.operationType === 'DeleteDocument' && update.status === 'Completed') {
        removeDeletedDocumentFromCurrentPage(update.documentId);
        return;
      }

      if (shouldRefreshForTaskStatus(update, oldStatus, status)) {
        scheduleRefresh();
      }
    });
  }, [
    applyTaskStatusUpdate,
    removeDeletedDocumentFromCurrentPage,
    scheduleRefresh,
    status,
    subscribeToTaskUpdates
  ]);

  useEffect(() => {
    if (!subscribeToDataCleared) {
      return undefined;
    }

    return subscribeToDataCleared(() => {
      documentsRef.current = [];
      totalCountRef.current = 0;
      pageRef.current = 1;
      setDocuments([]);
      setPreviewDocument(null);
      setTotalCount(0);
      setTotalPages(1);
      setPage(1);
      refreshNow();
    });
  }, [refreshNow, subscribeToDataCleared]);

  function handleStatusChange(event: React.ChangeEvent<HTMLSelectElement>) {
    applyStatusFilter(event.target.value);
  }

  function handleStatusTabClick(event: React.MouseEvent<HTMLDivElement>) {
    const target = event.target;

    if (!(target instanceof HTMLAnchorElement)) {
      return;
    }

    const url = new URL(target.href);

    if (url.pathname !== '/documents') {
      return;
    }

    event.preventDefault();
    applyStatusFilter(url.searchParams.get('status') ?? '');
  }

  function applyStatusFilter(nextStatus: string) {
    const normalizedStatus = normalizeStatus(nextStatus);
    setStatus(normalizedStatus);
    setPage(1);
    syncStatusToUrl(normalizedStatus);
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

  function beginDocumentAction(id: number): string | null {
    if (documentActionTokensRef.current[id]) {
      return null;
    }

    const token = `${id}-${Date.now()}-${Math.random()}`;
    documentActionTokensRef.current = { ...documentActionTokensRef.current, [id]: token };
    setDocumentActionTokens(documentActionTokensRef.current);
    return token;
  }

  function finishDocumentAction(id: number, token: string) {
    if (documentActionTokensRef.current[id] !== token) {
      return;
    }

    const nextTokens = { ...documentActionTokensRef.current };
    delete nextTokens[id];
    documentActionTokensRef.current = nextTokens;
    setDocumentActionTokens(nextTokens);
  }

  function isDocumentActionPending(id: number): boolean {
    return Boolean(documentActionTokens[id]);
  }

  function handleView(document: MarkdownDocumentDto) {
    const token = beginDocumentAction(document.id);
    if (!token) {
      return;
    }

    previewTriggerRef.current = window.document.activeElement instanceof HTMLElement ? window.document.activeElement : null;
    setErrorMessage(null);
    setPreviewDocument(document);
    finishDocumentAction(document.id, token);
  }

  function closePreview() {
    const trigger = previewTriggerRef.current;
    setPreviewDocument(null);

    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus();
      }
    }, 0);
  }

  async function handleAddToRag(document: MarkdownDocumentDto) {
    const token = beginDocumentAction(document.id);
    if (!token) {
      return;
    }

    setErrorMessage(null);

    try {
      updateDocument(await addToRag(apiBase, document.id));
    } catch (error) {
      setErrorMessage(getErrorMessage(error, 'Failed to add document to RAG'));
    } finally {
      finishDocumentAction(document.id, token);
    }
  }

  async function handleRetry(document: MarkdownDocumentDto) {
    const token = beginDocumentAction(document.id);
    if (!token) {
      return;
    }

    setErrorMessage(null);

    try {
      const result = await retry(apiBase, document.id);
      if (result.accepted) {
        applyPipelineActionResult(document.id, result, 'retry');
      }
    } catch (error) {
      setErrorMessage(getErrorMessage(error, 'Failed to retry document'));
    } finally {
      finishDocumentAction(document.id, token);
    }
  }

  async function handleCancel(document: MarkdownDocumentDto) {
    const token = beginDocumentAction(document.id);
    if (!token) {
      return;
    }

    setErrorMessage(null);

    try {
      const result = await cancelPipeline(apiBase, document.id);
      if (result.accepted) {
        applyPipelineActionResult(document.id, result, 'cancel');
      }
    } catch (error) {
      setErrorMessage(getErrorMessage(error, 'Failed to cancel document pipeline'));
    } finally {
      finishDocumentAction(document.id, token);
    }
  }

  async function handleDelete(document: MarkdownDocumentDto) {
    const token = beginDocumentAction(document.id);
    if (!token) {
      return;
    }

    if (!window.confirm(`Delete ${document.fileName}?`)) {
      finishDocumentAction(document.id, token);
      return;
    }

    const previousDocument = document;
    setErrorMessage(null);
    patchDocument(document.id, { ragStatus: 'Deleting', ragCurrentStage: null, ragErrorMessage: null });

    try {
      const result = await removeDocument(apiBase, document.id);

      if (result.deletedImmediately) {
        const nextTotalCount = Math.max(0, totalCount - 1);
        const nextTotalPages = Math.max(1, Math.ceil(nextTotalCount / pageSize));
        const nextPage = Math.min(page, nextTotalPages);

        setDocuments((current) => current.filter((item) => item.id !== document.id));
        setPreviewDocument((current) => (current?.id === document.id ? null : current));
        setTotalCount(nextTotalCount);
        setTotalPages(nextTotalPages);

        if (nextPage !== page) {
          setPage(nextPage);
        } else {
          setRefreshVersion((current) => current + 1);
        }
        return;
      }

      if (result.accepted) {
        return;
      }

      rollbackOptimisticDelete(previousDocument);
      setErrorMessage(result.errorMessage ?? (result.conflict ? 'Document delete conflict' : 'Failed to delete document'));
    } catch (error) {
      rollbackOptimisticDelete(previousDocument);
      setErrorMessage(getErrorMessage(error, 'Failed to delete document'));
    } finally {
      finishDocumentAction(document.id, token);
    }
  }

  function rollbackOptimisticDelete(previousDocument: MarkdownDocumentDto) {
    const rollback = (current: MarkdownDocumentDto) =>
      current.ragStatus === 'Deleting'
        ? {
            ...current,
            ragStatus: previousDocument.ragStatus,
            ragCurrentStage: previousDocument.ragCurrentStage,
            ragErrorMessage: previousDocument.ragErrorMessage
          }
        : current;

    setDocuments((current) =>
      current.map((document) => (document.id === previousDocument.id ? rollback(document) : document))
    );
    setPreviewDocument((current) => (current?.id === previousDocument.id ? rollback(current) : current));
  }

  function applyPipelineActionResult(id: number, result: DocumentPipelineActionResult, action: 'retry' | 'cancel') {
    setDocuments((current) =>
      current.map((document) => (document.id === id ? applyPipelinePatch(document, result, action) : document))
    );
    setPreviewDocument((current) => (current?.id === id ? applyPipelinePatch(current, result, action) : current));
  }

  const activeCount = documents.filter((document) => isBusy(document)).length;
  const failedCount = documents.filter((document) => document.ragStatus === 'Failed' || document.ragStatus === 'DeletionFailed').length;
  const completedCount = documents.filter((document) => document.ragStatus === 'Completed').length;

  return (
    <section className="document-list" aria-label="Documents">
      <article className="document-list__page-header">
        <PageHeader
          title="Documents"
          description="Review uploaded documents and their current RAG ingestion state."
          meta={
            <>
              <StatusPill tone="accent">{totalCount} total</StatusPill>
              <StatusPill tone={activeCount > 0 ? 'warning' : 'neutral'}>{activeCount} active</StatusPill>
              <StatusPill tone={failedCount > 0 ? 'danger' : 'success'}>{failedCount} attention</StatusPill>
            </>
          }
          actions={
            <a className="lrn-button document-list__upload-link" href="/documents/upload" aria-label="Upload">
              Upload
            </a>
          }
        />
      </article>

      <div onClickCapture={handleStatusTabClick}>
        <PageTabs
          tabs={statusTabs}
          currentId={status.length > 0 ? status.toLowerCase() : 'all'}
          label="Document status views"
        />
      </div>

      <div className="document-list__summary" aria-label="Document lifecycle summary">
        <SummaryCard label="Total Documents" value={totalCount} detail={status ? `${status} filter active` : 'All statuses'} />
        <SummaryCard label="Completed" value={completedCount} detail="Completed on this page" />
        <SummaryCard label="In Flight" value={activeCount} detail="Active on this page" />
        <SummaryCard label="Needs Review" value={failedCount} detail="Failed on this page" />
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
          <table className="lrn-data-table document-list__table" aria-label="Document lifecycle">
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
                <DocumentRow
                  key={document.id}
                  apiBase={apiBase}
                  document={document}
                  isActionPending={isDocumentActionPending(document.id)}
                  onView={handleView}
                  onAddToRag={handleAddToRag}
                  onRetry={handleRetry}
                  onCancel={handleCancel}
                  onDelete={handleDelete}
                />
              ))}
            </tbody>
          </table>
        </div>
      ) : null}

      {previewDocument ? (
        <DocumentPreviewPanel
          apiBase={apiBase}
          document={previewDocument}
          loadPreview={loadPreview}
          onClose={closePreview}
        />
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

function SummaryCard({ label, value, detail }: { label: string; value: number; detail: string }) {
  return (
    <article className="document-list__summary-card">
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </article>
  );
}

type DocumentRowProps = {
  apiBase: string;
  document: MarkdownDocumentDto;
  isActionPending: boolean;
  onView: (document: MarkdownDocumentDto) => void;
  onAddToRag: (document: MarkdownDocumentDto) => void;
  onRetry: (document: MarkdownDocumentDto) => void;
  onCancel: (document: MarkdownDocumentDto) => void;
  onDelete: (document: MarkdownDocumentDto) => void;
};

function DocumentRow({
  apiBase,
  document,
  isActionPending,
  onView,
  onAddToRag,
  onRetry,
  onCancel,
  onDelete
}: DocumentRowProps) {
  const downloadHref = getDownloadHref(apiBase, document.fileUrl);

  return (
    <tr>
      <td>{document.fileName}</td>
      <td>{formatFileSize(document.fileSize)}</td>
      <td>{formatDateTime(document.uploadTime)}</td>
      <td>
        <DocumentStatus document={document} />
      </td>
      <td>
        <div className="document-list__actions">
          <button type="button" aria-label={`View ${document.fileName}`} disabled={isActionPending} onClick={() => void onView(document)}>
            View
          </button>
          {downloadHref ? (
            <a href={downloadHref} download aria-label={`Download ${document.fileName}`}>
              Download
            </a>
          ) : null}
          {canAddToRag(document) ? (
            <button type="button" aria-label={`Add ${document.fileName} to RAG`} disabled={isActionPending} onClick={() => void onAddToRag(document)}>
              Add to RAG
            </button>
          ) : null}
          {canRetry(document) ? (
            <button type="button" aria-label={`Retry ${document.fileName}`} disabled={isActionPending} onClick={() => void onRetry(document)}>
              Retry
            </button>
          ) : null}
          {canCancel(document) ? (
            <button type="button" aria-label={`Cancel ${document.fileName}`} disabled={isActionPending} onClick={() => void onCancel(document)}>
              Cancel
            </button>
          ) : null}
          <button type="button" aria-label={`Delete ${document.fileName}`} disabled={isActionPending || isBusy(document)} onClick={() => void onDelete(document)}>
            Delete
          </button>
        </div>
      </td>
    </tr>
  );
}

function DocumentStatus({ document }: { document: MarkdownDocumentDto }) {
  const statusText = getStatusText(document);
  const progress = Math.max(0, Math.min(100, Math.round(document.ragProgress)));
  const shouldShowError = (document.ragStatus === 'Failed' || document.ragStatus === 'DeletionFailed') &&
    Boolean(document.ragErrorMessage?.trim());

  return (
    <div className="document-list__status">
      <StatusPill tone={getStatusTone(document.ragStatus)}>{statusText}</StatusPill>
      {document.ragStatus === 'Processing' ? (
        <div className="document-list__progress" role="progressbar" aria-label={`Progress ${progress}%`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={progress}>
          <span style={{ width: `${progress}%` }} />
        </div>
      ) : null}
      {shouldShowError ? (
        <p className="document-list__status-error">
          Error: {getShortErrorMessage(document.ragErrorMessage)}
        </p>
      ) : null}
      {document.ragAddedTime ? (
        <p className="document-list__status-detail">
          Added Time: {formatDateTime(document.ragAddedTime)}
        </p>
      ) : null}
    </div>
  );
}

function getStatusTone(status: MarkdownDocumentDto['ragStatus']): React.ComponentProps<typeof StatusPill>['tone'] {
  if (status === 'Completed') {
    return 'success';
  }

  if (status === 'Failed' || status === 'DeletionFailed') {
    return 'danger';
  }

  if (status === 'Queued' || status === 'Processing' || status === 'Pending' || status === 'Deleting') {
    return 'warning';
  }

  return 'neutral';
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

function getStatusFromLocation(): string {
  return normalizeStatus(new URLSearchParams(window.location.search).get('status') ?? '');
}

function normalizeStatus(value: string): string {
  const matchingStatus = statusOptions.find((option) => option.toLowerCase() === value.toLowerCase());
  return matchingStatus && statusOptionSet.has(matchingStatus) ? matchingStatus : '';
}

function syncStatusToUrl(status: string) {
  const nextUrl = status.length > 0 ? `/documents?status=${encodeURIComponent(status)}` : '/documents';
  const currentUrl = `${window.location.pathname}${window.location.search}`;

  if (currentUrl !== nextUrl) {
    window.history.pushState({}, '', nextUrl);
  }
}

function applyPipelinePatch(document: MarkdownDocumentDto, result: DocumentPipelineActionResult, action: 'retry' | 'cancel'): MarkdownDocumentDto {
  return {
    ...document,
    ragStatus: result.status,
    ragCurrentStage: null,
    ragErrorMessage: null,
    ragProgress: result.status === 'Processing' ? document.ragProgress : 0,
    activeRagTaskId: action === 'cancel' ? null : document.activeRagTaskId
  };
}

function applyTaskUpdateToDocument(document: MarkdownDocumentDto, update: TaskStatusUpdate): MarkdownDocumentDto {
  if (update.operationType === 'DeleteDocument') {
    return {
      ...document,
      ragStatus: update.status === 'Failed' ? 'DeletionFailed' : 'Deleting',
      ragErrorMessage: update.errorMessage ?? null,
      ragCurrentStage: update.currentStage ?? null,
      ragProgress: update.progress
    };
  }

  return {
    ...document,
    ragStatus: update.status,
    ragCurrentStage: update.currentStage ?? null,
    ragProgress: update.progress,
    ragErrorMessage: update.errorMessage ?? (update.status === 'Failed' ? document.ragErrorMessage : null),
    isInRagSystem: update.status === 'Completed' ? true : document.isInRagSystem,
    ragAddedTime: update.status === 'Completed'
      ? update.completedAt ?? document.ragAddedTime ?? new Date().toISOString()
      : document.ragAddedTime,
    activeRagTaskId: update.status === 'Completed' || update.status === 'Failed' || update.status === 'Cancelled'
      ? null
      : document.activeRagTaskId
  };
}
