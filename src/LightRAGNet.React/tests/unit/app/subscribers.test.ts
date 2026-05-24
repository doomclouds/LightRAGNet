import { describe, expect, it, vi } from 'vitest';
import { notifySubscribers } from '@/app/subscribers';
import type { TaskStatusUpdate } from '@/features/documents/documentTypes';

describe('notifySubscribers', () => {
  it('continues notifying later task subscribers when one throws', () => {
    const update: TaskStatusUpdate = {
      documentId: 9,
      status: 'Processing',
      currentStage: 'MergingRelationships',
      progress: 72
    };
    const throwingSubscriber = vi.fn(() => {
      throw new Error('subscriber failed');
    });
    const receivingSubscriber = vi.fn();

    notifySubscribers(new Set([throwingSubscriber, receivingSubscriber]), update);

    expect(throwingSubscriber).toHaveBeenCalledWith(update);
    expect(receivingSubscriber).toHaveBeenCalledWith(update);
  });
});
