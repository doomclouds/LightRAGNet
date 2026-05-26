import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import { describe, expect, it } from 'vitest';

const themeCss = readCss('../../../../src/shared/styles/theme.css');
const appCss = readCss('../../../../src/shared/styles/app.css');
const cacheCss = readCss('../../../../src/features/cache-management/cache-management.css');
const graphCss = readCss('../../../../src/features/graph-workbench/graph-workbench.css');
const systemStatusCss = readCss('../../../../src/features/system-status/system-status.css');

describe('light workbench theme tokens', () => {
  it('carries the approved light shell token set with compatibility aliases', () => {
    const rootStyles = getRuleBlock(themeCss, ':root');

    expect(rootStyles).toContain('--app-bg: #fbfaf6');
    expect(rootStyles).toContain('--panel-bg: #fffefa');
    expect(rootStyles).toContain('--panel-border: #e5ded2');
    expect(rootStyles).toContain('--accent: #c8552d');
    expect(rootStyles).toContain('--shadow-panel: 0 18px 46px rgba(64, 46, 24, .08)');

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
      '--radius-control',
      '--sidebar-width',
      '--topbar-height',
      '--color-bg',
      '--color-surface',
      '--color-primary'
    ].forEach((token) => {
      expect(themeCss).toContain(token);
    });
  });

  it('defines shared shell and reusable surface classes', () => {
    [
      '.app-frame',
      '.app-topbar',
      '.app-sidebar',
      '.app-main-shell',
      '.app-sidebar-status',
      '.app-realtime-status',
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

  it('uses light-theme-safe accent fill foregrounds for document CTAs', () => {
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

  it('keeps cache management surfaces on the approved light theme tokens', () => {
    [
      '#111922',
      '#1b2430',
      '#263140',
      '#2a3544',
      '#0a0f15',
      '#d7f7ff',
      '#d5deea',
      '#dce4ee'
    ].forEach((darkLiteral) => {
      expect(cacheCss).not.toContain(darkLiteral);
    });

    [
      'background: var(--panel-bg-elevated)',
      'border-bottom: 1px solid var(--panel-border)',
      'color: var(--text-muted)',
      'background: var(--accent-soft)'
    ].forEach((lightDeclaration) => {
      expect(cacheCss).toContain(lightDeclaration);
    });
  });

  it('keeps system status controls and states on light theme-safe colors', () => {
    [
      '#c7f3ff',
      '#ffd5d5',
      '#dff7e5',
      '#ffe6ad',
      'rgba(76, 201, 240',
      'rgba(255, 107, 107',
      'rgba(123, 216, 143',
      'rgba(246, 200, 95'
    ].forEach((darkLiteral) => {
      expect(systemStatusCss).not.toContain(darkLiteral);
    });

    [
      'color: var(--accent-strong)',
      'color: var(--danger)',
      'color: var(--success)',
      'color: var(--warning)'
    ].forEach((lightDeclaration) => {
      expect(systemStatusCss).toContain(lightDeclaration);
    });
  });

  it('keeps graph workbench canvas, controls, and dialogs on light theme-safe colors', () => {
    [
      '#090d12',
      '#c7f3ff',
      '#ffd5d5',
      'rgba(21, 27, 35',
      'rgba(13, 17, 23',
      'rgba(76, 201, 240',
      'rgba(255, 107, 107',
      'rgb(15 23 42'
    ].forEach((darkLiteral) => {
      expect(graphCss).not.toContain(darkLiteral);
    });

    [
      'background: var(--app-bg)',
      'color: var(--accent-strong)',
      'color: var(--danger)',
      'background: var(--scrim)'
    ].forEach((lightDeclaration) => {
      expect(graphCss).toContain(lightDeclaration);
    });
  });
});

function readCss(relativePath: string): string {
  return readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), 'utf8');
}

function getRuleBlock(css: string, selector: string): string {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const pattern = new RegExp(`${escapedSelector}\\s*\\{(?<block>[^}]+)\\}`, 'm');
  const match = css.match(pattern);

  expect(match, `Expected CSS rule for ${selector}`).not.toBeNull();

  return match?.groups?.block ?? '';
}

function getRuleBlockContaining(css: string, selector: string, expectedDeclaration: string): string {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const pattern = new RegExp(`${escapedSelector}[^{}]*\\{(?<block>[^}]+)\\}`, 'gm');
  const matches = Array.from(css.matchAll(pattern));
  const match = matches.find((candidate) => candidate.groups?.block.includes(expectedDeclaration));

  expect(match, `Expected CSS rule for ${selector} containing ${expectedDeclaration}`).not.toBeUndefined();

  return match?.groups?.block ?? '';
}
