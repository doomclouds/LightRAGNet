import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DocumentsPage } from '@/features/documents/DocumentsPage';
import type { MarkdownDocumentDto, PagedResult, TaskStatusUpdate } from '@/features/documents/documentTypes';

const apiBase = 'http://localhost:5261';

function makeDocument(overrides: Partial<MarkdownDocumentDto> = {}): MarkdownDocumentDto {
  return {
    id: 7,
    fileName: 'pipeline.pdf',
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

function paged(
  items: MarkdownDocumentDto[],
  overrides: Partial<PagedResult<MarkdownDocumentDto>> = {}
): PagedResult<MarkdownDocumentDto> {
  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 10,
    totalPages: Math.max(1, items.length === 0 ? 0 : 1),
    ...overrides
  };
}

function captureTaskSubscription() {
  let taskHandler: ((update: TaskStatusUpdate) => void) | undefined;
  const unsubscribe = vi.fn();
  const subscribeToTaskUpdates = vi.fn((handler: (update: TaskStatusUpdate) => void) => {
    taskHandler = handler;
    return unsubscribe;
  });

  return {
    emitTask(update: TaskStatusUpdate) {
      taskHandler?.(update);
    },
    subscribeToTaskUpdates,
    unsubscribe
  };
}

function captureDataClearedSubscription() {
  let dataClearedHandler: (() => void) | undefined;
  const unsubscribe = vi.fn();
  const subscribeToDataCleared = vi.fn((handler: () => void) => {
    dataClearedHandler = handler;
    return unsubscribe;
  });

  return {
    emitDataCleared() {
      dataClearedHandler?.();
    },
    subscribeToDataCleared,
    unsubscribe
  };
}

describe('Document task refresh', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('applies task progress updates to the visible row', async () => {
    const taskSubscription = captureTaskSubscription();
    const loadDocuments = vi.fn().mockResolvedValue(paged([makeDocument()]));

    render(
      <DocumentsPage
        apiBase={apiBase}
        loadDocuments={loadDocuments}
        subscribeToTaskUpdates={taskSubscription.subscribeToTaskUpdates}
      />
    );

    await screen.findByText('pipeline.pdf');
    await act(async () => {
      taskSubscription.emitTask({
        documentId: 7,
        status: 'Processing',
        currentStage: 'MergingEntities',
        progress: 60
      });
    });

    const row = await screen.findByRole('row', { name: /pipeline\.pdf/i });
    expect(within(row).getByText('Processing')).toBeInTheDocument();
    expect(within(row).getByText('MergingEntities')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Progress 60%' })).toBeInTheDocument();
  });

  it('clears rows and reloads immediately when data cleared is received', async () => {
    const dataClearedSubscription = captureDataClearedSubscription();
    const loadDocuments = vi
      .fn()
      .mockResolvedValueOnce(paged([makeDocument()]))
      .mockResolvedValueOnce(paged([]));

    render(
      <DocumentsPage
        apiBase={apiBase}
        loadDocuments={loadDocuments}
        subscribeToDataCleared={dataClearedSubscription.subscribeToDataCleared}
      />
    );

    await screen.findByText('pipeline.pdf');
    await act(async () => {
      dataClearedSubscription.emitDataCleared();
    });

    expect(await screen.findByText('No documents found')).toBeInTheDocument();
    expect(loadDocuments).toHaveBeenCalledTimes(2);
  });

  it('debounces reloads when visible progress updates complete active work', async () => {
    const taskSubscription = captureTaskSubscription();
    const loadDocuments = vi
      .fn()
      .mockResolvedValueOnce(paged([makeDocument()]))
      .mockResolvedValue(paged([makeDocument({ isInRagSystem: true, ragStatus: 'Completed', ragProgress: 100 })]));

    render(
      <DocumentsPage
        apiBase={apiBase}
        loadDocuments={loadDocuments}
        subscribeToTaskUpdates={taskSubscription.subscribeToTaskUpdates}
      />
    );

    await screen.findByText('pipeline.pdf');
    vi.useFakeTimers();

    await act(async () => {
      taskSubscription.emitTask({ documentId: 7, status: 'Processing', currentStage: 'MergingEntities', progress: 55 });
      taskSubscription.emitTask({ documentId: 7, status: 'Completed', currentStage: null, progress: 100 });
      taskSubscription.emitTask({ documentId: 7, status: 'Completed', currentStage: null, progress: 100 });
    });

    expect(loadDocuments).toHaveBeenCalledTimes(1);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(260);
    });
    vi.useRealTimers();

    await waitFor(() => expect(loadDocuments).toHaveBeenCalledTimes(2));
  });

  it('reloads when the selected status filter boundary is crossed', async () => {
    const user = userEvent.setup();
    const taskSubscription = captureTaskSubscription();
    const loadDocuments = vi
      .fn()
      .mockResolvedValueOnce(paged([makeDocument()]))
      .mockResolvedValueOnce(paged([makeDocument({ ragStatus: 'Processing' })]))
      .mockResolvedValueOnce(paged([]));

    render(
      <DocumentsPage
        apiBase={apiBase}
        loadDocuments={loadDocuments}
        subscribeToTaskUpdates={taskSubscription.subscribeToTaskUpdates}
      />
    );

    await screen.findByText('pipeline.pdf');
    await user.selectOptions(screen.getByLabelText('RAG status filter'), 'Processing');
    await waitFor(() => expect(loadDocuments).toHaveBeenCalledTimes(2));
    vi.useFakeTimers();

    await act(async () => {
      taskSubscription.emitTask({ documentId: 7, status: 'Completed', currentStage: null, progress: 100 });
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(260);
    });
    vi.useRealTimers();

    await waitFor(() => expect(loadDocuments).toHaveBeenCalledTimes(3));
  });

  it('reloads when a missing row update matches the selected status filter', async () => {
    const user = userEvent.setup();
    const taskSubscription = captureTaskSubscription();
    const loadDocuments = vi
      .fn()
      .mockResolvedValueOnce(paged([]))
      .mockResolvedValueOnce(paged([]))
      .mockResolvedValueOnce(paged([makeDocument({ id: 9, fileName: 'queued.md', ragStatus: 'Queued' })]));

    render(
      <DocumentsPage
        apiBase={apiBase}
        loadDocuments={loadDocuments}
        subscribeToTaskUpdates={taskSubscription.subscribeToTaskUpdates}
      />
    );

    await screen.findByText('No documents found');
    await user.selectOptions(screen.getByLabelText('RAG status filter'), 'Queued');
    await waitFor(() => expect(loadDocuments).toHaveBeenCalledTimes(2));
    vi.useFakeTimers();

    await act(async () => {
      taskSubscription.emitTask({ documentId: 9, status: 'Queued', currentStage: 'Queued', progress: 0 });
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(260);
    });
    vi.useRealTimers();

    expect(await screen.findByText('queued.md')).toBeInTheDocument();
    expect(loadDocuments).toHaveBeenCalledTimes(3);
  });

  it('removes a row and reloads when delete task completes', async () => {
    const taskSubscription = captureTaskSubscription();
    const loadDocuments = vi
      .fn()
      .mockResolvedValueOnce(paged([makeDocument()], { totalCount: 1, totalPages: 1 }))
      .mockResolvedValueOnce(paged([], { totalCount: 0, totalPages: 0 }));

    render(
      <DocumentsPage
        apiBase={apiBase}
        loadDocuments={loadDocuments}
        subscribeToTaskUpdates={taskSubscription.subscribeToTaskUpdates}
      />
    );

    await screen.findByText('pipeline.pdf');
    vi.useFakeTimers();
    await act(async () => {
      taskSubscription.emitTask({
        documentId: 7,
        operationType: 'DeleteDocument',
        status: 'Completed',
        currentStage: null,
        progress: 100
      });
    });

    expect(screen.queryByText('pipeline.pdf')).not.toBeInTheDocument();
    expect(screen.getByText('Page 1 of 1')).toBeInTheDocument();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(260);
    });
    vi.useRealTimers();
    await waitFor(() => expect(loadDocuments).toHaveBeenCalledTimes(2));
  });

  it('unsubscribes and clears pending refresh timers on unmount', async () => {
    const taskSubscription = captureTaskSubscription();
    const dataClearedSubscription = captureDataClearedSubscription();
    const loadDocuments = vi.fn().mockResolvedValue(paged([makeDocument()]));

    const { unmount } = render(
      <DocumentsPage
        apiBase={apiBase}
        loadDocuments={loadDocuments}
        subscribeToTaskUpdates={taskSubscription.subscribeToTaskUpdates}
        subscribeToDataCleared={dataClearedSubscription.subscribeToDataCleared}
      />
    );

    await screen.findByText('pipeline.pdf');
    vi.useFakeTimers();
    await act(async () => {
      taskSubscription.emitTask({ documentId: 7, status: 'Completed', currentStage: null, progress: 100 });
    });
    unmount();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(260);
    });

    expect(taskSubscription.unsubscribe).toHaveBeenCalledTimes(1);
    expect(dataClearedSubscription.unsubscribe).toHaveBeenCalledTimes(1);
    expect(loadDocuments).toHaveBeenCalledTimes(1);
  });
});
