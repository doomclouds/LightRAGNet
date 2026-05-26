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
    pageSize: 20,
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
        pageSize: 20,
        status: undefined
      })
    );

    expect(screen.getByRole('heading', { name: 'Documents' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Upload Document' })).toHaveAttribute('href', '/documents/upload');
    expect(screen.getByRole('columnheader', { name: 'File Name' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Size' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Uploaded Time' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'RAG Status' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Progress' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Actions' })).toBeInTheDocument();

    const row = screen.getByRole('row', { name: /handbook\.md/i });
    expect(within(row).getByText('2.0 KB')).toBeInTheDocument();
    expect(within(row).getByText('Indexed')).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: 'View handbook.md' })).toBeInTheDocument();
    expect(within(row).queryByRole('button', { name: 'Add handbook.md to RAG' })).not.toBeInTheDocument();
    expect(within(row).getByRole('button', { name: 'More actions for handbook.md' })).toBeInTheDocument();
  });

  it('renders the light document workbench visual contract', async () => {
    const loadDocuments = vi.fn().mockResolvedValue(
      paged([
        makeDocument({
          fileName: 'LightRAGNet_Architecture_Overview.pdf',
          fileSize: 12_400_000,
          fileUrl: '/uploads/LightRAGNet_Architecture_Overview.pdf',
          originalContentType: 'application/pdf',
          ragStatus: 'Completed',
          ragProgress: 100
        }),
        makeDocument({
          id: 2,
          fileName: 'Product_Requirements.docx',
          fileSize: 2_100_000,
          originalContentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
          ragStatus: 'Processing',
          ragCurrentStage: 'Embedding',
          ragProgress: 65
        }),
        makeDocument({
          id: 3,
          fileName: 'API_Reference_Guide.pdf',
          fileSize: 5_300_000,
          originalContentType: 'application/pdf',
          ragStatus: 'Failed',
          ragProgress: 0,
          ragErrorMessage: 'Parsing failed'
        })
      ], { totalCount: 1248, totalPages: 63 })
    );

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    expect(await screen.findByRole('heading', { name: 'Documents' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Upload Document/i })).toHaveClass('document-list__upload-link');

    const statusViews = screen.getByRole('navigation', { name: 'Document status views' });
    expect(within(statusViews).getByRole('link', { name: /All/i })).toBeInTheDocument();
    expect(within(statusViews).getByRole('link', { name: /Indexed/i })).toBeInTheDocument();
    expect(within(statusViews).getByRole('link', { name: 'Processing' })).toBeInTheDocument();
    expect(within(statusViews).getByRole('link', { name: 'Failed' })).toBeInTheDocument();
    const summary = screen.getByRole('region', { name: 'Document summary' });
    expect(summary).toHaveClass('document-list__summary-grid');
    expect(within(summary).getByText('Total Documents')).toBeInTheDocument();
    expect(within(summary).getByText('Indexed')).toBeInTheDocument();
    expect(within(summary).getByText('Processing')).toBeInTheDocument();
    expect(within(summary).getByText('Failed')).toBeInTheDocument();
    expect(within(summary).getByText('Total Size')).toBeInTheDocument();

    const toolbar = screen.getByRole('toolbar', { name: 'Document table tools' });
    expect(within(toolbar).getByRole('searchbox', { name: 'Search documents' })).toBeInTheDocument();
    expect(within(toolbar).getByLabelText('File type filter')).toBeInTheDocument();
    expect(within(toolbar).getByLabelText('RAG status filter')).toBeInTheDocument();
    expect(within(toolbar).getByLabelText('Tag filter')).toBeInTheDocument();
    expect(within(toolbar).getByRole('button', { name: 'More filters' })).toBeInTheDocument();
    expect(within(toolbar).getByRole('button', { name: 'Refresh documents' })).toBeInTheDocument();

    const table = await screen.findByRole('table', { name: 'Document lifecycle' });
    expect(table).toHaveClass('lrn-data-table');
    expect(screen.getByRole('columnheader', { name: 'Size' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Uploaded Time' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Progress' })).toBeInTheDocument();
    const row = within(table).getByRole('row', { name: /LightRAGNet_Architecture_Overview\.pdf/i });
    expect(within(row).getByText('Indexed')).toBeInTheDocument();
    expect(within(row).getAllByText('PDF').length).toBeGreaterThan(0);
    expect(within(row).getByRole('progressbar', { name: 'Progress 100%' })).toHaveClass('lrn-progress');
    expect(within(row).getByRole('button', { name: 'View LightRAGNet_Architecture_Overview.pdf' })).toHaveClass('lrn-icon-button');
    expect(within(row).getByRole('link', { name: 'Download LightRAGNet_Architecture_Overview.pdf' })).toHaveClass('lrn-icon-link');
    expect(within(row).getByRole('button', { name: 'More actions for LightRAGNet_Architecture_Overview.pdf' })).toHaveClass('lrn-action-menu__trigger');
    expect(
      within(row).getAllByText('PDF').find((element) => element.closest('.lrn-file-type-icon'))?.closest('.lrn-file-type-icon')
    ).toHaveClass('lrn-file-type-icon--pdf');
    expect(
      within(table).getAllByText('DOCX').find((element) => element.closest('.lrn-file-type-icon'))?.closest('.lrn-file-type-icon')
    ).toHaveClass('lrn-file-type-icon--docx');
    expect(screen.getByText('Showing 1 to 3 of 1,248 results')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go to page 1' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('button', { name: 'Go to page 2' })).toBeInTheDocument();
    expect(screen.getByText('...')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go to page 63' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Rows per page' })).toHaveValue('20');
  });

  it('opens document previews in a same-page drawer without leaving the list route', async () => {
    const user = userEvent.setup();
    window.history.pushState({}, '', '/documents');
    const loadDocuments = vi.fn().mockResolvedValue(
      paged([makeDocument({ fileName: 'system-architecture.md' })])
    );
    const loadPreview = vi.fn().mockResolvedValue({
      contentType: 'text/markdown',
      content: '# Architecture',
      fileName: 'system-architecture.md'
    });

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} loadPreview={loadPreview} />);

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

  it('moves focus into the preview drawer and returns focus to the triggering view button on Escape', async () => {
    const user = userEvent.setup();
    const loadDocuments = vi.fn().mockResolvedValue(
      paged([makeDocument({ fileName: 'system-architecture.md' })])
    );
    const loadPreview = vi.fn().mockResolvedValue({
      contentType: 'text/markdown',
      content: '# Architecture',
      fileName: 'system-architecture.md'
    });

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} loadPreview={loadPreview} />);

    const viewButton = await screen.findByRole('button', { name: 'View system-architecture.md' });
    await user.click(viewButton);

    const dialog = await screen.findByRole('dialog', { name: 'Preview system-architecture.md' });
    const closeButton = within(dialog).getByRole('button', { name: 'Close preview' });

    await waitFor(() => expect(closeButton).toHaveFocus());

    await user.keyboard('{Escape}');

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Preview system-architecture.md' })).not.toBeInTheDocument());
    expect(viewButton).toHaveFocus();
  });

  it('traps tab focus inside the preview drawer while it is open', async () => {
    const user = userEvent.setup();
    const loadDocuments = vi.fn().mockResolvedValue(
      paged([makeDocument({ fileName: 'system-architecture.md' })])
    );
    const loadPreview = vi.fn().mockResolvedValue({
      contentType: 'text/markdown',
      content: '# Architecture',
      fileName: 'system-architecture.md'
    });

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} loadPreview={loadPreview} />);

    await user.click(await screen.findByRole('button', { name: 'View system-architecture.md' }));

    const dialog = await screen.findByRole('dialog', { name: 'Preview system-architecture.md' });
    const closeButton = within(dialog).getByRole('button', { name: 'Close preview' });
    const previewTab = within(dialog).getByRole('tab', { name: 'Preview' });
    const previousPageButton = screen.getByRole('button', { name: 'Previous page' });

    await waitFor(() => expect(closeButton).toHaveFocus());

    await user.tab();

    expect(previewTab).toHaveFocus();
    expect(previousPageButton).not.toHaveFocus();

    await user.tab({ shift: true });

    expect(closeButton).toHaveFocus();
    expect(previousPageButton).not.toHaveFocus();
  });

  it('loads the status query filter from the documents URL and marks controls active', async () => {
    window.history.pushState({}, '', '/documents?status=Failed');
    const loadDocuments = vi.fn().mockResolvedValue(
      paged([makeDocument({ fileName: 'failed.md', ragStatus: 'Failed' })])
    );

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    await waitFor(() =>
      expect(loadDocuments).toHaveBeenCalledWith(apiBase, {
        page: 1,
        pageSize: 20,
        status: 'Failed'
      })
    );

    expect(screen.getByLabelText('RAG status filter')).toHaveValue('Failed');
    expect(screen.getByRole('link', { name: 'Failed' })).toHaveAttribute('aria-current', 'page');
  });

  it('keeps status tabs, select, and the URL query in sync without leaving the page', async () => {
    const user = userEvent.setup();
    const loadDocuments = vi.fn().mockResolvedValue(paged([makeDocument()]));

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    await waitFor(() => expect(loadDocuments).toHaveBeenCalledTimes(1));

    await user.click(screen.getByRole('link', { name: 'Processing' }));

    await waitFor(() =>
      expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, {
        page: 1,
        pageSize: 20,
        status: 'Processing'
      })
    );
    expect(window.location.pathname).toBe('/documents');
    expect(window.location.search).toBe('?status=Processing');
    expect(screen.getByLabelText('RAG status filter')).toHaveValue('Processing');

    await user.selectOptions(screen.getByLabelText('RAG status filter'), 'Failed');

    await waitFor(() =>
      expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, {
        page: 1,
        pageSize: 20,
        status: 'Failed'
      })
    );
    expect(window.location.pathname).toBe('/documents');
    expect(window.location.search).toBe('?status=Failed');
    expect(screen.getByRole('link', { name: 'Failed' })).toHaveAttribute('aria-current', 'page');

    await user.click(screen.getByRole('link', { name: 'All Documents' }));

    await waitFor(() =>
      expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, {
        page: 1,
        pageSize: 20,
        status: undefined
      })
    );
    expect(window.location.pathname).toBe('/documents');
    expect(window.location.search).toBe('');
    expect(screen.getByLabelText('RAG status filter')).toHaveValue('');
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
        pageSize: 20,
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

    expect(await screen.findByText('No documents found')).toBeInTheDocument();

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

    const row = await screen.findByRole('row', { name: /pipeline\.md/i });
    expect(within(row).getByText('Processing')).toBeInTheDocument();
    expect(within(row).getByText('Embedding')).toBeInTheDocument();
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
    expect(within(deleteFailedRow).getByText('Failed')).toBeInTheDocument();
    expect(within(deleteFailedRow).getByText('Error: Vector store delete failed')).toBeInTheDocument();
  });

  it('loads next and previous pages from the table footer', async () => {
    const user = userEvent.setup();
    const loadDocuments = vi.fn().mockResolvedValue(paged([makeDocument()], { totalCount: 30, totalPages: 3 }));

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    expect(await screen.findByRole('button', { name: 'Go to page 1' })).toHaveAttribute('aria-current', 'page');

    await user.click(screen.getByRole('button', { name: 'Next page' }));
    await waitFor(() =>
      expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, {
        page: 2,
        pageSize: 20,
        status: undefined
      })
    );
    expect(await screen.findByRole('button', { name: 'Go to page 2' })).toHaveAttribute('aria-current', 'page');

    await user.click(screen.getByRole('button', { name: 'Previous page' }));
    await waitFor(() =>
      expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, {
        page: 1,
        pageSize: 20,
        status: undefined
      })
    );
  });

  it('changes the server page size from the footer selector and resets to the first page', async () => {
    const user = userEvent.setup();
    const loadDocuments = vi.fn().mockResolvedValue(paged([makeDocument()], { totalCount: 1248, totalPages: 63 }));

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} />);

    expect(await screen.findByRole('combobox', { name: 'Rows per page' })).toHaveValue('20');

    await user.click(screen.getByRole('button', { name: 'Next page' }));
    await waitFor(() =>
      expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, {
        page: 2,
        pageSize: 20,
        status: undefined
      })
    );

    await user.selectOptions(screen.getByRole('combobox', { name: 'Rows per page' }), '50');

    await waitFor(() =>
      expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, {
        page: 1,
        pageSize: 50,
        status: undefined
      })
    );
    expect(screen.getByRole('button', { name: 'Go to page 1' })).toHaveAttribute('aria-current', 'page');
  });
});
