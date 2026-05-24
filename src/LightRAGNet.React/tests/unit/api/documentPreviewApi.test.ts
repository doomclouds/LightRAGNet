import { afterEach, describe, expect, it, vi } from 'vitest';
import { getDocumentPreviewContent } from '@/api/documentPreviewApi';

function jsonResponse(body: unknown, init?: ResponseInit): Response {
  return new Response(JSON.stringify(body), {
    headers: { 'content-type': 'application/json' },
    status: 200,
    ...init
  });
}

describe('documentPreviewApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('loads json document preview content', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        contentType: 'text/markdown',
        content: '# Preview',
        fileName: 'preview.md'
      })
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(getDocumentPreviewContent('http://localhost:5261', 42)).resolves.toMatchObject({
      content: '# Preview',
      fileName: 'preview.md'
    });

    expect(fetchMock).toHaveBeenCalledWith('http://localhost:5261/api/document-preview/42/content', {
      method: 'GET'
    });
  });

  it('loads text markdown preview content exactly once with filename header fallback', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response('# Text Preview', {
        status: 200,
        headers: {
          'content-type': 'text/markdown; charset=utf-8',
          'content-disposition': 'inline; filename="text-preview.md"'
        }
      })
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(getDocumentPreviewContent('http://localhost:5261', 42)).resolves.toEqual({
      contentType: 'text/markdown; charset=utf-8',
      content: '# Text Preview',
      fileName: 'text-preview.md'
    });
  });

  it('preserves non-json error response bodies', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response('preview storage exploded', {
        status: 500,
        statusText: 'Internal Server Error',
        headers: { 'content-type': 'text/plain' }
      })
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(getDocumentPreviewContent('http://localhost:5261', 42)).rejects.toThrow('preview storage exploded');
  });

  it('falls back to status text for empty non-json error bodies', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(null, {
        status: 404,
        statusText: 'Not Found',
        headers: { 'content-type': 'text/plain' }
      })
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(getDocumentPreviewContent('http://localhost:5261', 42)).rejects.toThrow('Not Found');
  });
});
