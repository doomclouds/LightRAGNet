# React Design System Guardrails Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add shared React design-system primitives and automated guardrails so future UI work uses the `anthropic-light` system instead of growing page-local button, panel, pill, table, dialog, font, and color debt.

**Architecture:** Keep shared, non-business UI primitives under `src/LightRAGNet.React/src/shared/components/` and shared visual language in `src/LightRAGNet.React/src/shared/styles/app.css`. Add Vitest component tests for behavior and source/CSS guardrail tests for tokens, classes, fonts, hard-coded colors, and registered page-local UI debt.

**Tech Stack:** React 19, TypeScript, Vitest, Testing Library, lucide-react, CSS tokens in `theme.css` and `app.css`.

---

## File Structure

- Create `src/LightRAGNet.React/tests/unit/shared/components/DesignSystemPrimitives.test.tsx`
  - Behavioral tests for `Banner`, `ConfirmDialog`, `SegmentedControl`, `Field`, and `DiagnosticTable`.
- Create `src/LightRAGNet.React/tests/unit/shared/styles/designSystemGuardrails.test.ts`
  - Source/CSS tests for page-local font stacks, hard-coded hex colors, and registered local UI class debt.
- Create `src/LightRAGNet.React/src/shared/components/Banner.tsx`
  - Page-level feedback primitive for info/success/warning/danger states.
- Create `src/LightRAGNet.React/src/shared/components/ConfirmDialog.tsx`
  - Shared confirmation modal for destructive or irreversible actions.
- Create `src/LightRAGNet.React/src/shared/components/SegmentedControl.tsx`
  - Shared compact mutually exclusive control.
- Create `src/LightRAGNet.React/src/shared/components/Field.tsx`
  - Shared label/hint/error wrapper for form controls.
- Create `src/LightRAGNet.React/src/shared/components/DiagnosticTable.tsx`
  - Shared key-value diagnostic table.
- Modify `src/LightRAGNet.React/src/shared/styles/app.css`
  - Add `.lrn-banner`, `.lrn-segmented-control`, `.lrn-field`, `.lrn-diagnostic-table`, and confirm dialog helper styles.
- Modify `src/LightRAGNet.React/tests/unit/shared/styles/theme.test.ts`
  - Extend existing shared class presence checks.
- Modify `design-system/MASTER.md`
  - Add the new shared primitives to the component standard and record guardrail rules.
- Modify `design-system/pages/README.md`
  - Add page override guidance for local UI debt and guardrail exceptions.

## Task 1: Write Shared Primitive Component Tests

**Files:**
- Create: `src/LightRAGNet.React/tests/unit/shared/components/DesignSystemPrimitives.test.tsx`

- [ ] **Step 1: Create the failing component test file**

Add this file exactly:

