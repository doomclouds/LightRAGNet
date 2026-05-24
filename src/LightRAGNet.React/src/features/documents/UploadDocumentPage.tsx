import { useRef, useState } from 'react';
import { UploadCloud } from 'lucide-react';
import { getApiBase } from '@/api/http';
import { uploadDocuments as uploadDocumentsDefault } from '@/api/documentsApi';
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
    const selectedFiles = Array.from(event.target.files ?? []);
    const nextMessages: string[] = [];
    const nextFiles: File[] = [];
    const seenNames = new Set<string>();
    let blockingErrors = false;

    for (const file of selectedFiles) {
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

      if (nextFiles.length >= maxFiles) {
        continue;
      }

      nextFiles.push(file);
    }

    if (selectedFiles.length > maxFiles) {
      nextMessages.push('Only 10 files can be selected. Extra files were ignored.');
    }

    setFiles(nextFiles);
    setMessages(nextMessages);
    setHasBlockingErrors(blockingErrors);
    setSuccessMessage(null);
    setErrorMessage(null);
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

  return (
    <section className="document-upload" aria-labelledby="document-upload-title">
      <div className="document-upload__header">
        <div>
          <h1 id="document-upload-title">Upload Document</h1>
          <p>Upload markdown, PDF, and Word documents for later knowledge ingestion.</p>
        </div>
      </div>

      <div className="document-upload__dropzone">
        <UploadCloud size={28} aria-hidden="true" />
        <label className="document-upload__picker">
          <span>Choose documents</span>
          <input
            ref={inputRef}
            type="file"
            multiple
            accept={acceptedExtensions.join(',')}
            aria-label="Choose documents"
            onChange={handleFileChange}
          />
        </label>
        <span className="document-upload__hint">.md, .markdown, .pdf, .docx up to 10 MB each</span>
      </div>

      {messages.length > 0 ? (
        <div className="document-upload__messages" role="status" aria-live="polite">
          {messages.map((message) => (
            <p key={message}>{message}</p>
          ))}
        </div>
      ) : null}

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
      ) : null}

      {successMessage ? (
        <p className="document-upload__feedback document-upload__feedback--success" role="status">
          {successMessage}
        </p>
      ) : null}

      {errorMessage ? (
        <p className="document-upload__feedback document-upload__feedback--error" role="alert">
          {errorMessage}
        </p>
      ) : null}

      <button className="document-upload__submit" type="button" onClick={handleUpload} disabled={isUploading}>
        {isUploading ? 'Uploading...' : 'Upload'}
      </button>
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
