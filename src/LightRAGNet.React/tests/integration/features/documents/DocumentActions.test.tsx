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

  it('opens a preview panel with markdown content and safe download links only', async () => {
    const user = userEvent.setup();
    const loadDocument = vi.fn()
      .mockResolvedValueOnce(
        makeDocument({
          id: 1,
          fileName: 'safe-relative.md',
          content: '# Document Title\n\n| A | B |\n| - | - |\n| one | two |',
          fileUrl: '/uploads/safe-relative.md'
        })
      )
      .mockResolvedValueOnce(
        makeDocument({
          id: 2,
          fileName: 'unsafe-upload.md',
          content: 'Hidden download link',
          fileUrl: 'upload://internal/unsafe-upload.md'
        })
      )
      .mockResolvedValueOnce(
        makeDocument({
          id: 3,
          fileName: 'safe-absolute.md',
          content: 'Absolute download link',
          fileUrl: 'https://cdn.example.com/safe-absolute.md'
        })
      );

    renderDocuments([
      makeDocument({ id: 1, fileName: 'safe-relative.md' }),
      makeDocument({ id: 2, fileName: 'unsafe-upload.md' }),
      makeDocument({ id: 3, fileName: 'safe-absolute.md' })
    ], {
      loadDocument
    });

    await user.click(await screen.findByRole('button', { name: 'View safe-relative.md' }));

    expect(await screen.findByRole('heading', { name: 'Document Title' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download safe-relative.md' })).toHaveAttribute(
      'href',
      'http://test-api/uploads/safe-relative.md'
    );
    expect(within(screen.getByLabelText('Preview safe-relative.md')).getByRole('table')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Close preview' }));
    await user.click(screen.getByRole('button', { name: 'View unsafe-upload.md' }));

    expect(await screen.findByText('Hidden download link')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Download unsafe-upload.md' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Close preview' }));
    await user.click(screen.getByRole('button', { name: 'View safe-absolute.md' }));

    expect(await screen.findByText('Absolute download link')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download safe-absolute.md' })).toHaveAttribute(
      'href',
      'https://cdn.example.com/safe-absolute.md'
    );
    expect(loadDocument).toHaveBeenCalledTimes(3);
  });

  it('removes a row when delete completes immediately', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const removeDocument = vi.fn().mockResolvedValue({ succeeded: true, deletedImmediately: true } satisfies MarkdownDocumentDeleteClientResult);

    renderDocuments([makeDocument()], { removeDocument });

    await user.click(await screen.findByRole('button', { name: 'Delete handbook.md' }));

    await waitFor(() => expect(removeDocument).toHaveBeenCalledWith(apiBase, 1));
    expect(screen.queryByText('handbook.md')).not.toBeInTheDocument();
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

  it('shows retry and cancel for eligible statuses and updates action state', async () => {
    const user = userEvent.setup();
    const retry = vi.fn().mockResolvedValue({
      accepted: true,
      documentId: 1,
      status: 'Pending',
      currentStage: 'Queued',
      progress: 10,
      errorMessage: null
    } as DocumentPipelineActionResult & { currentStage: string; progress: number; errorMessage: null });
    const cancelPipeline = vi.fn().mockResolvedValue({
      accepted: true,
      documentId: 2,
      status: 'Cancelled',
      currentStage: 'Cancelled',
      progress: 25,
      errorMessage: 'Cancelled by user'
    } as DocumentPipelineActionResult & { currentStage: string; progress: number; errorMessage: string });

    renderDocuments([
      makeDocument({ id: 1, fileName: 'failed.md', isInRagSystem: true, ragStatus: 'Failed', ragErrorMessage: 'Boom' }),
      makeDocument({ id: 2, fileName: 'processing.md', isInRagSystem: true, ragStatus: 'Processing', ragCurrentStage: 'Embedding', ragProgress: 25 })
    ], {
      retry,
      cancelPipeline
    });

    await user.click(await screen.findByRole('button', { name: 'Retry failed.md' }));

    expect(await screen.findByText('Pending / Queued')).toBeInTheDocument();
    expect(retry).toHaveBeenCalledWith(apiBase, 1);

    await user.click(screen.getByRole('button', { name: 'Cancel processing.md' }));

    expect(await screen.findByText('Cancelled / Cancelled')).toBeInTheDocument();
    expect(cancelPipeline).toHaveBeenCalledWith(apiBase, 2);
  });

  it('disables delete for busy documents', async () => {
    renderDocuments([makeDocument({ fileName: 'busy.md', isInRagSystem: true, ragStatus: 'Processing' })]);

    const row = await screen.findByRole('row', { name: /busy\.md/i });

    expect(within(row).getByRole('button', { name: 'Delete busy.md' })).toBeDisabled();
  });
});
