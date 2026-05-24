import { describe, expect, it } from 'vitest';
import { resolveRoute } from '@/app/router';

describe('resolveRoute', () => {
  it.each([
    ['/', 'rag-chat'],
    ['/documents', 'documents'],
    ['/documents/upload', 'upload'],
    ['/graph-view', 'graph'],
    ['/system-status', 'system-status'],
    ['/cache-management', 'cache-management'],
    ['/document-preview', 'document-preview'],
    ['/document-preview/42', 'document-preview']
  ])('maps %s to %s', (pathname, routeId) => {
    expect(resolveRoute(pathname).id).toBe(routeId);
  });

  it('falls back unknown paths to rag chat', () => {
    expect(resolveRoute('/missing-route').id).toBe('rag-chat');
  });
});
