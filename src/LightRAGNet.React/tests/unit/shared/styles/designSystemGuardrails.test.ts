import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import { describe, expect, it } from 'vitest';

type CssFile = {
  name: string;
  path: string;
  css: string;
};

const cssFiles: CssFile[] = [
  readCssFile('document-preview.css', '../../../../src/features/document-preview/document-preview.css'),
  readCssFile('cache-management.css', '../../../../src/features/cache-management/cache-management.css'),
  readCssFile('graph-workbench.css', '../../../../src/features/graph-workbench/graph-workbench.css'),
  readCssFile('rag-chat.css', '../../../../src/features/rag-chat/rag-chat.css'),
  readCssFile('system-status.css', '../../../../src/features/system-status/system-status.css')
];

const allowedRootFontDebt = new Set([
  'cache-management.css|.cache-workbench|font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif',
  'graph-workbench.css|.graph-workbench|font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif',
  'system-status.css|.system-status|font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif'
]);

const allowedHexDebt = new Map<string, string[]>([
  [
    'document-preview.css',
    ['#1f2937', '#26313f', '#374151', '#7a3217', '#f7f4ee', '#fffefa']
  ]
]);

const allowedLocalUiDebt = new Map<string, string[]>([
  [
    'cache-management.css',
    [
      'cache-button',
      'cache-icon-button',
      'cache-panel',
      'cache-pill',
      'cache-table',
      'cache-table-wrap',
      'cache-toolbar'
    ]
  ],
  [
    'graph-workbench.css',
    [
      'graph-workbench__dialog',
      'graph-workbench__dialog-backdrop',
      'graph-workbench__icon-button',
      'graph-workbench__layout-menu',
      'graph-workbench__primary-button',
      'graph-workbench__danger-button'
    ]
  ],
  [
    'rag-chat.css',
    [
      'rag-chat__dialog',
      'rag-chat__dialog-backdrop',
      'rag-chat__detail-table',
      'rag-chat__detail-tab',
      'rag-chat__table-wrap'
    ]
  ],
  [
    'system-status.css',
    [
      'system-status__button',
      'system-status__panel',
      'system-status__status-pill'
    ]
  ]
]);

describe('React design system guardrails', () => {
  it('keeps page-level font-family debt explicit and prevents new page root font stacks', () => {
    const declarations = cssFiles.flatMap((file) =>
      collectDeclarations(file.css, 'font-family').map((declaration) => `${file.name}|${declaration.selector}|${declaration.value}`)
    );

    const rootFontDeclarations = declarations.filter((declaration) => !declaration.includes('monospace'));

    expect(new Set(rootFontDeclarations)).toEqual(allowedRootFontDebt);
  });

  it('keeps hard-coded page hex colors registered instead of allowing silent drift', () => {
    const actual = new Map(
      cssFiles.map((file) => [file.name, collectHexLiterals(file.css)])
    );

    cssFiles.forEach((file) => {
      expect(actual.get(file.name) ?? []).toEqual(allowedHexDebt.get(file.name) ?? []);
    });
  });

  it('keeps page-local generic UI class debt registered with migration targets', () => {
    const actual = new Map(
      cssFiles.map((file) => [file.name, collectLocalUiClasses(file.css)])
    );

    cssFiles.forEach((file) => {
      expect(actual.get(file.name) ?? []).toEqual(allowedLocalUiDebt.get(file.name) ?? []);
    });
  });
});

function readCssFile(name: string, relativePath: string): CssFile {
  return {
    name,
    path: relativePath,
    css: readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), 'utf8')
  };
}

function collectDeclarations(css: string, propertyName: string): Array<{ selector: string; value: string }> {
  const declarations: Array<{ selector: string; value: string }> = [];
  const rulePattern = /(?<selector>[^{}]+)\{(?<body>[^{}]+)\}/gm;

  for (const match of css.matchAll(rulePattern)) {
    const selector = normalizeSelector(match.groups?.selector ?? '');
    const body = match.groups?.body ?? '';
    const declarationPattern = new RegExp(`${propertyName}\\s*:\\s*(?<value>[^;]+)`, 'g');

    for (const declaration of body.matchAll(declarationPattern)) {
      declarations.push({ selector, value: `${propertyName}: ${declaration.groups?.value.trim() ?? ''}` });
    }
  }

  return declarations;
}

function collectHexLiterals(css: string): string[] {
  return Array.from(new Set(css.match(/#[0-9a-fA-F]{3,8}\b/g) ?? [])).sort();
}

function collectLocalUiClasses(css: string): string[] {
  const classNames = new Set<string>();
  const classPattern = /\.([a-zA-Z][a-zA-Z0-9_-]*(?:__(?:button|icon-button|panel|pill|dialog|toolbar|table|banner)|-(?:button|icon-button|panel|pill|table|toolbar)))\b/gm;

  for (const match of css.matchAll(classPattern)) {
    classNames.add(match[1]);
  }

  return Array.from(classNames).sort();
}

function normalizeSelector(selector: string): string {
  return selector
    .split(',')
    .map((part) => part.trim())
    .filter(Boolean)
    .join(', ');
}
