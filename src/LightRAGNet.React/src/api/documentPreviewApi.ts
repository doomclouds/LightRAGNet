import { buildUrl, readErrorMessage, readJson } from '@/api/http';

export type DocumentPreviewContent = {
  contentType: string;
  content?: string | null;
  fileName?: string | null;
  originalUrl?: string | null;
};

export async function getDocumentPreviewContent(apiBase: string, documentId: number): Promise<DocumentPreviewContent> {
  const response = await fetch(buildUrl(apiBase, `/api/document-preview/${documentId}/content`), { method: 'GET' });

  if (isJsonResponse(response)) {
    return readJson<DocumentPreviewContent>(response);
  }

  if (!response.ok) {
    throw new Error(await readErrorMessage(response));
  }

  return {
    contentType: response.headers.get('content-type') ?? 'text/markdown',
    content: await response.text(),
    fileName: getResponseFileName(response)
  };
}

function isJsonResponse(response: Response): boolean {
  return response.headers.get('content-type')?.toLowerCase().includes('application/json') ?? false;
}

function getResponseFileName(response: Response): string | null {
  const explicitFileName = response.headers.get('x-file-name')?.trim();
  if (explicitFileName) {
    return explicitFileName;
  }

  const contentDisposition = response.headers.get('content-disposition');
  if (!contentDisposition) {
    return null;
  }

  const encodedMatch = contentDisposition.match(/filename\*=UTF-8''([^;]+)/i);
  if (encodedMatch?.[1]) {
    try {
      return decodeURIComponent(encodedMatch[1].trim());
    } catch {
      return encodedMatch[1].trim();
    }
  }

  const quotedMatch = contentDisposition.match(/filename="([^"]+)"/i);
  if (quotedMatch?.[1]) {
    return quotedMatch[1].trim();
  }

  const plainMatch = contentDisposition.match(/filename=([^;]+)/i);
  return plainMatch?.[1]?.trim() ?? null;
}
