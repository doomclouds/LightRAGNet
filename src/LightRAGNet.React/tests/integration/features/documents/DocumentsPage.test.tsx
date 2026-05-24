import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '@/app/App';
import { DocumentsPage } from '@/features/documents/DocumentsPage';
import type { MarkdownDocumentDto, PagedResult } from '@/features/documents/documentTypes';

const apiBase = 'http://test-api';

function makeDocument(overrides: Partial<MarkdownDocumentDto> = {}): MarkdownDocumentDto {
  return {
    id: 1,
    fileName: 'handbook.md',
    fileSize: 2048,
    uploadTime: '2026-05-24T08:30:00Z',
    isInRagSystem: true,
    ragStatus: 'Completed',
    ragProgress: 100,
    ragRetryCount: 0,
    ...overrides
  };
}

function paged(items: MarkdownDocumentDto[], overrides: Partial<PagedResult<MarkdownDocumentDto>> = {}): PagedResult<MarkdownDocumentDto> {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 10,
    totalPages: Math.max(1, items.length === 0 ? 0 : 1),
    ...overrides
  };
}

function deferred<T>() {
  let resolve: (value: T) => void = () => {};
  let reject: (reason?: unknown) => void = () => {};
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

async function findRowByText(text: string): Promise<HTMLElement> {
  const row = (await screen.findByText(text)).closest('tr');
  if (!row) {
    throw new Error(`Could not find row for ${text}`);
  }

  return row;
}

describe('DocumentsPage', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    window.history.pushState({}, '', '/documents');
  });

  it('renders the documents page at the documents route and keeps upload on the upload route', async () => {
    window.history.pushState({}, '', '/documents');
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(paged([makeDocument({ fileName: 'route-doc.md' })])), {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
      })
    );

    const { unmount } = render(<App />);

    expect(await screen.findByRole('heading', { name: 'Documents' })).toBeInTheDocument();
    expect(await screen.findByText('route-doc.md')).toBeInTheDocument();

    unmount();
    window.history.pushState({}, '', '/documents/upload');
    render(<App />);

    expect(screen.getByRole('heading', { name: 'Upload Document' })).toBeInTheDocument();
    expect(screen.getByLabelText('Choose documents')).toBeInTheDocument();
  });

  it('loads the first page and renders document rows with eligible actions', async () => {
    const loadDocuments = vi.fn().mockResolvedValue(paged([makeDocument()]));

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    await waitFor(() =>
      expect(loadDocuments).toHaveBeenCalledWith(apiBase, {
        page: 1,
        pageSize: 10,
        status: undefined
      })
    );

    expect(screen.getByRole('heading', { name: 'Documents' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Upload' })).toHaveAttribute('href', '/documents/upload');
    expect(screen.getByRole('columnheader', { name: 'File Name' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'File Size' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Upload Time' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'RAG Status' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Actions' })).toBeInTheDocument();

    const row = screen.getByRole('row', { name: /handbook\.md/i });
    expect(within(row).getByText('2.0 KB')).toBeInTheDocument();
    expect(within(row).getByText('Completed')).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: 'View handbook.md' })).toBeInTheDocument();
    expect(within(row).queryByRole('button', { name: 'Add handbook.md to RAG' })).not.toBeInTheDocument();
    expect(within(row).getByRole('button', { name: 'Delete handbook.md' })).toBeInTheDocument();
  });

  it('renders the dark document workbench visual contract', async () => {
    const loadDocuments = vi.fn().mockResolvedValue(
      paged([makeDocument({ fileName: 'system-architecture.md' })])
    );

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    expect(await screen.findByRole('heading', { name: 'Documents' })).toBeInTheDocument();

    const statusViews = screen.getByRole('navigation', { name: 'Document status views' });
    expect(within(statusViews).getByRole('link', { name: 'All Documents' })).toBeInTheDocument();
    expect(within(statusViews).getByRole('link', { name: 'Processing' })).toBeInTheDocument();

    const table = await screen.findByRole('table', { name: 'Document lifecycle' });
    const row = within(table).getByRole('row', { name: /system-architecture\.md/i });
    expect(within(row).getByText('Completed')).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: 'View system-architecture.md' })).toBeInTheDocument();
  });

  it('opens document previews in a same-page drawer without leaving the list route', async () => {
    const user = userEvent.setup();
    window.history.pushState({}, '', '/documents');
    const loadDocuments = vi.fn().mockResolvedValue(
      paged([makeDocument({ fileName: 'system-architecture.md' })])
    );
    const loadDocument = vi.fn().mockResolvedValue(
      makeDocument({
        fileName: 'system-architecture.md',
        content: '# Architecture'
      })
    );

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} loadDocument={loadDocument} />);

    await user.click(await screen.findByRole('button', { name: 'View system-architecture.md' }));

    const dialog = await screen.findByRole('dialog', { name: 'Preview system-architecture.md' });
    expect(within(dialog).getByRole('button', { name: 'Close preview' })).toBeInTheDocument();
    expect(within(dialog).getByRole('link', { name: 'Open full preview' })).toHaveAttribute(
      'href',
      '/document-preview/1'
    );
    expect(document.querySelector('.lrn-scrim')).toBeInTheDocument();
    expect(window.location.pathname).toBe('/documents');
  });

  it('reloads page one with the selected status filter', async () => {
    const user = userEvent.setup();
    const loadDocuments = vi.fn().mockResolvedValue(paged([makeDocument()]));

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    await waitFor(() => expect(loadDocuments).toHaveBeenCalledTimes(1));
    await user.selectOptions(screen.getByLabelText('RAG status filter'), 'Failed');

    await waitFor(() =>
      expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, {
        page: 1,
        pageSize: 10,
        status: 'Failed'
      })
    );
  });

  it('hides stale rows and actions while a status filter reload is pending', async () => {
    const user = userEvent.setup();
    const initialLoad = deferred<PagedResult<MarkdownDocumentDto>>();
    const filteredLoad = deferred<PagedResult<MarkdownDocumentDto>>();
    const loadDocuments = vi.fn()
      .mockReturnValueOnce(initialLoad.promise)
      .mockReturnValueOnce(filteredLoad.promise);

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    initialLoad.resolve(paged([makeDocument({ fileName: 'completed.md', ragStatus: 'Completed' })]));

    expect(await screen.findByText('completed.md')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('RAG status filter'), 'Failed');

    await waitFor(() => expect(loadDocuments).toHaveBeenCalledTimes(2));
    expect(screen.getByText('Loading documents...')).toBeInTheDocument();
    expect(screen.queryByText('completed.md')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'View completed.md' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add completed.md to RAG' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete completed.md' })).not.toBeInTheDocument();

    filteredLoad.resolve(paged([makeDocument({ fileName: 'failed.md', ragStatus: 'Failed' })]));

    expect(await screen.findByText('failed.md')).toBeInTheDocument();
  });

  it('shows loading, empty, and error states', async () => {
    const neverLoad = vi.fn(() => new Promise<PagedResult<MarkdownDocumentDto>>(() => {}));

    const { unmount } = render(<DocumentsPage apiBase={apiBase} loadDocuments={neverLoad} />);

    expect(screen.getByText('Loading documents...')).toBeInTheDocument();

    unmount();
    const emptyLoad = vi.fn().mockResolvedValue(paged([]));
    const emptyRender = render(<DocumentsPage apiBase={apiBase} loadDocuments={emptyLoad} />);

    expect(await screen.findByText('No documents found.')).toBeInTheDocument();

    emptyRender.unmount();
    const failedLoad = vi.fn().mockRejectedValue(new Error('Backend unavailable'));
    render(<DocumentsPage apiBase={apiBase} loadDocuments={failedLoad} />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Backend unavailable');
  });

  it('renders processing progress with stage details', async () => {
    const loadDocuments = vi.fn().mockResolvedValue(
      paged([
        makeDocument({
          fileName: 'pipeline.md',
          ragStatus: 'Processing',
          ragCurrentStage: 'Embedding',
          ragProgress: 60
        })
      ])
    );

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    expect(await screen.findByText('Processing / Embedding')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Progress 60%' })).toHaveAttribute('aria-valuenow', '60');
  });

  it('renders error summaries and added time in the status column', async () => {
    const loadDocuments = vi.fn().mockResolvedValue(
      paged([
        makeDocument({
          id: 1,
          fileName: 'failed.md',
          ragStatus: 'Failed',
          ragErrorMessage: 'Embedding provider timed out',
          ragAddedTime: '2026-05-24T09:40:00Z'
        }),
        makeDocument({
          id: 2,
          fileName: 'delete-failed.md',
          ragStatus: 'DeletionFailed',
          ragErrorMessage: 'Vector store delete failed'
        })
      ])
    );

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    const failedRow = await findRowByText('failed.md');
    expect(within(failedRow).getByText('Failed')).toBeInTheDocument();
    expect(within(failedRow).getByText('Error: Embedding provider timed out')).toBeInTheDocument();
    expect(within(failedRow).getByText(/Added Time:/)).toBeInTheDocument();

    const deleteFailedRow = await findRowByText('delete-failed.md');
    expect(within(deleteFailedRow).getByText('DeletionFailed')).toBeInTheDocument();
    expect(within(deleteFailedRow).getByText('Error: Vector store delete failed')).toBeInTheDocument();
  });

  it('loads next and previous pages from the table footer', async () => {
    const user = userEvent.setup();
    const loadDocuments = vi.fn().mockResolvedValue(paged([makeDocument()], { totalCount: 30, totalPages: 3 }));

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    expect(await screen.findByText('Page 1 of 3')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() =>
      expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, {
        page: 2,
        pageSize: 10,
        status: undefined
      })
    );
    expect(await screen.findByText('Page 2 of 3')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Previous' }));
    await waitFor(() =>
      expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, {
        page: 1,
        pageSize: 10,
        status: undefined
      })
    );
  });
});
