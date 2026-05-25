import { act, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createRagTaskHubClient, type RagTaskHubClient, type RagTaskHubHandlers } from '@/api/ragTaskHubClient';
import { App } from '@/app/App';
import type { SystemHealthResponse } from '@/api/systemStatusApi';
import type { MarkdownDocumentDto, PagedResult } from '@/features/documents/documentTypes';
import type { GraphViewDto } from '@/types/graph';

vi.mock('@/api/ragTaskHubClient', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/ragTaskHubClient')>();

  return {
    ...actual,
    createRagTaskHubClient: vi.fn()
  };
});

vi.mock('@/features/graph-workbench/GraphWorkbench', () => ({
  GraphWorkbench: ({ apiBase }: { apiBase: string }) => (
    <section className="graph-workbench" data-api-base={apiBase}>
      <h1>Knowledge Graph</h1>
    </section>
  )
}));

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

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' }
  });
}

function makeGraphView(): GraphViewDto {
  return {
    nodes: [
      {
        id: 'LIGHTRAG',
        label: 'LightRAGNet',
        size: 12,
        color: '#2f6fed',
        type: 'Project',
        properties: {
          description: 'LightRAGNet graph smoke node'
        }
      }
    ],
    edges: [],
    isTruncated: false
  };
}

function makeSystemHealth(): SystemHealthResponse {
  return {
    status: 'Healthy',
    generatedAt: '2026-05-24T10:00:00Z',
    durationMs: 12,
    summary: {
      healthy: 1,
      degraded: 0,
      unhealthy: 0,
      notMeasured: 0
    },
    checks: [],
    fixFirst: [],
    featureImpacts: []
  };
}

function mockRouteFetch() {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;

    if (url.includes('/api/graph/config')) {
      return jsonResponse({ maxNodesLimit: 2000 });
    }

    if (url.includes('/api/graph/query')) {
      return jsonResponse(makeGraphView());
    }

    if (url.includes('/api/system/health')) {
      return jsonResponse(makeSystemHealth());
    }

    return jsonResponse(paged([]));
  });
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

  it('renders the app banner, grouped navigation, and sidebar footer', () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(paged([])), {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
      })
    );

    render(<App />);

    expect(screen.getByRole('link', { name: 'LightRAGNet home' })).toHaveTextContent('LightRAGNet');

    const navigation = within(screen.getByRole('navigation', { name: 'Primary' }));
    expect(navigation.getByRole('heading', { name: 'Workspace' })).toBeInTheDocument();
    expect(navigation.getByRole('heading', { name: 'Document Flow' })).toBeInTheDocument();
    expect(navigation.getByRole('heading', { name: 'Operations' })).toBeInTheDocument();
    expect(navigation.getByRole('link', { name: 'RAG Chat' })).toHaveAttribute('href', '/');
    expect(navigation.getByRole('link', { name: 'Documents' })).toHaveAttribute('href', '/documents');
    expect(navigation.getByRole('link', { name: 'Knowledge Graph' })).toHaveAttribute('href', '/graph-view');
    expect(navigation.getByRole('link', { name: 'Upload Document' })).toHaveAttribute('href', '/documents/upload');
    expect(navigation.getByRole('link', { name: 'Document Preview' })).toHaveAttribute('href', '/document-preview');
    expect(navigation.getByRole('link', { name: 'System Status' })).toHaveAttribute('href', '/system-status');
    expect(navigation.getByRole('link', { name: 'Cache Management' })).toHaveAttribute('href', '/cache-management');

    const mainLandmarks = screen.getAllByRole('main');
    expect(mainLandmarks).toHaveLength(1);
    expect(mainLandmarks[0]).toHaveClass('app-main');

    const status = screen.getByRole('contentinfo', { name: 'Application status' });
    expect(status).toHaveTextContent('SignalR Connecting');
    expect(status).toHaveTextContent('LightRAGNet v0.1.0');
  });

  it('renders SignalR status changes in the sidebar footer', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(paged([])), {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
      })
    );

    render(<App />);

    const status = screen.getByLabelText('Application status');
    expect(status).toHaveTextContent('SignalR Connecting');
    expect(status.querySelector('.app-realtime-status--connecting')).toBeInTheDocument();

    await act(async () => {
      client.capturedHandlers?.onConnectionStateChanged?.('Connected');
    });

    expect(status).toHaveTextContent('SignalR Connected');
    expect(status.querySelector('.app-realtime-status--connected')).toBeInTheDocument();

    await act(async () => {
      client.capturedHandlers?.onConnectionStateChanged?.('Reconnecting');
    });

    expect(status).toHaveTextContent('SignalR Reconnecting');
    expect(status.querySelector('.app-realtime-status--connecting')).toBeInTheDocument();

    await act(async () => {
      client.capturedHandlers?.onConnectionStateChanged?.('ServerNotStarted');
    });

    expect(status).toHaveTextContent('SignalR ServerNotStarted');
    expect(status.querySelector('.app-realtime-status--disconnected')).toBeInTheDocument();
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
    expect(navigation.getByRole('link', { name: 'Upload Document' })).toHaveAttribute('aria-current', 'page');
    expect(navigation.getByRole('link', { name: 'Documents' })).not.toHaveAttribute('aria-current');
  });

  it('keeps the graph route mounted inside the light shell', async () => {
    window.history.pushState({}, '', '/graph-view');
    mockRouteFetch();

    const { container } = render(<App />);

    expect(screen.getByRole('navigation', { name: 'Primary' })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Knowledge Graph' })).toHaveAttribute('aria-current', 'page');
    const appMain = screen.getByRole('main');
    expect(appMain).toHaveClass('app-main');
    await waitFor(() => expect(container.querySelector('.graph-workbench')).toBeInTheDocument());
  });

  it('keeps the system status route mounted inside the light shell', async () => {
    window.history.pushState({}, '', '/system-status');
    mockRouteFetch();

    render(<App />);

    expect(screen.getByRole('navigation', { name: 'Primary' })).toBeInTheDocument();
    const heading = await screen.findByRole('heading', { name: 'System Status' });
    const appMain = screen.getByRole('main');
    expect(appMain).toHaveClass('app-main');
    expect(appMain).toContainElement(heading);
    expect(appMain.querySelector('.system-status')).toBeInTheDocument();
  });
});
