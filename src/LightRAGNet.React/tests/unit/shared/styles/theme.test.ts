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
});

function readCss(relativePath: string): string {
  return readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), 'utf8');
}
