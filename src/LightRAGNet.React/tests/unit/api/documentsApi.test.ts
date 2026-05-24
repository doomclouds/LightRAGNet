import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  addToRagSystem,
  cancelDocumentPipeline,
  deleteMarkdownDocument,
  getMarkdownDocument,
  getMarkdownDocuments,
  retryDocument,
  uploadDocuments
} from '@/api/documentsApi';
import { buildUrl, readJson } from '@/api/http';

function jsonResponse(body: unknown, init?: ResponseInit): Response {
  return new Response(JSON.stringify(body), {
    headers: { 'content-type': 'application/json' },
    status: 200,
    ...init
  });
}

describe('documentsApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('gets paged markdown documents with optional filters', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({
      items: [],
      totalCount: 0,
      page: 2,
      pageSize: 25,
      totalPages: 0
    }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await getMarkdownDocuments('http://api.test/', {
      page: 2,
      pageSize: 25,
      status: 'Queued',
      trackId: 'track-1'
    });

    expect(fetchMock).toHaveBeenCalledWith(
      'http://api.test/api/MarkdownDocuments?page=2&pageSize=25&status=Queued&trackId=track-1',
      { method: 'GET' }
    );
    expect(result.page).toBe(2);
  });

  it('gets a single markdown document', async () => {
    const document = { id: 7, fileName: 'note.md', fileSize: 10, uploadTime: '2026-05-24T00:00:00Z', isInRagSystem: false, ragProgress: 0, ragRetryCount: 0 };
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(document));
    vi.stubGlobal('fetch', fetchMock);

    await expect(getMarkdownDocument('http://api.test', 7)).resolves.toEqual(document);
    expect(fetchMock).toHaveBeenCalledWith('http://api.test/api/MarkdownDocuments/7', { method: 'GET' });
  });

  it('uploads documents with files form field', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ trackId: 'track-1', documents: [] }, { status: 202 }));
    vi.stubGlobal('fetch', fetchMock);

    const file = new File(['hello'], 'hello.md', { type: 'text/markdown' });
    const result = await uploadDocuments('http://api.test', [file]);

    expect(result.trackId).toBe('track-1');
    expect(fetchMock).toHaveBeenCalledWith(
      'http://api.test/api/MarkdownDocuments/upload',
      expect.objectContaining({ method: 'POST', body: expect.any(FormData) })
    );
    const body = fetchMock.mock.calls[0]?.[1]?.body as FormData;
    expect(body.getAll('files')).toEqual([file]);
  });

  it('posts document pipeline actions', async () => {
    const actionResult = { accepted: true, documentId: 7, status: 'Queued', message: 'queued' };
    const updatedDocument = { id: 7, fileName: 'note.md', fileSize: 10, uploadTime: '2026-05-24T00:00:00Z', isInRagSystem: false, ragProgress: 0, ragRetryCount: 0 };
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(updatedDocument))
      .mockResolvedValueOnce(jsonResponse(actionResult, { status: 202 }))
      .mockResolvedValueOnce(jsonResponse(actionResult, { status: 202 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(addToRagSystem('http://api.test', 7)).resolves.toEqual(updatedDocument);
    await expect(retryDocument('http://api.test', 7)).resolves.toEqual(actionResult);
    await expect(cancelDocumentPipeline('http://api.test', 7)).resolves.toEqual(actionResult);

    expect(fetchMock).toHaveBeenNthCalledWith(1, 'http://api.test/api/MarkdownDocuments/7/add-to-rag', { method: 'POST' });
    expect(fetchMock).toHaveBeenNthCalledWith(2, 'http://api.test/api/MarkdownDocuments/7/retry', { method: 'POST' });
    expect(fetchMock).toHaveBeenNthCalledWith(3, 'http://api.test/api/MarkdownDocuments/7/cancel', { method: 'POST' });
  });

  it('maps markdown document delete response variants', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(jsonResponse({ taskId: 'task-1' }, { status: 202 }))
      .mockResolvedValueOnce(jsonResponse({ error: 'Document has active RAG task' }, { status: 409, statusText: 'Conflict' }))
      .mockResolvedValueOnce(jsonResponse({ title: 'Unexpected failure' }, { status: 500, statusText: 'Server Error' }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(deleteMarkdownDocument('http://api.test', 1)).resolves.toEqual({ succeeded: true, deletedImmediately: true });
    await expect(deleteMarkdownDocument('http://api.test', 2)).resolves.toEqual({ succeeded: true, accepted: true, taskId: 'task-1' });
    await expect(deleteMarkdownDocument('http://api.test', 3)).resolves.toEqual({ conflict: true, errorMessage: 'Document has active RAG task' });
    await expect(deleteMarkdownDocument('http://api.test', 4)).resolves.toEqual({ errorMessage: 'Unexpected failure' });

    expect(fetchMock).toHaveBeenNthCalledWith(1, 'http://api.test/api/MarkdownDocuments/1?deleteLlmCache=false', { method: 'DELETE' });
  });

  it('throws api error messages for failed non-delete requests', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ message: 'Bad upload' }, { status: 400, statusText: 'Bad Request' }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(retryDocument('http://api.test', 7)).rejects.toThrow('Bad upload');
  });

  it('builds urls without duplicate slashes at the api boundary', () => {
    expect(buildUrl('http://api.test///', '/api/MarkdownDocuments')).toBe('http://api.test/api/MarkdownDocuments');
  });

  it('reads empty successful responses and falls back to status text on empty failures', async () => {
    await expect(readJson<void>(new Response(null, { status: 204 }))).resolves.toBeUndefined();
    await expect(readJson(new Response(null, { status: 404, statusText: 'Not Found' }))).rejects.toThrow('Not Found');
  });

  it('prefers structured error fields when reading failed json responses', async () => {
    await expect(readJson(jsonResponse({ title: 'Problem details title' }, { status: 500, statusText: 'Server Error' })))
      .rejects.toThrow('Problem details title');
  });
});
