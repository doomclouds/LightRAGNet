import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { App } from '@/app/App';
import { UploadDocumentPage } from '@/features/documents/UploadDocumentPage';
import type { DocumentSubmissionResponse } from '@/features/documents/documentTypes';

const apiBase = 'http://test-api';

function makeFile(name: string, size: number, type = 'text/markdown'): File {
  const file = new File(['x'], name, { type });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

function successfulUpload(count: number): DocumentSubmissionResponse {
  return {
    trackId: 'track-1',
    documents: Array.from({ length: count }, (_, index) => ({
      id: index + 1,
      fileName: `doc-${index + 1}.md`,
      fileSize: 1024,
      uploadTime: '2026-05-24T00:00:00Z',
      isInRagSystem: false,
      ragProgress: 0,
      ragRetryCount: 0
    }))
  };
}

describe('UploadDocumentPage', () => {
  it('renders the upload page at the document upload route', () => {
    window.history.pushState({}, '', '/documents/upload');

    render(<App />);

    expect(screen.getByRole('heading', { name: 'Upload Document' })).toBeInTheDocument();
    expect(screen.getByLabelText('Choose documents')).toBeInTheDocument();

    window.history.pushState({}, '', '/documents');
  });

  it('rejects unsupported and oversized files before upload', async () => {
    const user = userEvent.setup();
    const uploadDocuments = vi.fn().mockResolvedValue(successfulUpload(0));

    render(<UploadDocumentPage apiBase={apiBase} uploadDocuments={uploadDocuments} />);

    const input = screen.getByLabelText('Choose documents');
    expect(input).toHaveAttribute('accept', '.md,.markdown,.pdf,.docx');

    fireEvent.change(input, {
      target: {
        files: [makeFile('notes.txt', 1024, 'text/plain'), makeFile('large.pdf', 10 * 1024 * 1024 + 1, 'application/pdf')]
      }
    });
    await user.click(screen.getByRole('button', { name: 'Upload' }));

    expect(screen.getByText(/Unsupported file type: notes\.txt/i)).toBeInTheDocument();
    expect(screen.getByText(/File exceeds 10 MB: large\.pdf/i)).toBeInTheDocument();
    expect(uploadDocuments).not.toHaveBeenCalled();
  });

  it('submits valid files in a single batch', async () => {
    const user = userEvent.setup();
    const uploadDocuments = vi.fn().mockResolvedValue(successfulUpload(2));

    render(<UploadDocumentPage apiBase={apiBase} uploadDocuments={uploadDocuments} />);

    const files = [makeFile('one.md', 1024), makeFile('two.pdf', 2048, 'application/pdf')];

    await user.upload(screen.getByLabelText('Choose documents'), files);

    expect(screen.getByText('2 files selected')).toBeInTheDocument();
    expect(screen.getByText('Total size: 3 KB')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Upload' }));

    await waitFor(() => expect(uploadDocuments).toHaveBeenCalledTimes(1));
    expect(uploadDocuments).toHaveBeenCalledWith(apiBase, files);
    expect(screen.getByText(/Uploaded 2 documents successfully/i)).toBeInTheDocument();
    expect(screen.getByText(/Add to RAG/i)).toBeInTheDocument();
    expect(screen.queryByText('2 files selected')).not.toBeInTheDocument();
  });

  it('rejects duplicate file names', async () => {
    const user = userEvent.setup();
    const uploadDocuments = vi.fn().mockResolvedValue(successfulUpload(0));

    render(<UploadDocumentPage apiBase={apiBase} uploadDocuments={uploadDocuments} />);

    await user.upload(screen.getByLabelText('Choose documents'), [makeFile('notes.md', 1024), makeFile('notes.md', 2048)]);
    await user.click(screen.getByRole('button', { name: 'Upload' }));

    expect(screen.getByText(/Duplicate file name rejected: notes\.md/i)).toBeInTheDocument();
    expect(uploadDocuments).not.toHaveBeenCalled();
  });

  it('keeps only ten files when more are selected', async () => {
    const user = userEvent.setup();
    const uploadDocuments = vi.fn().mockResolvedValue(successfulUpload(10));
    const files = Array.from({ length: 12 }, (_, index) => makeFile(`doc-${index + 1}.md`, 1024));

    render(<UploadDocumentPage apiBase={apiBase} uploadDocuments={uploadDocuments} />);

    await user.upload(screen.getByLabelText('Choose documents'), files);

    expect(screen.getByText(/Only 10 files can be selected/i)).toBeInTheDocument();
    expect(screen.getByText('10 files selected')).toBeInTheDocument();
    expect(screen.getByText('doc-10.md')).toBeInTheDocument();
    expect(screen.queryByText('doc-11.md')).not.toBeInTheDocument();
  });

  it('shows a message when upload is clicked without a selection', async () => {
    const user = userEvent.setup();
    const uploadDocuments = vi.fn().mockResolvedValue(successfulUpload(0));

    render(<UploadDocumentPage apiBase={apiBase} uploadDocuments={uploadDocuments} />);

    await user.click(screen.getByRole('button', { name: 'Upload' }));

    expect(screen.getByText('Please select files first')).toBeInTheDocument();
    expect(uploadDocuments).not.toHaveBeenCalled();
  });
});
