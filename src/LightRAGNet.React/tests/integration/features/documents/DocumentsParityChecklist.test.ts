import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

function readDocumentsPageSource(): string {
  return readFileSync(resolve(process.cwd(), 'src/features/documents/DocumentsPage.tsx'), 'utf8');
}

describe('DocumentsPage parity checklist', () => {
  it('keeps the document list lifecycle controls from the Blazor page', () => {
    const source = readDocumentsPageSource();

    expect(source).toContain('Status');
    expect(source).toContain('View');
    expect(source).toContain('Download');
    expect(source).toContain('Add to RAG');
    expect(source).toContain('Retry');
    expect(source).toContain('Cancel');
    expect(source).toContain('Delete');
    expect(source).toContain('Progress');
    expect(source).toContain('DeletionFailed');
    expect(source).toContain('subscribeToTaskUpdates');
    expect(source).toContain('subscribeToDataCleared');
    expect(source).toContain('shouldRefreshForTaskStatus');
    expect(source).toContain('shouldRefreshForMissingTaskStatus');
  });
});
