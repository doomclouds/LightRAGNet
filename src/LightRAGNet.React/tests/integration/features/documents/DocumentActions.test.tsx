import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DocumentsPage } from '@/features/documents/DocumentsPage';
import type {
  DocumentPipelineActionResult,
  MarkdownDocumentDeleteClientResult,
  MarkdownDocumentDto,
  PagedResult
} from '@/features/documents/documentTypes';

const apiBase = 'http://test-api/';

function makeDocument(overrides: Partial<MarkdownDocumentDto> = {}): MarkdownDocumentDto {
  return {
    id: 1,
    fileName: 'handbook.md',
    fileSize: 2048,
    uploadTime: '2026-05-24T08:30:00Z',
    isInRagSystem: false,
    ragStatus: null,
    ragProgress: 0,
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

function renderDocuments(
  items: MarkdownDocumentDto[],
  actions: Partial<React.ComponentProps<typeof DocumentsPage>> = {}
) {
  const loadDocuments = vi.fn().mockResolvedValue(paged(items));

  render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} {...actions} />);

  return { loadDocuments };
}

describe('Document actions', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('adds an eligible document to RAG and updates the row to Pending', async () => {
    const user = userEvent.setup();
    const addToRag = vi.fn().mockResolvedValue(
      makeDocument({
        isInRagSystem: true,
        ragStatus: 'Pending',
        ragCurrentStage: 'Queued',
        ragProgress: 5
      })
    );

    renderDocuments([makeDocument()], { addToRag });

    const row = await screen.findByRole('row', { name: /handbook\.md/i });
    await user.click(within(row).getByRole('button', { name: 'Add handbook.md to RAG' }));

    await waitFor(() => expect(addToRag).toHaveBeenCalledWith(apiBase, 1));
    expect(await screen.findByText('Pending / Queued')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add handbook.md to RAG' })).not.toBeInTheDocument();
  });

  it('guards row actions while add is pending and ignores double clicks', async () => {
    const user = userEvent.setup();
    const addRequest = deferred<MarkdownDocumentDto>();
    const addToRag = vi.fn().mockReturnValue(addRequest.promise);
    const removeDocument = vi.fn().mockResolvedValue({ succeeded: true, deletedImmediately: true } satisfies MarkdownDocumentDeleteClientResult);

    renderDocuments([makeDocument()], { addToRag, removeDocument });

    const row = await screen.findByRole('row', { name: /handbook\.md/i });
    await user.dblClick(within(row).getByRole('button', { name: 'Add handbook.md to RAG' }));

    expect(addToRag).toHaveBeenCalledTimes(1);
    expect(within(row).getByRole('button', { name: 'Add handbook.md to RAG' })).toBeDisabled();
    expect(within(row).getByRole('button', { name: 'Delete handbook.md' })).toBeDisabled();

    addRequest.resolve(makeDocument({ isInRagSystem: true, ragStatus: 'Pending' }));

    expect(await screen.findByText('Pending')).toBeInTheDocument();
    expect(removeDocument).not.toHaveBeenCalled();
  });

  it('opens a preview panel with safe preview API content and safe download links only', async () => {
    const user = userEvent.setup();
    const loadPreview = vi.fn()
      .mockResolvedValueOnce(
        {
          contentType: 'text/markdown',
          content: '# Document Title\n\n| A | B |\n| - | - |\n| one | two |',
          fileName: 'safe-relative.md'
        }
      )
      .mockResolvedValueOnce(
        {
          contentType: 'text/markdown',
          content: 'Hidden download link',
          fileName: 'unsafe-upload.md'
        }
      )
      .mockResolvedValueOnce(
        {
          contentType: 'text/markdown',
          content: 'Absolute download link',
          fileName: 'safe-absolute.md'
        }
      )
      .mockResolvedValueOnce(
        {
          contentType: 'text/markdown',
          content: 'External download link',
          fileName: 'external-absolute.md'
        }
      );

    renderDocuments([
      makeDocument({ id: 1, fileName: 'safe-relative.md', fileUrl: '/uploads/safe-relative.md', content: 'Stale row content' }),
      makeDocument({ id: 2, fileName: 'unsafe-upload.md', fileUrl: 'upload://internal/unsafe-upload.md' }),
      makeDocument({ id: 3, fileName: 'safe-absolute.md', fileUrl: 'http://test-api/uploads/safe-absolute.md' }),
      makeDocument({ id: 4, fileName: 'external-absolute.md', fileUrl: 'https://cdn.example.com/external-absolute.md' })
    ], {
      loadPreview
    });

    await user.click(await screen.findByRole('button', { name: 'View safe-relative.md' }));

    expect(await screen.findByRole('heading', { name: 'Document Title' })).toBeInTheDocument();
    expect(screen.queryByText('Stale row content')).not.toBeInTheDocument();
    expect(within(screen.getByLabelText('Preview safe-relative.md')).getByRole('link', { name: 'Download safe-relative.md' })).toHaveAttribute(
      'href',
      'http://test-api/uploads/safe-relative.md'
    );
    expect(within(screen.getByLabelText('Preview safe-relative.md')).getByRole('table')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Close preview' }));
    await user.click(screen.getByRole('button', { name: 'View unsafe-upload.md' }));

    expect(await screen.findByText('Hidden download link')).toBeInTheDocument();
    expect(within(screen.getByLabelText('Preview unsafe-upload.md')).queryByRole('link', { name: 'Download unsafe-upload.md' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Close preview' }));
    await user.click(screen.getByRole('button', { name: 'View safe-absolute.md' }));

    expect(await screen.findByText('Absolute download link')).toBeInTheDocument();
    expect(within(screen.getByLabelText('Preview safe-absolute.md')).getByRole('link', { name: 'Download safe-absolute.md' })).toHaveAttribute(
      'href',
      'http://test-api/uploads/safe-absolute.md'
    );

    await user.click(screen.getByRole('button', { name: 'Close preview' }));
    await user.click(screen.getByRole('button', { name: 'View external-absolute.md' }));

    expect(await screen.findByText('External download link')).toBeInTheDocument();
    expect(within(screen.getByLabelText('Preview external-absolute.md')).queryByRole('link', { name: 'Download external-absolute.md' })).not.toBeInTheDocument();
    expect(loadPreview).toHaveBeenCalledTimes(4);
  });

  it('renders safe download links in the document list only', async () => {
    renderDocuments([
      makeDocument({ id: 1, fileName: 'safe-relative.md', fileUrl: '/uploads/safe-relative.md' }),
      makeDocument({ id: 2, fileName: 'safe-absolute.md', fileUrl: 'http://test-api/uploads/safe-absolute.md' }),
      makeDocument({ id: 3, fileName: 'unsafe-upload.md', fileUrl: 'upload://internal/unsafe-upload.md' }),
      makeDocument({ id: 4, fileName: 'external-absolute.md', fileUrl: 'https://cdn.example.com/external-absolute.md' })
    ]);

    expect(await screen.findByRole('link', { name: 'Download safe-relative.md' })).toHaveAttribute(
      'href',
      'http://test-api/uploads/safe-relative.md'
    );
    expect(screen.getByRole('link', { name: 'Download safe-absolute.md' })).toHaveAttribute(
      'href',
      'http://test-api/uploads/safe-absolute.md'
    );
    expect(screen.queryByRole('link', { name: 'Download unsafe-upload.md' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Download external-absolute.md' })).not.toBeInTheDocument();
  });

  it('removes a row when delete completes immediately', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const removeDocument = vi.fn().mockResolvedValue({ succeeded: true, deletedImmediately: true } satisfies MarkdownDocumentDeleteClientResult);
    const loadDocuments = vi
      .fn()
      .mockResolvedValueOnce(paged([makeDocument()]))
      .mockResolvedValueOnce(paged([]));

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} removeDocument={removeDocument} />);

    await user.click(await screen.findByRole('button', { name: 'Delete handbook.md' }));

    await waitFor(() => expect(removeDocument).toHaveBeenCalledWith(apiBase, 1));
    await waitFor(() => expect(screen.queryByText('handbook.md')).not.toBeInTheDocument());
  });

  it('reloads the effective page after immediate delete and avoids invalid pagination', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const removeDocument = vi.fn().mockResolvedValue({ succeeded: true, deletedImmediately: true } satisfies MarkdownDocumentDeleteClientResult);
    const loadDocuments = vi
      .fn()
      .mockResolvedValueOnce(paged([makeDocument({ id: 1, fileName: 'first-page.md' })], { page: 1, totalCount: 11, totalPages: 2 }))
      .mockResolvedValueOnce(paged([makeDocument({ id: 11, fileName: 'last-on-page.md' })], { page: 2, totalCount: 11, totalPages: 2 }))
      .mockResolvedValueOnce(paged([makeDocument({ id: 1, fileName: 'filled-page-one.md' })], { page: 1, totalCount: 10, totalPages: 1 }));

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} removeDocument={removeDocument} />);

    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, { page: 2, pageSize: 10, status: undefined }));

    await user.click(await screen.findByRole('button', { name: 'Delete last-on-page.md' }));

    await waitFor(() => expect(loadDocuments).toHaveBeenLastCalledWith(apiBase, { page: 1, pageSize: 10, status: undefined }));
    expect(await screen.findByText('Page 1 of 1')).toBeInTheDocument();
    expect(screen.queryByText('Page 2 of 1')).not.toBeInTheDocument();
    expect(await screen.findByText('filled-page-one.md')).toBeInTheDocument();
  });

  it('restores the row and shows an error when delete conflicts or fails', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const removeDocument = vi
      .fn()
      .mockResolvedValueOnce({ conflict: true, errorMessage: 'Document has an active pipeline' } satisfies MarkdownDocumentDeleteClientResult)
      .mockRejectedValueOnce(new Error('Delete request failed'));

    renderDocuments([makeDocument()], { removeDocument });

    await user.click(await screen.findByRole('button', { name: 'Delete handbook.md' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Document has an active pipeline');
    expect(screen.getByRole('row', { name: /handbook\.md/i })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Delete handbook.md' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Delete request failed');
    expect(screen.getByRole('row', { name: /handbook\.md/i })).toBeInTheDocument();
  });

  it('does not overwrite concurrent row updates when delete rollback completes', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const deleteRequest = deferred<MarkdownDocumentDeleteClientResult>();
    const removeDocument = vi.fn().mockReturnValue(deleteRequest.promise);
    const loadDocuments = vi
      .fn()
      .mockResolvedValueOnce(paged([makeDocument({ fileName: 'handbook.md', ragStatus: null })]))
      .mockResolvedValueOnce(paged([makeDocument({ fileName: 'handbook.md', isInRagSystem: true, ragStatus: 'Processing', ragCurrentStage: 'Embedding', ragProgress: 60 })]));

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} removeDocument={removeDocument} />);

    await user.click(await screen.findByRole('button', { name: 'Delete handbook.md' }));
    await user.selectOptions(screen.getByLabelText('RAG status filter'), 'Processing');

    expect(await screen.findByText('Processing / Embedding')).toBeInTheDocument();

    deleteRequest.resolve({ conflict: true, errorMessage: 'Document has an active pipeline' });

    expect(await screen.findByRole('alert')).toHaveTextContent('Document has an active pipeline');
    expect(screen.getByText('Processing / Embedding')).toBeInTheDocument();
  });

  it('shows retry and cancel for eligible statuses and updates action state', async () => {
    const user = userEvent.setup();
    const retry = vi.fn().mockResolvedValue({
      accepted: true,
      documentId: 1,
      status: 'Pending',
      message: 'Retry queued'
    } satisfies DocumentPipelineActionResult);
    const cancelPipeline = vi.fn().mockResolvedValue({
      accepted: true,
      documentId: 2,
      status: 'Cancelled',
      message: 'Cancel accepted'
    } satisfies DocumentPipelineActionResult);

    renderDocuments([
      makeDocument({ id: 1, fileName: 'failed.md', isInRagSystem: true, ragStatus: 'Failed', ragErrorMessage: 'Boom' }),
      makeDocument({ id: 2, fileName: 'processing.md', isInRagSystem: true, ragStatus: 'Processing', ragCurrentStage: 'Embedding', ragProgress: 25, activeRagTaskId: 'task-2' })
    ], {
      retry,
      cancelPipeline
    });

    await user.click(await screen.findByRole('button', { name: 'Retry failed.md' }));

    expect(await screen.findByText('Pending')).toBeInTheDocument();
    expect(screen.queryByText('Retry queued')).not.toBeInTheDocument();
    expect(screen.queryByText('Boom')).not.toBeInTheDocument();
    expect(retry).toHaveBeenCalledWith(apiBase, 1);

    await user.click(screen.getByRole('button', { name: 'Cancel processing.md' }));

    const cancelledRow = await screen.findByRole('row', { name: /processing\.md/i });
    expect(within(cancelledRow).getByText('Cancelled')).toBeInTheDocument();
    expect(within(cancelledRow).getByRole('button', { name: 'Delete processing.md' })).toBeEnabled();
    expect(screen.queryByText('Cancel accepted')).not.toBeInTheDocument();
    expect(cancelPipeline).toHaveBeenCalledWith(apiBase, 2);
  });

  it('disables delete for busy documents', async () => {
    renderDocuments([makeDocument({ fileName: 'busy.md', isInRagSystem: true, ragStatus: 'Processing' })]);

    const row = await screen.findByRole('row', { name: /busy\.md/i });

    expect(within(row).getByRole('button', { name: 'Delete busy.md' })).toBeDisabled();
  });
});
