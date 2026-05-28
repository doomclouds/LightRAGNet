import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { describe, expect, it } from 'vitest';

type CssFile = {
  name: string;
  featurePath: string;
  css: string;
};

const cssFiles: CssFile[] = [
  readCssFile('src/features/document-preview/document-preview.css'),
  readCssFile('src/features/cache-management/cache-management.css'),
  readCssFile('src/features/graph-workbench/graph-workbench.css'),
  readCssFile('src/features/rag-chat/rag-chat.css'),
  readCssFile('src/features/system-status/system-status.css')
];

const allowedRootFontDebt = new Set([
  'graph-workbench.css|.graph-workbench|font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif'
]);

const allowedMonospaceFontDebt = new Set([
  'system-status.css|.system-status__raw-code|font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace'
]);

const allowedHexDebt = new Map<string, string[]>([
  [
    'document-preview.css',
    ['#1f2937', '#26313f', '#374151', '#7a3217', '#f7f4ee', '#fffefa']
  ],
  [
    'system-status.css',
    ['#7a3217', '#d8a43d']
  ]
]);

const allowedLocalUiDebt = new Map<string, string[]>([
  [
    'cache-management.css',
    [
      'cache-banner',
      'cache-button',
      'cache-clear-table',
      'cache-family-table',
      'cache-field',
      'cache-icon-button',
      'cache-mini-button',
      'cache-panel',
      'cache-pill',
      'cache-policy__field',
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
      'system-status__mini-button',
      'system-status__panel',
      'system-status__pill',
      'system-status__raw-toolbar',
      'system-status__table-wrap'
    ]
  ]
]);

describe('React design system guardrails', () => {
  it('keeps configured feature CSS files synchronized with src/features CSS files', () => {
    const configuredPaths = cssFiles.map((file) => file.featurePath).sort();
    const discoveredPaths = discoverFeatureCssPaths();

    expect(
      configuredPaths,
      `Configured feature CSS guardrails must match discovered src/features CSS files.\nConfigured paths:\n${formatPaths(configuredPaths)}\nDiscovered paths:\n${formatPaths(discoveredPaths)}`
    ).toEqual(discoveredPaths);
  });

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

function readCssFile(featurePath: string): CssFile {
  return {
    name: featurePath.split('/').at(-1) ?? featurePath,
    featurePath,
    css: readFileSync(join(getProjectRoot(), featurePath), 'utf8')
  };
}

function discoverFeatureCssPaths(): string[] {
  const projectRoot = getProjectRoot();
  const featuresRoot = join(projectRoot, 'src/features');
  const discoveredPaths: string[] = [];

  collectCssFiles(featuresRoot, discoveredPaths, projectRoot);

  return discoveredPaths.sort();
}

function collectCssFiles(directory: string, discoveredPaths: string[], projectRoot: string): void {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const entryPath = join(directory, entry.name);

    if (entry.isDirectory()) {
      collectCssFiles(entryPath, discoveredPaths, projectRoot);
      continue;
    }

    if (entry.isFile() && entry.name.endsWith('.css')) {
      discoveredPaths.push(relative(projectRoot, entryPath).split(sep).join('/'));
    }
  }
}

function formatPaths(paths: string[]): string {
  return paths.length ? paths.map((path) => `- ${path}`).join('\n') : '- <none>';
}

function getProjectRoot(): string {
  return process.cwd();
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