```tsx
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Banner } from '@/shared/components/Banner';
import { ConfirmDialog } from '@/shared/components/ConfirmDialog';
import { DiagnosticTable } from '@/shared/components/DiagnosticTable';
import { Field } from '@/shared/components/Field';
import { SegmentedControl } from '@/shared/components/SegmentedControl';

describe('design system primitives', () => {
  it('renders semantic banners with tone classes and readable content', () => {
    render(
      <Banner tone="danger" title="Unable to load cache overview">
        Check the server connection and try again.
      </Banner>
    );

    const banner = screen.getByRole('alert');

    expect(banner).toHaveClass('lrn-banner', 'lrn-banner--danger');
    expect(screen.getByText('Unable to load cache overview')).toBeInTheDocument();
    expect(screen.getByText('Check the server connection and try again.')).toBeInTheDocument();
  });

  it('renders segmented controls with stable pressed state and change callback', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(
      <SegmentedControl
        ariaLabel="Time window"
        value="24h"
        options={[
          { value: '24h', label: '24h' },
          { value: '7d', label: '7d' }
        ]}
        onChange={onChange}
      />
    );

    expect(screen.getByRole('group', { name: 'Time window' })).toHaveClass('lrn-segmented-control');
    expect(screen.getByRole('button', { name: '24h' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: '7d' })).toHaveAttribute('aria-pressed', 'false');

    await user.click(screen.getByRole('button', { name: '7d' }));

    expect(onChange).toHaveBeenCalledWith('7d');
  });

  it('links fields to label, hint, and error text without relying on placeholders', () => {
    render(
      <Field label="Workspace" hint="Use _ for the default workspace" error="Workspace is required">
        <input value="" onChange={() => undefined} />
      </Field>
    );

    const input = screen.getByLabelText('Workspace');

    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('Use _ for the default workspace Workspace is required');
    expect(screen.getByText('Workspace is required')).toHaveClass('lrn-field__error');
  });

  it('renders diagnostic rows with wrapped values and optional monospace values', () => {
    render(
      <DiagnosticTable
        rows={[
          { label: 'Provider', value: 'DeepSeek' },
          { label: 'Cache key', value: 'query:workspace:long-value-that-must-wrap', monospace: true }
        ]}
      />
    );

    expect(screen.getByRole('table')).toHaveClass('lrn-diagnostic-table');
    expect(screen.getByRole('row', { name: /Provider DeepSeek/ })).toBeInTheDocument();
    expect(screen.getByText('query:workspace:long-value-that-must-wrap')).toHaveClass(
      'lrn-diagnostic-table__value--mono'
    );
  });

  it('renders confirm dialogs with escape cancel, pending state, and danger action', async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    const onConfirm = vi.fn();

    render(
      <ConfirmDialog
        open
        title="Clear cache entries?"
        tone="danger"
        confirmLabel="Clear"
        cancelLabel="Cancel"
        pending={false}
        onCancel={onCancel}
        onConfirm={onConfirm}
      >
        This action cannot be undone.
      </ConfirmDialog>
    );

    expect(screen.getByRole('dialog', { name: 'Clear cache entries?' })).toHaveClass('lrn-modal');
    expect(screen.getByText('This action cannot be undone.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Clear' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);

    await user.keyboard('{Escape}');
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('keeps pending confirm dialogs open and disables both actions', () => {
    render(
      <ConfirmDialog
        open
        title="Delete document?"
        tone="danger"
        confirmLabel="Delete"
        cancelLabel="Cancel"
        pending
        onCancel={() => undefined}
        onConfirm={() => undefined}
      >
        The document will be removed from the list.
      </ConfirmDialog>
    );

    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
  });
});
```

- [ ] **Step 2: Run the component test and verify it fails for missing components**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run tests/unit/shared/components/DesignSystemPrimitives.test.tsx
```

Expected: FAIL with TypeScript or Vite import errors for missing modules such as `@/shared/components/Banner`.

- [ ] **Step 3: Commit the failing tests**

```powershell
git add src/LightRAGNet.React/tests/unit/shared/components/DesignSystemPrimitives.test.tsx
git commit -m "test: add design system primitive coverage"
```

## Task 2: Write Design System Guardrail Tests

**Files:**
- Modify: `src/LightRAGNet.React/tests/unit/shared/styles/theme.test.ts`
- Create: `src/LightRAGNet.React/tests/unit/shared/styles/designSystemGuardrails.test.ts`

- [ ] **Step 1: Extend shared class presence checks**

In `src/LightRAGNet.React/tests/unit/shared/styles/theme.test.ts`, add these class names to the existing `defines shared shell and reusable surface classes` array:

```ts
      '.lrn-banner',
      '.lrn-segmented-control',
      '.lrn-field',
      '.lrn-diagnostic-table',
      '.lrn-confirm-dialog'
```

The resulting list should still include existing entries such as `.lrn-modal`, `.lrn-status-pill`, and `.lrn-data-table-surface`.

- [ ] **Step 2: Create source/CSS guardrail tests**

Add `src/LightRAGNet.React/tests/unit/shared/styles/designSystemGuardrails.test.ts`:

```ts
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
```

- [ ] **Step 3: Run the source/CSS tests and verify they fail for missing shared classes**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run tests/unit/shared/styles/theme.test.ts tests/unit/shared/styles/designSystemGuardrails.test.ts
```

Expected: FAIL because `app.css` does not yet contain `.lrn-banner`, `.lrn-segmented-control`, `.lrn-field`, `.lrn-diagnostic-table`, and `.lrn-confirm-dialog`.

- [ ] **Step 4: Commit the failing guardrail tests**

```powershell
git add src/LightRAGNet.React/tests/unit/shared/styles/theme.test.ts src/LightRAGNet.React/tests/unit/shared/styles/designSystemGuardrails.test.ts
git commit -m "test: add React design system guardrails"
```

## Task 3: Implement Shared Components and CSS

