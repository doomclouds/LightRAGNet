import { buildUrl, readJson } from '@/api/http';

export type DocumentPreviewContent = {
  contentType: string;
  content?: string | null;
  fileName?: string | null;
  originalUrl?: string | null;
};

export async function getDocumentPreviewContent(apiBase: string, documentId: number): Promise<DocumentPreviewContent> {
  const response = await fetch(buildUrl(apiBase, `/api/document-preview/${documentId}/content`), { method: 'GET' });

  try {
    return await readJson<DocumentPreviewContent>(response.clone());
  } catch (error) {
    if (isJsonResponse(response)) {
      throw error;
    }

    if (!response.ok) {
      throw error;
    }

    return {
      contentType: response.headers.get('content-type') ?? 'text/markdown',
      content: await response.text(),
      fileName: null
    };
  }
}

function isJsonResponse(response: Response): boolean {
  return response.headers.get('content-type')?.toLowerCase().includes('application/json') ?? false;
}
