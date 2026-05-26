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

function deferred<T>() {
  let resolve: (value: T) => void = () => {};
  let reject: (reason?: unknown) => void = () => {};
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
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
    expect(within(dialog).getByRole('tab', { name: 'Preview' })).toHaveAttribute('aria-selected', 'true');
    expect(within(dialog).getByRole('tab', { name: 'Metadata' })).toBeInTheDocument();
    expect(within(dialog).getByText('Uploaded Time')).toBeInTheDocument();
    expect(within(dialog).getByText('RAG Status')).toBeInTheDocument();
    expect(within(dialog).getByText('Chunks')).toBeInTheDocument();
    expect(within(dialog).getByRole('region', { name: 'Content Preview' })).toBeInTheDocument();
    expect(await within(dialog).findByRole('heading', { name: 'Architecture' })).toBeInTheDocument();
    expect(within(dialog).queryByText('Stale row content')).not.toBeInTheDocument();
    expect(within(dialog).getByRole('link', { name: 'Open full preview' })).toHaveAttribute(
      'href',
      '/document-preview/42'
    );
    await waitFor(() => expect(loadPreview).toHaveBeenCalledWith(apiBase, 42));
  });

  it('ignores late preview responses after the drawer is closed', async () => {
    const user = userEvent.setup();
    const preview = deferred<{ contentType: string; content: string; fileName: string }>();
    const loadDocuments = vi.fn().mockResolvedValue(paged([makeDocument()]));
    const loadPreview = vi.fn().mockReturnValue(preview.promise);

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} loadPreview={loadPreview} />);

    await user.click(await screen.findByRole('button', { name: 'View system-architecture.md' }));
    await user.click(await screen.findByRole('button', { name: 'Close preview' }));

    preview.resolve({
      contentType: 'text/markdown',
      content: '# Late Preview',
      fileName: 'late.md'
    });

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.queryByRole('heading', { name: 'Late Preview' })).not.toBeInTheDocument();
  });

  it('keeps quick document switches from rendering stale drawer content', async () => {
    const user = userEvent.setup();
    const firstPreview = deferred<{ contentType: string; content: string; fileName: string }>();
    const secondPreview = deferred<{ contentType: string; content: string; fileName: string }>();
    const loadDocuments = vi.fn().mockResolvedValue(paged([
      makeDocument({ id: 1, fileName: 'first.md' }),
      makeDocument({ id: 2, fileName: 'second.md' })
    ]));
    const loadPreview = vi.fn()
      .mockReturnValueOnce(firstPreview.promise)
      .mockReturnValueOnce(secondPreview.promise);

    render(<DocumentsPage apiBase={apiBase} loadDocuments={loadDocuments} loadPreview={loadPreview} />);

    await user.click(await screen.findByRole('button', { name: 'View first.md' }));
    await user.click(await screen.findByRole('button', { name: 'View second.md' }));

    secondPreview.resolve({
      contentType: 'text/markdown',
      content: '# Second Drawer',
      fileName: 'second.md'
    });
    firstPreview.resolve({
      contentType: 'text/markdown',
      content: '# First Drawer',
      fileName: 'first.md'
    });

    const dialog = await screen.findByRole('dialog', { name: 'Preview second.md' });
    expect(await within(dialog).findByRole('heading', { name: 'Second Drawer' })).toBeInTheDocument();
    expect(within(dialog).queryByRole('heading', { name: 'First Drawer' })).not.toBeInTheDocument();
  });
});
