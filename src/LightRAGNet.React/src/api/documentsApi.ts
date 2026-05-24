import { buildUrl, readJson } from './http';
import type {
  DocumentPipelineActionResult,
  DocumentSubmissionResponse,
  MarkdownDocumentDeleteClientResult,
  MarkdownDocumentDto,
  PagedResult
} from '@/features/documents/documentTypes';

type DocumentsQuery = {
  page: number;
  pageSize: number;
  status?: string | null;
  trackId?: string | null;
};

export async function getMarkdownDocuments(apiBase: string, query: DocumentsQuery): Promise<PagedResult<MarkdownDocumentDto>> {
  const search = new URLSearchParams({
    page: String(query.page),
    pageSize: String(query.pageSize)
  });

  if (query.status) {
    search.set('status', query.status);
  }

  if (query.trackId) {
    search.set('trackId', query.trackId);
  }

  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments?${search.toString()}`), { method: 'GET' });
  return readJson<PagedResult<MarkdownDocumentDto>>(response);
}

export async function getMarkdownDocument(apiBase: string, id: number): Promise<MarkdownDocumentDto> {
  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments/${id}`), { method: 'GET' });
  return readJson<MarkdownDocumentDto>(response);
}

export async function uploadDocuments(apiBase: string, files: File[]): Promise<DocumentSubmissionResponse> {
  const form = new FormData();
  for (const file of files) {
    form.append('files', file, file.name);
  }

  const response = await fetch(buildUrl(apiBase, '/api/MarkdownDocuments/upload'), {
    method: 'POST',
    body: form
  });
  return readJson<DocumentSubmissionResponse>(response);
}

export async function addToRagSystem(apiBase: string, id: number): Promise<MarkdownDocumentDto> {
  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments/${id}/add-to-rag`), { method: 'POST' });
  return readJson<MarkdownDocumentDto>(response);
}

export async function retryDocument(apiBase: string, id: number): Promise<DocumentPipelineActionResult> {
  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments/${id}/retry`), { method: 'POST' });
  return readJson<DocumentPipelineActionResult>(response);
}

export async function cancelDocumentPipeline(apiBase: string, id: number): Promise<DocumentPipelineActionResult> {
  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments/${id}/cancel`), { method: 'POST' });
  return readJson<DocumentPipelineActionResult>(response);
}

export async function deleteMarkdownDocument(apiBase: string, id: number): Promise<MarkdownDocumentDeleteClientResult> {
  const response = await fetch(buildUrl(apiBase, `/api/MarkdownDocuments/${id}?deleteLlmCache=false`), { method: 'DELETE' });

  if (response.status === 204) {
    return { succeeded: true, deletedImmediately: true };
  }

  if (response.status === 202) {
    const body = await readJson<{ taskId?: string | null }>(response);
    return { succeeded: true, accepted: true, taskId: body.taskId };
  }

  if (response.status === 409) {
    try {
      await readJson<unknown>(response);
    } catch (error) {
      return { conflict: true, errorMessage: error instanceof Error ? error.message : 'Conflict' };
    }
  }

  try {
    await readJson<unknown>(response);
  } catch (error) {
    return { errorMessage: error instanceof Error ? error.message : 'Request failed' };
  }

  return { errorMessage: 'Request failed' };
}
