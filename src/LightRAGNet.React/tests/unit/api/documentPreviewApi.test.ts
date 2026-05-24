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

  it('loads safe document preview content', async () => {
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
});