**Files:**
- Create: `src/LightRAGNet.React/src/shared/components/Banner.tsx`
- Create: `src/LightRAGNet.React/src/shared/components/ConfirmDialog.tsx`
- Create: `src/LightRAGNet.React/src/shared/components/SegmentedControl.tsx`
- Create: `src/LightRAGNet.React/src/shared/components/Field.tsx`
- Create: `src/LightRAGNet.React/src/shared/components/DiagnosticTable.tsx`
- Modify: `src/LightRAGNet.React/src/shared/styles/app.css`

- [ ] **Step 1: Add `Banner`**

Create `src/LightRAGNet.React/src/shared/components/Banner.tsx`:

```tsx
import { AlertCircle, CheckCircle2, Info, TriangleAlert, type LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';

type BannerTone = 'info' | 'success' | 'warning' | 'danger';

type BannerProps = {
  title?: string;
  children: ReactNode;
  tone?: BannerTone;
};

const toneIcons: Record<BannerTone, LucideIcon> = {
  info: Info,
  success: CheckCircle2,
  warning: TriangleAlert,
  danger: AlertCircle
};

export function Banner({ title, children, tone = 'info' }: BannerProps) {
  const Icon = toneIcons[tone];
  const role = tone === 'danger' || tone === 'warning' ? 'alert' : 'status';

  return (
    <div className={`lrn-banner lrn-banner--${tone}`} role={role}>
      <Icon className="lrn-banner__icon" size={18} aria-hidden="true" />
      <div className="lrn-banner__body">
        {title ? <strong>{title}</strong> : null}
        <div className="lrn-banner__content">{children}</div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Add `SegmentedControl`**

Create `src/LightRAGNet.React/src/shared/components/SegmentedControl.tsx`:

```tsx
type SegmentedControlOption<TValue extends string> = {
  value: TValue;
  label: string;
  disabled?: boolean;
};

type SegmentedControlProps<TValue extends string> = {
  ariaLabel: string;
  value: TValue;
  options: Array<SegmentedControlOption<TValue>>;
  onChange: (value: TValue) => void;
  className?: string;
};

