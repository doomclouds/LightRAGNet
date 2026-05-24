import { describe, expect, it } from 'vitest';
import {
  canCancelDocumentPipeline,
  canRetryDocument,
  getShortErrorMessage,
  isDocumentBusy,
  normalizeFilterStatus
} from '@/features/documents/documentStatus';
import { formatDateTime, formatFileSize } from '@/features/documents/documentFormatters';

describe('documentStatus', () => {
  it('normalizes pending filter status to queued', () => {
    expect(normalizeFilterStatus('Pending')).toBe('Queued');
    expect(normalizeFilterStatus('Processing')).toBe('Processing');
    expect(normalizeFilterStatus(null)).toBeUndefined();
    expect(normalizeFilterStatus(undefined)).toBeUndefined();
  });

  it('marks active pipeline statuses as busy', () => {
    expect(isDocumentBusy({ ragStatus: 'Pending' })).toBe(true);
    expect(isDocumentBusy({ ragStatus: 'Queued' })).toBe(true);
    expect(isDocumentBusy({ ragStatus: 'Processing' })).toBe(true);
    expect(isDocumentBusy({ ragStatus: 'Deleting' })).toBe(true);
    expect(isDocumentBusy({ ragStatus: 'Completed' })).toBe(false);
    expect(isDocumentBusy({ ragStatus: null })).toBe(false);
  });

  it('allows retry only for failed or cancelled documents', () => {
    expect(canRetryDocument({ ragStatus: 'Failed' })).toBe(true);
    expect(canRetryDocument({ ragStatus: 'Cancelled' })).toBe(true);
    expect(canRetryDocument({ ragStatus: 'Queued' })).toBe(false);
    expect(canRetryDocument({ ragStatus: null })).toBe(false);
  });

  it('allows cancelling queued, processing, and pending documents', () => {
    expect(canCancelDocumentPipeline({ ragStatus: 'Queued' })).toBe(true);
    expect(canCancelDocumentPipeline({ ragStatus: 'Processing' })).toBe(true);
    expect(canCancelDocumentPipeline({ ragStatus: 'Pending' })).toBe(true);
    expect(canCancelDocumentPipeline({ ragStatus: 'Failed' })).toBe(false);
    expect(canCancelDocumentPipeline({ ragStatus: null })).toBe(false);
  });

  it('returns a short display error message', () => {
    expect(getShortErrorMessage(null)).toBe('Unknown error');
    expect(getShortErrorMessage('   ')).toBe('Unknown error');
    expect(getShortErrorMessage('short failure')).toBe('short failure');

    const longError = 'x'.repeat(121);
    expect(getShortErrorMessage(longError)).toBe(`${'x'.repeat(120)}...`);
  });

  it('formats file sizes for display', () => {
    expect(formatFileSize(512)).toBe('512 B');
    expect(formatFileSize(1024)).toBe('1.0 KB');
    expect(formatFileSize(10 * 1024)).toBe('10 KB');
    expect(formatFileSize(1536 * 1024)).toBe('1.5 MB');
  });

  it('formats date time values without throwing on missing or invalid input', () => {
    expect(formatDateTime(null)).toBe('-');
    expect(formatDateTime(undefined)).toBe('-');
    expect(formatDateTime('not-a-date')).toBe('not-a-date');
    expect(formatDateTime('2026-05-24T10:30:00Z')).not.toBe('2026-05-24T10:30:00Z');
  });
});
