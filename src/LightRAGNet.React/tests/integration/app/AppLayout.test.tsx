import { act, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createRagTaskHubClient, type RagTaskHubClient, type RagTaskHubHandlers } from '@/api/ragTaskHubClient';
import { App } from '@/app/App';
import type { MarkdownDocumentDto, PagedResult } from '@/features/documents/documentTypes';

vi.mock('@/api/ragTaskHubClient', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/ragTaskHubClient')>();

  return {
    ...actual,
    createRagTaskHubClient: vi.fn()
  };
});

const createRagTaskHubClientMock = vi.mocked(createRagTaskHubClient);

function makeDocument(overrides: Partial<MarkdownDocumentDto> = {}): MarkdownDocumentDto {
  return {
    id: 7,
    fileName: 'app-route.pdf',
    fileSize: 4096,
    uploadTime: '2026-05-24T10:00:00Z',
    isInRagSystem: false,
    ragStatus: 'Processing',
    ragProgress: 25,
    ragCurrentStage: 'ProcessingChunks',
    ragRetryCount: 0,
    fileUrl: null,
    ...overrides
  };
}

function paged(items: MarkdownDocumentDto[]): PagedResult<MarkdownDocumentDto> {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 10,
    totalPages: Math.max(1, items.length === 0 ? 0 : 1)
  };
}

function createClient(): RagTaskHubClient & { capturedHandlers?: RagTaskHubHandlers } {
  const client: RagTaskHubClient & { capturedHandlers?: RagTaskHubHandlers } = {
    configure: vi.fn(),
    start: vi.fn(async (handlers?: RagTaskHubHandlers) => {
      client.capturedHandlers = handlers;
    }),
    stop: vi.fn().mockResolvedValue(undefined)
  };

  return client;
}

describe('AppLayout', () => {
  let client: RagTaskHubClient & { capturedHandlers?: RagTaskHubHandlers };

  beforeEach(() => {
    client = createClient();
    createRagTaskHubClientMock.mockReturnValue(client);
    window.history.pushState({}, '', '/documents');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    window.history.pushState({}, '', '/documents');
  });

  it('renders the app banner and document navigation links', () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(paged([])), {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
      })
    );

    render(<App />);

    expect(screen.getByRole('banner')).toHaveTextContent('LightRAGNet');

    const navigation = within(screen.getByRole('navigation', { name: 'Primary' }));
    expect(navigation.getByRole('link', { name: 'RAG Chat' })).toHaveAttribute('href', '/');
    expect(navigation.getByRole('link', { name: 'Documents' })).toHaveAttribute('href', '/documents');
    expect(navigation.getByRole('link', { name: 'Upload' })).toHaveAttribute('href', '/documents/upload');
    expect(navigation.getByRole('link', { name: 'Knowledge Graph' })).toHaveAttribute('href', '/graph-view');
    expect(navigation.getByRole('link', { name: 'System Status' })).toHaveAttribute('href', '/system-status');
    expect(navigation.getByRole('link', { name: 'Cache Management' })).toHaveAttribute('href', '/cache-management');
    expect(navigation.getByRole('link', { name: 'Document Preview' })).toHaveAttribute('href', '/document-preview');
  });

  it('renders SignalR status changes in the shell statusbar', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(paged([])), {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
      })
    );

    render(<App />);

    const statusbar = screen.getByRole('contentinfo', { name: 'Application status' });
    expect(statusbar).toHaveTextContent('SignalR Connecting');

    await act(async () => {
      client.capturedHandlers?.onConnectionStateChanged?.('Connected');
    });

    expect(statusbar).toHaveTextContent('SignalR Connected');
  });

  it('wires the production document route to task hub updates', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(paged([makeDocument()])), {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
      })
    );

    render(<App />);

    await screen.findByText('app-route.pdf');

    await act(async () => {
      client.capturedHandlers?.onTaskStatusUpdated?.({
        documentId: 7,
        status: 'Processing',
        currentStage: 'MergingEntities',
        progress: 60
      });
    });

    expect(await screen.findByText('Processing / MergingEntities')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Progress 60%' })).toBeInTheDocument();
  });

  it('keeps the upload route outside the document task subscription page', () => {
    window.history.pushState({}, '', '/documents/upload');

    render(<App />);

    expect(screen.getByRole('heading', { name: 'Upload Document' })).toBeInTheDocument();
    expect(screen.getByLabelText('Choose documents')).toBeInTheDocument();
  });

  it('marks upload active without marking documents active on the upload route', () => {
    window.history.pushState({}, '', '/documents/upload');

    render(<App />);

    const navigation = within(screen.getByRole('navigation', { name: 'Primary' }));
    expect(navigation.getByRole('link', { name: 'Upload' })).toHaveAttribute('aria-current', 'page');
    expect(navigation.getByRole('link', { name: 'Documents' })).not.toHaveAttribute('aria-current');
  });
});
