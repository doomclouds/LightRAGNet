import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { DocumentsPage } from '@/features/documents/DocumentsPage';
import type { MarkdownDocumentDto, PagedResult } from '@/features/documents/documentTypes';

const apiBase = 'http://localhost:5261';

function makeDocument(overrides: Partial<MarkdownDocumentDto> = {}): MarkdownDocumentDto {
  return {
    id: 42,
    fileName: 'system-architecture.md',
    fileSize: 2048,
    uploadTime: '2026-05-24T08:30:00Z',
    isInRagSystem: true,
    ragStatus: 'Completed',
    ragProgress: 100,
    ragRetryCount: 0,
    ...overrides
  };
}

function paged(items: MarkdownDocumentDto[]): PagedResult<MarkdownDocumentDto> {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 10,
    totalPages: 1
  };
}

describe('DocumentPreviewDrawer', () => {
  it('loads drawer content through the safe preview API and keeps the full preview route', async () => {
    const user = userEvent.setup();
    const loadDocuments = vi.fn().mockResolvedValue(paged([makeDocument({ content: 'Stale row content' })]));
    const loadPreview = vi.fn().mockResolvedValue({
      contentType: 'text/markdown',
      content: '# Architecture\n\nLoaded through preview API.',
      fileName: 'system-architecture.md'
    });

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} loadPreview={loadPreview} />);

    await user.click(await screen.findByRole('button', { name: 'View system-architecture.md' }));

    const dialog = await screen.findByRole('dialog', { name: 'Preview system-architecture.md' });
    expect(dialog).toBeInTheDocument();
    expect(await within(dialog).findByRole('heading', { name: 'Architecture' })).toBeInTheDocument();
    expect(within(dialog).queryByText('Stale row content')).not.toBeInTheDocument();
    expect(within(dialog).getByRole('link', { name: 'Open full preview' })).toHaveAttribute(
      'href',
      '/document-preview/42'
    );
    await waitFor(() => expect(loadPreview).toHaveBeenCalledWith(apiBase, 42));
  });
});
