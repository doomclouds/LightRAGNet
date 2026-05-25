import { useRef, useState } from 'react';
import { FileUp, Trash2 } from 'lucide-react';
import { getApiBase } from '@/api/http';
import { uploadDocuments as uploadDocumentsDefault } from '@/api/documentsApi';
import { Button, ButtonLink } from '@/shared/components/Button';
import { EmptyState } from '@/shared/components/EmptyState';
import { ErrorState } from '@/shared/components/ErrorState';
import { PageHeader } from '@/shared/components/PageHeader';
import { Panel } from '@/shared/components/Panel';
import { StatusPill } from '@/shared/components/StatusPill';
import type { DocumentSubmissionResponse } from './documentTypes';

type UploadDocumentsFn = (apiBase: string, files: File[]) => Promise<DocumentSubmissionResponse>;

type UploadDocumentPageProps = {
  apiBase?: string;
  uploadDocuments?: UploadDocumentsFn;
};

const acceptedExtensions = ['.md', '.markdown', '.pdf', '.docx'];
const maxFiles = 10;
const maxFileSizeBytes = 10 * 1024 * 1024;

export function UploadDocumentPage({
  apiBase = getApiBase(),
  uploadDocuments = uploadDocumentsDefault
}: UploadDocumentPageProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [files, setFiles] = useState<File[]>([]);
  const [messages, setMessages] = useState<string[]>([]);
  const [hasBlockingErrors, setHasBlockingErrors] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    if (isUploading) {
      return;
    }

    validateSelectedFiles(Array.from(event.target.files ?? []));
  }

  function handleDrop(event: React.DragEvent<HTMLDivElement>) {
    event.preventDefault();

    if (isUploading) {
      return;
    }

    validateSelectedFiles(Array.from(event.dataTransfer.files));
  }

  function validateSelectedFiles(selectedFiles: File[]) {
    const filesToValidate = selectedFiles.slice(0, maxFiles);
    const nextMessages: string[] = [];
    const nextFiles: File[] = [];
    const seenNames = new Set<string>();
    let blockingErrors = false;

    if (selectedFiles.length > maxFiles) {
      nextMessages.push('Only 10 files can be selected. Extra files were ignored.');
    }

    for (const file of filesToValidate) {
      const normalizedName = file.name.toLowerCase();
      const isSupported = acceptedExtensions.some((extension) => normalizedName.endsWith(extension));

      if (!isSupported) {
        nextMessages.push(`Unsupported file type: ${file.name}`);
        blockingErrors = true;
        continue;
      }

      if (file.size > maxFileSizeBytes) {
        nextMessages.push(`File exceeds 10 MB: ${file.name}`);
        blockingErrors = true;
        continue;
      }

      if (seenNames.has(normalizedName)) {
        nextMessages.push(`Duplicate file name rejected: ${file.name}`);
        blockingErrors = true;
        continue;
      }

      seenNames.add(normalizedName);
      nextFiles.push(file);
    }

    setFiles(nextFiles);
    setMessages(nextMessages);
    setHasBlockingErrors(blockingErrors);
    setSuccessMessage(null);
    setErrorMessage(null);
  }

  function clearSelection() {
    if (isUploading) {
      return;
    }

    setFiles([]);
    setMessages([]);
    setHasBlockingErrors(false);
    setSuccessMessage(null);
    setErrorMessage(null);

    if (inputRef.current) {
      inputRef.current.value = '';
    }
  }

  async function handleUpload() {
    setSuccessMessage(null);
    setErrorMessage(null);

    if (files.length === 0) {
      setErrorMessage('Please select files first');
      return;
    }

    if (hasBlockingErrors) {
      return;
    }

    setIsUploading(true);

    try {
      const response = await uploadDocuments(apiBase, files);
      const uploadedCount = response.documents.length;
      setFiles([]);
      setMessages([]);
      setHasBlockingErrors(false);
      setSuccessMessage(
        `Uploaded ${uploadedCount} documents successfully. Add to RAG can be started later from the document list.`
      );

      if (inputRef.current) {
        inputRef.current.value = '';
      }
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Upload failed');
    } finally {
      setIsUploading(false);
    }
  }

  const totalSize = files.reduce((sum, file) => sum + file.size, 0);
  const canClearSelection =
    files.length > 0 || messages.length > 0 || successMessage !== null || errorMessage !== null || hasBlockingErrors;

  return (
    <section className="document-upload" aria-label="Upload Document">
      <article className="document-upload__page-header">
        <PageHeader
          title="Upload Document"
          description="Stage markdown, PDF, and Word documents for later knowledge ingestion."
          meta={
            <>
              <StatusPill tone="accent">10 files max</StatusPill>
              <StatusPill tone="accent">10 MB each</StatusPill>
              <StatusPill tone="neutral">Add to RAG later</StatusPill>
            </>
          }
          actions={
            <ButtonLink href="/documents">
              Back to Documents
            </ButtonLink>
          }
        />
      </article>

      <section className="document-upload__workbench" aria-label="Upload workbench">
        <Panel as="section" className="document-upload__panel" aria-labelledby="batch-upload-title">
          <div className="document-upload__panel-header">
            <div>
              <h2 id="batch-upload-title">Batch Upload</h2>
              <span>Local validation runs before submit.</span>
            </div>
            <span>{maxFiles} files max</span>
          </div>

          <div
            className="document-upload__dropzone"
            onDragOver={(event) => event.preventDefault()}
            onDrop={handleDrop}
          >
            <FileUp size={34} aria-hidden="true" />
            <strong>Drop documents here</strong>
            <label className="document-upload__picker">
              <span>Choose documents</span>
              <input
                ref={inputRef}
                type="file"
                multiple
                accept={acceptedExtensions.join(',')}
                aria-label="Choose documents"
                disabled={isUploading}
                onChange={handleFileChange}
              />
            </label>
            <div className="document-upload__formats">
              <span>Accepted formats</span>
              <strong>{acceptedExtensions.join(', ')}</strong>
            </div>
          </div>
        </Panel>

        <Panel as="section" className="document-upload__panel document-upload__selected-panel" aria-labelledby="selected-files-title">
          <div className="document-upload__panel-header">
            <div>
              <h2 id="selected-files-title">Selected Files</h2>
              <span>{files.length} / {maxFiles} staged</span>
            </div>
            <span>{formatFileSize(totalSize)}</span>
          </div>

          {files.length > 0 ? (
            <div className="document-upload__summary" aria-label="Selected files">
              <div className="document-upload__summary-row">
                <strong>{files.length} files selected</strong>
                <span>Total size: {formatFileSize(totalSize)}</span>
              </div>
              <ul className="document-upload__file-list">
                {files.map((file) => (
                  <li key={`${file.name}-${file.size}`}>
                    <span>{file.name}</span>
                    <span>{formatFileSize(file.size)}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : (
            <EmptyState title="No files selected" description="Choose up to 10 documents before uploading." />
          )}
        </Panel>
      </section>

      {messages.length > 0 ? (
        <div className="document-upload__messages" role="status" aria-live="polite">
          {messages.map((message) => (
            <p key={message}>{message}</p>
          ))}
        </div>
      ) : null}

      {successMessage ? (
        <p className="document-upload__feedback document-upload__feedback--success" role="status">
          {successMessage}
        </p>
      ) : null}

      {errorMessage ? (
        <ErrorState message={errorMessage} />
      ) : null}

      <div className="document-upload__actions">
        <Button tone="primary" onClick={handleUpload} disabled={isUploading}>
          {isUploading ? 'Uploading...' : 'Upload'}
        </Button>
        <Button onClick={clearSelection} disabled={isUploading || !canClearSelection}>
          <Trash2 size={16} aria-hidden="true" />
          Clear selection
        </Button>
      </div>
    </section>
  );
}

function formatFileSize(size: number): string {
  if (size < 1024) {
    return `${size} B`;
  }

  const kilobytes = size / 1024;

  if (kilobytes < 1024) {
    return `${formatNumber(kilobytes)} KB`;
  }

  return `${formatNumber(kilobytes / 1024)} MB`;
}

function formatNumber(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}
