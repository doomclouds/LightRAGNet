import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import { describe, expect, it } from 'vitest';

const themeCss = readCss('../../../../src/shared/styles/theme.css');
const appCss = readCss('../../../../src/shared/styles/app.css');

describe('dark ops theme tokens', () => {
  it('carries the web dark ops token set with shell overlays', () => {
    [
      '--app-bg',
      '--panel-bg',
      '--panel-bg-elevated',
      '--panel-border',
      '--text-primary',
      '--text-secondary',
      '--text-muted',
      '--accent',
      '--accent-soft',
      '--accent-border',
      '--accent-fill',
      '--accent-fill-hover',
      '--accent-on-fill',
      '--danger',
      '--warning',
      '--success',
      '--control-bg',
      '--control-border',
      '--shadow-panel',
      '--shadow-popover',
      '--shadow-modal',
      '--scrim',
      '--radius-panel',
      '--radius-control'
    ].forEach((token) => {
      expect(themeCss).toContain(token);
    });
  });

  it('defines shared shell and reusable surface classes', () => {
    [
      '.app-shell',
      '.app-topbar',
      '.app-sidebar',
      '.app-statusbar',
      '.lrn-page-tabs',
      '.lrn-data-table',
      '.lrn-status-pill',
      '.lrn-scrim',
      '.lrn-drawer',
      '.lrn-modal'
    ].forEach((className) => {
      expect(appCss).toContain(className);
    });
  });

  it('uses dark-theme-safe accent fill foregrounds for document CTAs', () => {
    [
      '.document-upload__picker',
      '.document-upload__submit',
      '.document-list__upload-link'
    ].forEach((selector) => {
      const block = getRuleBlockContaining(appCss, selector, 'color: var(--accent-on-fill)');

      expect(block).toContain('color: var(--accent-on-fill)');
      expect(block).toContain('background: var(--accent-fill)');
    });

    [
      '.document-upload__picker:hover',
      '.document-upload__submit:hover',
      '.document-list__upload-link:hover'
    ].forEach((selector) => {
      expect(getRuleBlockContaining(appCss, selector, 'background: var(--accent-fill-hover)')).toContain(
        'background: var(--accent-fill-hover)'
      );
    });
  });
});

function readCss(relativePath: string): string {
  return readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), 'utf8');
}

function getRuleBlockContaining(css: string, selector: string, expectedDeclaration: string): string {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const pattern = new RegExp(`${escapedSelector}[^{}]*\\{(?<block>[^}]+)\\}`, 'gm');
  const matches = Array.from(css.matchAll(pattern));
  const match = matches.find((candidate) => candidate.groups?.block.includes(expectedDeclaration));

  expect(match, `Expected CSS rule for ${selector} containing ${expectedDeclaration}`).not.toBeUndefined();

  return match?.groups?.block ?? '';
}