export function SegmentedControl<TValue extends string>({
  ariaLabel,
  value,
  options,
  onChange,
  className
}: SegmentedControlProps<TValue>) {
  return (
    <div className={['lrn-segmented-control', className].filter(Boolean).join(' ')} role="group" aria-label={ariaLabel}>
      {options.map((option) => (
        <button
          className="lrn-segmented-control__item"
          key={option.value}
          type="button"
          aria-pressed={option.value === value}
          disabled={option.disabled}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}
```

- [ ] **Step 3: Add `Field`**

Create `src/LightRAGNet.React/src/shared/components/Field.tsx`:

```tsx
import { cloneElement, isValidElement, useId, type ReactElement, type ReactNode } from 'react';

type FieldProps = {
  label: string;
  children: ReactElement;
  hint?: ReactNode;
  error?: ReactNode;
  className?: string;
};

export function Field({ label, children, hint, error, className }: FieldProps) {
  const generatedId = useId();
  const inputId = children.props.id ?? `${generatedId}-control`;
  const hintId = hint ? `${generatedId}-hint` : undefined;
  const errorId = error ? `${generatedId}-error` : undefined;
  const describedBy = [children.props['aria-describedby'], hintId, errorId].filter(Boolean).join(' ') || undefined;

  if (!isValidElement(children)) {
    return null;
  }

  const control = cloneElement(children, {
    id: inputId,
    'aria-describedby': describedBy,
    'aria-invalid': error ? true : children.props['aria-invalid']
  });

  return (
    <label className={['lrn-field', className].filter(Boolean).join(' ')} htmlFor={inputId}>
      <span className="lrn-field__label">{label}</span>
      {control}
      {hint ? (
        <span className="lrn-field__hint" id={hintId}>
          {hint}
        </span>
      ) : null}
      {error ? (
        <span className="lrn-field__error" id={errorId}>
          {error}
        </span>
      ) : null}
    </label>
  );
}
```

- [ ] **Step 4: Add `DiagnosticTable`**

Create `src/LightRAGNet.React/src/shared/components/DiagnosticTable.tsx`:

```tsx
import type { ReactNode } from 'react';

export type DiagnosticTableRow = {
  label: ReactNode;
  value: ReactNode;
  monospace?: boolean;
};

type DiagnosticTableProps = {
  rows: DiagnosticTableRow[];
  caption?: string;
  className?: string;
};

export function DiagnosticTable({ rows, caption, className }: DiagnosticTableProps) {
  return (
    <table className={['lrn-diagnostic-table', className].filter(Boolean).join(' ')}>
      {caption ? <caption>{caption}</caption> : null}
      <tbody>
        {rows.map((row, index) => (
          <tr key={index}>
            <th scope="row">{row.label}</th>
            <td>
              <span className={row.monospace ? 'lrn-diagnostic-table__value--mono' : undefined}>{row.value}</span>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
```

- [ ] **Step 5: Add `ConfirmDialog`**

Create `src/LightRAGNet.React/src/shared/components/ConfirmDialog.tsx`:

```tsx
import { useEffect, type ReactNode } from 'react';
import { Button } from './Button';

type ConfirmDialogTone = 'neutral' | 'danger';

type ConfirmDialogProps = {
  open: boolean;
  title: string;
  children: ReactNode;
  confirmLabel: string;
  cancelLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
  pending?: boolean;
  tone?: ConfirmDialogTone;
};

export function ConfirmDialog({
  open,
  title,
  children,
  confirmLabel,
  cancelLabel,
  onConfirm,
  onCancel,
  pending = false,
  tone = 'neutral'
}: ConfirmDialogProps) {
  useEffect(() => {
    if (!open || pending) {
      return;
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        onCancel();
      }
    }

    document.addEventListener('keydown', handleKeyDown);

    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onCancel, open, pending]);

  if (!open) {
    return null;
  }

  return (
    <div className="lrn-confirm-dialog" role="presentation">
      <div className="lrn-scrim lrn-confirm-dialog__scrim" onClick={pending ? undefined : onCancel} />
      <section className="lrn-modal lrn-confirm-dialog__surface" role="dialog" aria-modal="true" aria-labelledby="lrn-confirm-dialog-title">
        <header className="lrn-confirm-dialog__header">
          <h2 id="lrn-confirm-dialog-title">{title}</h2>
        </header>
        <div className="lrn-confirm-dialog__body">{children}</div>
        <footer className="lrn-confirm-dialog__footer">
          <Button disabled={pending} onClick={onCancel}>
            {cancelLabel}
          </Button>
          <Button tone={tone === 'danger' ? 'danger' : 'primary'} disabled={pending} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </footer>
      </section>
    </div>
  );
}
```

- [ ] **Step 6: Add shared CSS classes**

Append this block to `src/LightRAGNet.React/src/shared/styles/app.css` before the final `@media` blocks:

```css
.lrn-banner {
  min-width: 0;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 10px;
  align-items: start;
  padding: 12px 14px;
  border: 1px solid var(--panel-border);
  border-radius: var(--radius-panel);
  color: var(--text-secondary);
  background: var(--panel-bg);
}

.lrn-banner__icon {
  margin-top: 1px;
  color: currentColor;
}

.lrn-banner__body {
  min-width: 0;
  display: grid;
  gap: 3px;
}

.lrn-banner strong {
  color: var(--text-primary);
}

.lrn-banner__content {
  min-width: 0;
  line-height: 1.55;
}

.lrn-banner--info {
  color: var(--accent-strong);
  background: var(--accent-soft);
  border-color: var(--accent-border);
}

.lrn-banner--success {
  color: #3c6f45;
  background: var(--success-soft);
  border-color: #bcd5b6;
}

.lrn-banner--warning {
  color: #8a5d12;
  background: var(--warning-soft);
  border-color: #e5c780;
}

.lrn-banner--danger {
  color: #a63a28;
  background: var(--danger-soft);
  border-color: #e4afa3;
}

.lrn-segmented-control {
  min-height: 38px;
  display: inline-flex;
  align-items: stretch;
  padding: 3px;
  border: 1px solid var(--control-border);
  border-radius: var(--radius-control);
  background: var(--panel-bg-elevated);
}

.lrn-segmented-control__item {
  min-height: 30px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0 11px;
  color: var(--text-secondary);
  background: transparent;
  border: 0;
  border-radius: 6px;
  font: inherit;
  font-weight: 720;
  cursor: pointer;
}

.lrn-segmented-control__item:hover,
.lrn-segmented-control__item:focus-visible {
  color: var(--accent-strong);
  background: var(--accent-soft);
  outline: none;
}

.lrn-segmented-control__item[aria-pressed="true"] {
  color: var(--accent-on-fill);
  background: var(--accent-fill);
}

.lrn-segmented-control__item:disabled {
  cursor: not-allowed;
  opacity: .56;
}

.lrn-field {
  min-width: 0;
  display: grid;
  gap: 6px;
  color: var(--text-secondary);
  font-size: 13px;
}

.lrn-field__label {
  color: var(--text-primary);
  font-weight: 760;
}

.lrn-field input,
.lrn-field select,
.lrn-field textarea {
  width: 100%;
  min-width: 0;
  min-height: 36px;
  padding: 0 10px;
  color: var(--text-primary);
  background: var(--control-bg);
  border: 1px solid var(--control-border);
  border-radius: var(--radius-control);
  font: inherit;
}

.lrn-field textarea {
  min-height: 96px;
  padding-block: 9px;
  resize: vertical;
}

.lrn-field input:focus-visible,
.lrn-field select:focus-visible,
.lrn-field textarea:focus-visible {
  outline: 2px solid rgba(200, 85, 45, .45);
  outline-offset: 2px;
}

.lrn-field__hint {
  color: var(--text-secondary);
}

.lrn-field__error {
  color: #a63a28;
  font-weight: 680;
}

.lrn-diagnostic-table {
  width: 100%;
  border-collapse: collapse;
  table-layout: fixed;
}

.lrn-diagnostic-table caption {
  margin-bottom: 8px;
  color: var(--text-secondary);
  text-align: left;
  font-weight: 720;
}

.lrn-diagnostic-table th,
.lrn-diagnostic-table td {
  padding: 9px 10px;
  border-bottom: 1px solid var(--panel-border);
  vertical-align: top;
  text-align: left;
}

.lrn-diagnostic-table tr:last-child th,
.lrn-diagnostic-table tr:last-child td {
  border-bottom: 0;
}

.lrn-diagnostic-table th {
  width: 32%;
  color: var(--text-secondary);
  font-weight: 760;
}

.lrn-diagnostic-table td {
  color: var(--text-primary);
  overflow-wrap: anywhere;
}

.lrn-diagnostic-table__value--mono {
  font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace;
  font-size: 12px;
}

.lrn-confirm-dialog {
  position: fixed;
  inset: 0;
  z-index: 80;
  display: grid;
  place-items: center;
  padding: 20px;
}

.lrn-confirm-dialog__scrim {
  z-index: 0;
}

.lrn-confirm-dialog__surface {
  position: relative;
  z-index: 1;
  width: min(440px, 100%);
  display: grid;
  gap: 0;
  overflow: hidden;
}

.lrn-confirm-dialog__header,
.lrn-confirm-dialog__body,
.lrn-confirm-dialog__footer {
  padding: 16px 18px;
}

.lrn-confirm-dialog__header {
  border-bottom: 1px solid var(--panel-border);
}

.lrn-confirm-dialog__header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 18px;
}

.lrn-confirm-dialog__body {
  color: var(--text-secondary);
  line-height: 1.6;
}

.lrn-confirm-dialog__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  border-top: 1px solid var(--panel-border);
  background: var(--panel-bg-elevated);
}
```

- [ ] **Step 7: Run primitive tests and source/CSS tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run tests/unit/shared/components/DesignSystemPrimitives.test.tsx tests/unit/shared/styles/theme.test.ts tests/unit/shared/styles/designSystemGuardrails.test.ts
```

Expected: PASS.

- [ ] **Step 8: Commit shared primitive implementation**

```powershell
git add src/LightRAGNet.React/src/shared/components/Banner.tsx src/LightRAGNet.React/src/shared/components/ConfirmDialog.tsx src/LightRAGNet.React/src/shared/components/SegmentedControl.tsx src/LightRAGNet.React/src/shared/components/Field.tsx src/LightRAGNet.React/src/shared/components/DiagnosticTable.tsx src/LightRAGNet.React/src/shared/styles/app.css
git commit -m "feat: add React design system primitives"
```

## Task 4: Update Design System Documentation

**Files:**
- Modify: `design-system/MASTER.md`
- Modify: `design-system/pages/README.md`

- [ ] **Step 1: Update shared component list in `MASTER.md`**

In `design-system/MASTER.md`, extend the `基础组件` list with these entries:

```markdown
- `Banner`
- `ConfirmDialog`
- `SegmentedControl`
- `Field`
- `DiagnosticTable`
```

- [ ] **Step 2: Add guardrail rules to `MASTER.md`**

Add this section after `Token 契约`:

```markdown
## 设计系统护栏

React 页面新增通用 UI 时，应优先使用共享组件，而不是继续扩展页面局部按钮、面板、pill、表格或 dialog 体系。

护栏规则：

- 页面 CSS 默认不定义根级 `font-family`，应继承 `theme.css` 的全局字体栈。
- 页面 CSS 不新增非白名单硬编码 hex；通用颜色应提升为 token 或使用已有 token。
- 命中 `*__button`、`*__panel`、`*__pill`、`*__table`、`*__dialog`、`*__toolbar`、`*__banner` 等通用 UI 概念时，先检查是否应使用 `Button`、`Panel`、`StatusPill`、`DataTableSurface`、`ConfirmDialog`、`Toolbar` 或 `Banner`。
- 图谱 canvas、文档类型图标、Markdown/code 内容渲染和缓存趋势条等数据可视化颜色可以保留局部实现，但必须在测试白名单里登记。
- 现有页面局部 UI 债务必须有迁移入口，不能静默扩散。
```

- [ ] **Step 3: Add page override guidance**

Append this section to `design-system/pages/README.md`:

```markdown
## 页面局部 UI 债务登记

页面覆盖文件应记录本页面允许保留的局部 UI 体系，并说明后续替换目标。

推荐格式：

```text
后续替换点：
- `<page>__button` -> `Button`
- `<page>__panel` -> `Panel`
- `<page>__pill` -> `StatusPill`
- `<page>__table` -> `DataTableSurface` 或 `DiagnosticTable`
- `<page>__dialog` -> `ConfirmDialog` 或共享 modal/dialog 基础

允许保留：
- 数据可视化几何关系
- canvas 或图谱专用控件位置
- Markdown/code 内容排版
- 消息气泡、composer、引用 pill 等页面工作流专用布局
```
```

- [ ] **Step 4: Run documentation/source tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run tests/unit/shared/styles/theme.test.ts tests/unit/shared/styles/designSystemGuardrails.test.ts
```

Expected: PASS.

- [ ] **Step 5: Commit design system documentation**

```powershell
git add design-system/MASTER.md design-system/pages/README.md
git commit -m "docs: document React design system guardrails"
```

## Task 5: Final Verification and Handoff

**Files:**
- Verify: all files changed by Tasks 1-4

- [ ] **Step 1: Run full React test suite**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run
```

Expected: PASS for all React tests.

- [ ] **Step 2: Run React production build**

Run:

```powershell
npm run build --prefix src/LightRAGNet.React
```

Expected: PASS. The existing Vite large chunk warning is acceptable if it remains the only warning.

- [ ] **Step 3: Review changed files**

Run:

```powershell
git status --short
git diff --stat HEAD
```

Expected: working tree contains only the files from this plan, or is clean if all task commits were created.

- [ ] **Step 4: Write final implementation summary**

Include these items in the handoff:

```text
Summary:
- Added shared design-system primitives: Banner, ConfirmDialog, SegmentedControl, Field, DiagnosticTable.
- Added source/CSS guardrails for shared classes, page font stacks, hard-coded colors, and registered page-local UI debt.
- Updated design-system docs with guardrail rules and page override expectations.

Verification:
- npm test --prefix src/LightRAGNet.React -- --run
- npm run build --prefix src/LightRAGNet.React

Notes:
- This slice intentionally does not migrate System Status, Cache Management, RAG Chat, Knowledge Graph, or Document Preview.
- Existing page-local UI debt is registered so future changes fail loudly instead of drifting silently.
```

## Self-Review Notes

- Spec coverage: Tasks 1 and 3 cover the five shared primitives. Task 2 covers token/class, font, hex, and local UI debt guardrails. Task 4 covers `design-system` documentation updates and migration boundaries. Task 5 covers required verification.
- Scope check: The plan does not modify backend APIs, SignalR, graph algorithms, document upload, document preview, or cache clearing semantics.
- TDD check: Tasks 1 and 2 add failing tests before implementation. Task 3 makes those tests pass.
- Page migration boundary: No task performs a full P1 page migration; only shared components, guardrails, and docs are changed.
