import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DocumentPreviewPage } from '@/features/document-preview/DocumentPreviewPage';

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
});
