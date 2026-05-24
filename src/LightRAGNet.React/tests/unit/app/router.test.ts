import { describe, expect, it } from 'vitest';
import { getPreviewDocumentId } from '@/app/App';
import { resolveRoute, routes } from '@/app/router';

describe('resolveRoute', () => {
  it.each([
    ['/', 'rag-chat'],
    ['/rag-chat', 'rag-chat'],
    ['/documents', 'documents'],
    ['/documents/upload', 'upload'],
    ['/graph-view', 'graph'],
    ['/system-status', 'system-status'],
    ['/cache-management', 'cache-management'],
    ['/document-preview', 'document-preview'],
    ['/document-preview/42', 'document-preview'],
    ['/app/document-preview/42', 'document-preview']
  ])('maps %s to %s', (pathname, routeId) => {
    expect(resolveRoute(pathname).id).toBe(routeId);
  });

  it('falls back unknown paths to rag chat', () => {
    expect(resolveRoute('/missing-route').id).toBe('rag-chat');
  });

  it('registers rag chat as an explicit standalone alias', () => {
    expect(routes).toEqual(expect.arrayContaining([expect.objectContaining({ id: 'rag-chat', path: '/rag-chat' })]));
  });
});

describe('getPreviewDocumentId', () => {
  it.each([
    ['/document-preview/42', 42],
    ['/app/document-preview/42', 42],
    ['/app/document-preview/not-a-number', undefined]
  ])('reads preview id from %s', (pathname, expected) => {
    expect(getPreviewDocumentId(pathname)).toBe(expected);
  });
});
