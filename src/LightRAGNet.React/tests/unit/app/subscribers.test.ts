import { describe, expect, it, vi } from 'vitest';
import { notifySubscribers } from '@/app/subscribers';
import type { TaskStatusUpdate } from '@/features/documents/documentTypes';

describe('notifySubscribers', () => {
  it('logs and continues notifying later task subscribers when one throws', () => {
    const update: TaskStatusUpdate = {
      documentId: 9,
      status: 'Processing',
      currentStage: 'MergingRelationships',
      progress: 72
    };
    const error = new Error('subscriber failed');
    const throwingSubscriber = vi.fn(() => {
      throw error;
    });
    const receivingSubscriber = vi.fn();
    const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    expect(() => notifySubscribers(new Set([throwingSubscriber, receivingSubscriber]), update)).not.toThrow();

    expect(throwingSubscriber).toHaveBeenCalledWith(update);
    expect(receivingSubscriber).toHaveBeenCalledWith(update);
    expect(consoleErrorSpy).toHaveBeenCalledWith('LightRAGNet subscriber notification failed.', error);

    consoleErrorSpy.mockRestore();
  });
});
