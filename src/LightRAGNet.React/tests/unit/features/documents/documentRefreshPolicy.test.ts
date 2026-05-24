import { describe, expect, it } from 'vitest';
import {
  shouldRefreshForMissingTaskStatus,
  shouldRefreshForTaskStatus
} from '@/features/documents/documentStatus';

describe('document refresh policy', () => {
  it('refreshes when a visible task crosses the selected status filter boundary', () => {
    expect(shouldRefreshForTaskStatus({ status: 'Completed' }, 'Processing', 'Processing')).toBe(true);
    expect(shouldRefreshForTaskStatus({ status: 'Processing' }, 'Completed', 'Completed')).toBe(true);
    expect(shouldRefreshForTaskStatus({ status: 'Completed' }, 'Processing', 'Failed')).toBe(false);
    expect(shouldRefreshForTaskStatus({ status: 'Processing' }, 'Processing', 'Processing')).toBe(false);
  });

  it('refreshes when a missing task update now matches the selected filter', () => {
    expect(shouldRefreshForMissingTaskStatus({ status: 'Queued' }, 'Queued')).toBe(true);
    expect(shouldRefreshForMissingTaskStatus({ status: 'Pending' }, 'Queued')).toBe(true);
    expect(shouldRefreshForMissingTaskStatus({ status: 'Completed' }, 'Queued')).toBe(false);
    expect(shouldRefreshForMissingTaskStatus({ status: 'Completed' }, undefined)).toBe(false);
  });

  it('refreshes final active statuses even when no filter is selected', () => {
    expect(shouldRefreshForTaskStatus({ status: 'Completed' }, 'Processing', undefined)).toBe(true);
    expect(shouldRefreshForTaskStatus({ status: 'Failed' }, 'Pending', undefined)).toBe(true);
    expect(shouldRefreshForTaskStatus({ status: 'Completed' }, 'Queued', undefined)).toBe(true);
    expect(shouldRefreshForTaskStatus({ status: 'Queued' }, 'Pending', undefined)).toBe(false);
    expect(shouldRefreshForTaskStatus({ status: 'Completed' }, 'Completed', undefined)).toBe(false);
  });
});
