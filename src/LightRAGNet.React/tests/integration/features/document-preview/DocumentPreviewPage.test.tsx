import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DocumentPreviewPage } from '@/features/document-preview/DocumentPreviewPage';

function deferred<T>() {
  let resolve: (value: T) => void = () => {};
  let reject: (reason?: unknown) => void = () => {};
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

describe('DocumentPreviewPage', () => {
  it('renders a safe empty state when no document id is selected', () => {
    render(<DocumentPreviewPage apiBase="http://localhost:5261" />);

    expect(screen.getByRole('heading', { name: 'Document Preview' })).toBeInTheDocument();
    expect(screen.getByText('Open a document from Documents or a RAG Chat reference.')).toBeInTheDocument();
    expect(screen.queryByText('Loading preview')).not.toBeInTheDocument();
  });

  it('renders markdown content from the safe preview API', async () => {
    const loadPreview = vi.fn().mockResolvedValue({
      contentType: 'text/markdown',
      content: '# Preview\n\nRendered **markdown** content.',
      fileName: 'preview.md'
    });

    render(<DocumentPreviewPage apiBase="http://localhost:5261" documentId={42} loadPreview={loadPreview} />);

    expect(await screen.findByRole('heading', { name: 'Preview' })).toBeInTheDocument();
    expect(screen.getByText('preview.md')).toBeInTheDocument();
    expect(screen.getByText(/Rendered/)).toBeInTheDocument();
    expect(screen.getByText('markdown')).toBeInTheDocument();
    expect(loadPreview).toHaveBeenCalledWith('http://localhost:5261', 42);
  });

  it('renders visible errors from the safe preview API', async () => {
    const loadPreview = vi.fn().mockRejectedValue(new Error('Preview is unavailable'));

    render(<DocumentPreviewPage apiBase="http://localhost:5261" documentId={42} loadPreview={loadPreview} />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Preview is unavailable');
  });

  it('ignores stale preview responses when the document id changes', async () => {
    const firstPreview = deferred<{ contentType: string; content: string; fileName: string }>();
    const secondPreview = deferred<{ contentType: string; content: string; fileName: string }>();
    const loadPreview = vi.fn()
      .mockReturnValueOnce(firstPreview.promise)
      .mockReturnValueOnce(secondPreview.promise);

    const { rerender } = render(
      <DocumentPreviewPage apiBase="http://localhost:5261" documentId={1} loadPreview={loadPreview} />
    );

    rerender(<DocumentPreviewPage apiBase="http://localhost:5261" documentId={2} loadPreview={loadPreview} />);

    secondPreview.resolve({
      contentType: 'text/markdown',
      content: '# Second Preview',
      fileName: 'second.md'
    });
    firstPreview.resolve({
      contentType: 'text/markdown',
      content: '# First Preview',
      fileName: 'first.md'
    });

    expect(await screen.findByRole('heading', { name: 'Second Preview' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'First Preview' })).not.toBeInTheDocument();
    expect(screen.queryByText('first.md')).not.toBeInTheDocument();
  });

  it('clears a previous error when loading a later document succeeds', async () => {
    const loadPreview = vi.fn()
      .mockRejectedValueOnce(new Error('First preview failed'))
      .mockResolvedValueOnce({
        contentType: 'text/markdown',
        content: '# Recovered Preview',
        fileName: 'recovered.md'
      });

    const { rerender } = render(
      <DocumentPreviewPage apiBase="http://localhost:5261" documentId={1} loadPreview={loadPreview} />
    );

    expect(await screen.findByRole('alert')).toHaveTextContent('First preview failed');

    rerender(<DocumentPreviewPage apiBase="http://localhost:5261" documentId={2} loadPreview={loadPreview} />);

    expect(await screen.findByRole('heading', { name: 'Recovered Preview' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
