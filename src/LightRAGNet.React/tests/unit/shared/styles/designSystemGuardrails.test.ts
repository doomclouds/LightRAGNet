import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import { describe, expect, it } from 'vitest';

type CssFile = {
  name: string;
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

const allowedMonospaceFontDebt = new Set([
  'cache-management.css|.cache-key-prefix|font-family: Consolas, "Cascadia Mono", monospace',
  'system-status.css|.system-status__evidence td|font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace'
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
      'cache-banner',
      'cache-button',
      'cache-field',
      'cache-icon-button',
      'cache-panel',
      'cache-pill',
      'cache-segmented',
      'cache-table',
      'cache-table-wrap',
      'cache-toolbar'
    ]
  ],
  [
    'graph-workbench.css',
    [
      'graph-workbench__compact-field',
      'graph-workbench__confirm-dialog',
      'graph-workbench__danger-button',
      'graph-workbench__dialog',
      'graph-workbench__dialog-backdrop',
      'graph-workbench__field',
      'graph-workbench__icon-button',
      'graph-workbench__layout-menu',
      'graph-workbench__primary-button',
      'graph-workbench__range-field',
      'graph-workbench__settings-panel'
    ]
  ],
  [
    'rag-chat.css',
    [
      'rag-chat__detail-table',
      'rag-chat__detail-tab',
      'rag-chat__dialog',
      'rag-chat__dialog-backdrop',
      'rag-chat__dialog-toolbar',
      'rag-chat__field',
      'rag-chat__table-wrap'
    ]
  ],
  [
    'system-status.css',
    [
      'system-status__button',
      'system-status__panel',
      'system-status__status'
    ]
  ]
]);

describe('React design system guardrails', () => {
  it('keeps page-level font-family debt explicit and prevents new page root font stacks', () => {
    const declarations = cssFiles.flatMap((file) =>
      collectDeclarations(file.css, 'font-family').map((declaration) => `${file.name}|${declaration.selector}|${declaration.value}`)
    );

    expect(new Set(declarations)).toEqual(new Set([...allowedRootFontDebt, ...allowedMonospaceFontDebt]));
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
      expect(actual.get(file.name) ?? []).toEqual([...(allowedLocalUiDebt.get(file.name) ?? [])].sort());
    });
  });
});

function readCssFile(name: string, relativePath: string): CssFile {
  return {
    name,
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
  const classPattern = /\.([a-zA-Z][a-zA-Z0-9_-]*)\b/gm;

  for (const match of css.matchAll(classPattern)) {
    const className = stripModifier(match[1]);

    if (getLocalUiTarget(className)) {
      classNames.add(className);
    }
  }

  return Array.from(classNames).sort();
}

function stripModifier(className: string): string {
  return className.replace(/--[a-zA-Z0-9_-]+$/, '');
}

function getLocalUiTarget(className: string): string | undefined {
  const bemElement = className.match(/^[a-zA-Z][a-zA-Z0-9_-]*__(?<element>[a-zA-Z][a-zA-Z0-9_-]*)$/)?.groups?.element;

  if (bemElement) {
    return getTargetFromName(bemElement, true);
  }

  return getTargetFromName(className, false);
}

function getTargetFromName(name: string, allowStatusTarget: boolean): string | undefined {
  const targets = [
    'confirm-dialog',
    'detail-tab',
    'dialog-backdrop',
    'icon-button',
    'layout-menu',
    'table-wrap',
    'segmented',
    'toolbar',
    'button',
    'dialog',
    'banner',
    'field',
    'panel',
    'table',
    'pill'
  ];

  if (allowStatusTarget) {
    targets.push('status');
  }

  return targets.find((target) => name === target || name.endsWith(`-${target}`));
}

function normalizeSelector(selector: string): string {
  return selector
    .split(',')
    .map((part) => part.trim())
    .filter(Boolean)
    .join(', ');
}
