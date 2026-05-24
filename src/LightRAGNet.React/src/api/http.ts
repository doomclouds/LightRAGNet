type ErrorLikeResponse = {
  message?: string;
  error?: string;
  title?: string;
};

export function getApiBase(): string {
  return import.meta.env.VITE_LIGHTRAG_API_BASE ?? 'http://localhost:5261';
}

export function buildUrl(apiBase: string, path: string): string {
  return `${apiBase.replace(/\/+$/, '')}${path}`;
}

export async function readJson<T>(response: Response): Promise<T> {
  const text = await response.text();
  const statusMessage = response.statusText || `Request failed with status ${response.status}`;

  if (text.trim().length === 0) {
    if (!response.ok) {
      throw new Error(statusMessage);
    }

    return undefined as T;
  }

  let body: T & ErrorLikeResponse;

  try {
    body = JSON.parse(text) as T & ErrorLikeResponse;
  } catch {
    throw new Error(response.ok ? 'Invalid JSON response' : statusMessage);
  }

  if (!response.ok) {
    throw new Error(body.message ?? body.error ?? body.title ?? statusMessage);
  }

  return body;
}

export async function readErrorMessage(response: Response): Promise<string> {
  const statusMessage = response.statusText || `HTTP ${response.status}`;
  const text = await response.text();
  const trimmedText = text.trim();

  if (trimmedText.length === 0) {
    return statusMessage;
  }

  try {
    const body = JSON.parse(text) as ErrorLikeResponse;
    return body.message ?? body.error ?? body.title ?? statusMessage;
  } catch {
    return trimmedText;
  }
}
