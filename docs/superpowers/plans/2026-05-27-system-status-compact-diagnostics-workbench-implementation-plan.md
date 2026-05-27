# System Status Compact Diagnostics Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the React `/system-status` page into a compact diagnostics workbench that matches the approved React/Lucide prototype while preserving the existing system health API contract and behavior.

**Architecture:** Keep the app shell unchanged and refactor only the right-side System Status page content. Split the page into small page-local components for summary tiles, evidence table, remediation priorities, feature impact, and raw JSON; reuse shared design-system primitives for header, buttons, status pills, banners, diagnostic tables, and data-table surfaces. Use the committed React prototype as the visual anchor throughout implementation and review.

**Tech Stack:** React 19, TypeScript, Vitest, Testing Library, lucide-react, existing `anthropic-light` CSS tokens.

---

## Visual Reference Lock

The implementation must use this prototype as the primary visual reference:

- `docs/superpowers/visuals/anthropic-light-workbench/05-system-status-compact-diagnostics-workbench-react-prototype.html`

The prototype was authored as a React/Lucide visual mockup, not as a static screenshot. Implementation should translate its right-side page content into production React components while relying on the existing app shell for sidebar and topbar.

Secondary reference:

- `docs/superpowers/visuals/anthropic-light-workbench/04-system-cache-table-pages.png`

Do not reintroduce the earlier large circular health ring direction.

## File Structure

- Create: `src/LightRAGNet.React/src/features/system-status/systemStatusPresentation.ts`
  - Pure presentation helpers: status tone mapping, icon mapping, duration formatting, generated-time formatting, evidence summary, JSON formatting.
- Create: `src/LightRAGNet.React/src/features/system-status/SystemStatusSummaryTiles.tsx`
  - Compact summary tile strip using existing health summary data and Lucide icons.
- Create: `src/LightRAGNet.React/src/features/system-status/SystemStatusEvidenceTable.tsx`
  - Table-first checks surface, expandable evidence details, shared `StatusPill`, shared `DiagnosticTable`.
- Create: `src/LightRAGNet.React/src/features/system-status/SystemStatusRemediationPanel.tsx`
  - Compact fix-first side panel.
- Create: `src/LightRAGNet.React/src/features/system-status/SystemStatusFeatureImpactPanel.tsx`
  - Compact user-facing impact side panel.
- Create: `src/LightRAGNet.React/src/features/system-status/SystemStatusRawJsonPanel.tsx`
  - Secondary raw JSON panel with copy action.
- Modify: `src/LightRAGNet.React/src/features/system-status/SystemStatusWorkbench.tsx`
  - Compose the new workbench structure and keep load/refresh/copy behavior.
- Modify: `src/LightRAGNet.React/src/features/system-status/system-status.css`
  - Replace heavy card-stack styles with compact dashboard layout and table-first styling.
- Modify: `src/LightRAGNet.React/tests/integration/features/system-status/SystemStatusWorkbench.test.tsx`
  - Update integration coverage for compact layout, copy behavior, refresh behavior, and empty states.
- Create: `src/LightRAGNet.React/tests/unit/features/system-status/systemStatusPresentation.test.ts`
  - Unit coverage for pure presentation helpers.
- Modify: `src/LightRAGNet.React/tests/unit/shared/styles/designSystemGuardrails.test.ts`
  - Remove migrated System Status root font and local UI debt allowances; keep only intentional monospace raw JSON allowance if needed.
- Modify: `design-system/pages/system-status.md`
  - Update page override to reflect the compact diagnostics workbench.
- Modify: `design-system/react-page-audit.md`
  - Mark System Status as migrated for this slice and document remaining local CSS boundaries.

## Task 1: Preserve Prototype And Add Presentation Helper Tests

**Files:**
- Verify: `docs/superpowers/visuals/anthropic-light-workbench/05-system-status-compact-diagnostics-workbench-react-prototype.html`
- Create: `src/LightRAGNet.React/tests/unit/features/system-status/systemStatusPresentation.test.ts`
- Create: `src/LightRAGNet.React/src/features/system-status/systemStatusPresentation.ts`

- [ ] **Step 1: Verify the committed React visual prototype exists**

Run:

```powershell
Test-Path docs\superpowers\visuals\anthropic-light-workbench\05-system-status-compact-diagnostics-workbench-react-prototype.html
```

Expected: `True`.

- [ ] **Step 2: Create failing presentation helper tests**

Create `src/LightRAGNet.React/tests/unit/features/system-status/systemStatusPresentation.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import type { SystemHealthCheckResult } from '@/api/systemStatusApi';
import {
  formatDurationMs,
  formatGeneratedAt,
  formatHealthJson,
  getStatusIconName,
  getStatusTone,
  summarizeEvidence
} from '@/features/system-status/systemStatusPresentation';

describe('systemStatusPresentation', () => {
  it('maps API statuses to shared StatusPill tones', () => {
    expect(getStatusTone('Healthy')).toBe('success');
    expect(getStatusTone('Degraded')).toBe('warning');
    expect(getStatusTone('Unhealthy')).toBe('danger');
    expect(getStatusTone('NotMeasured')).toBe('neutral');
  });

  it('maps API statuses to Lucide icon names', () => {
    expect(getStatusIconName('Healthy')).toBe('check-circle-2');
    expect(getStatusIconName('Degraded')).toBe('triangle-alert');
    expect(getStatusIconName('Unhealthy')).toBe('octagon-alert');
    expect(getStatusIconName('NotMeasured')).toBe('circle-help');
  });

  it('formats duration without inventing last-checked data', () => {
    expect(formatDurationMs(0)).toBe('0 ms');
    expect(formatDurationMs(42)).toBe('42 ms');
    expect(formatDurationMs(1240)).toBe('1.24 s');
  });

  it('formats generated time with a stable fallback', () => {
    expect(formatGeneratedAt('2026-05-27T12:01:44Z')).toContain('2026');
    expect(formatGeneratedAt('')).toBe('Unknown');
    expect(formatGeneratedAt('not-a-date')).toBe('not-a-date');
  });

  it('summarizes evidence from existing key-value data', () => {
    const check: SystemHealthCheckResult = {
      id: 'qdrant-latency',
      name: 'Qdrant',
      category: 'Vector Store',
      status: 'Degraded',
      message: 'Vector store is reachable but slow.',
      evidence: {
        collection: 'default',
        thresholdMs: 60,
        measuredMs: 84,
        ignoredAfterFirstTwo: true
      },
      remediation: 'Check the Qdrant container.',
      affects: ['RAG Chat'],
      durationMs: 84
    };

    expect(summarizeEvidence(check.evidence)).toBe('collection: default, thresholdMs: 60');
  });

  it('handles empty and complex evidence safely', () => {
    expect(summarizeEvidence({})).toBe('No evidence');
    expect(summarizeEvidence({ nested: { ok: true } })).toBe('nested: {"ok":true}');
  });

  it('formats raw health JSON for the raw data panel', () => {
    const json = formatHealthJson({ status: 'Healthy', summary: { healthy: 1 } });

    expect(json).toContain('"status": "Healthy"');
    expect(json).toContain('"healthy": 1');
  });
});
```

- [ ] **Step 3: Run helper tests and verify RED**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run tests/unit/features/system-status/systemStatusPresentation.test.ts
```

Expected: FAIL because `systemStatusPresentation.ts` does not exist.

- [ ] **Step 4: Implement presentation helpers**

Create `src/LightRAGNet.React/src/features/system-status/systemStatusPresentation.ts`:

```ts
import type { SystemHealthStatus } from '@/api/systemStatusApi';

export type SystemStatusTone = 'neutral' | 'success' | 'warning' | 'danger';
export type SystemStatusIconName =
  | 'check-circle-2'
  | 'triangle-alert'
  | 'octagon-alert'
  | 'circle-help';

export function getStatusTone(status: SystemHealthStatus): SystemStatusTone {
  if (status === 'Healthy') {
    return 'success';
  }

  if (status === 'Degraded') {
    return 'warning';
  }

  if (status === 'Unhealthy') {
    return 'danger';
  }

  return 'neutral';
}

export function getStatusIconName(status: SystemHealthStatus): SystemStatusIconName {
  if (status === 'Healthy') {
    return 'check-circle-2';
  }

  if (status === 'Degraded') {
    return 'triangle-alert';
  }

  if (status === 'Unhealthy') {
    return 'octagon-alert';
  }

  return 'circle-help';
}

export function formatDurationMs(durationMs: number): string {
  if (durationMs < 1000) {
    return `${Math.max(0, durationMs)} ms`;
  }

  return `${(durationMs / 1000).toFixed(2)} s`;
}

export function formatGeneratedAt(value: string): string {
  if (!value) {
    return 'Unknown';
  }

  const date = new Date(value);

  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString();
}

export function summarizeEvidence(evidence: Record<string, unknown>): string {
  const entries = Object.entries(evidence).slice(0, 2);

  if (entries.length === 0) {
    return 'No evidence';
  }

  return entries
    .map(([key, value]) => `${key}: ${formatEvidenceValue(value)}`)
    .join(', ');
}

export function formatHealthJson(value: unknown): string {
  return JSON.stringify(value, null, 2);
}

function formatEvidenceValue(value: unknown): string {
  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }

  if (value === null) {
    return 'null';
  }

  return JSON.stringify(value);
}
```

- [ ] **Step 5: Run helper tests and verify GREEN**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run tests/unit/features/system-status/systemStatusPresentation.test.ts
```

Expected: PASS.

- [ ] **Step 6: Commit helper tests and implementation**

```powershell
git add src/LightRAGNet.React/tests/unit/features/system-status/systemStatusPresentation.test.ts src/LightRAGNet.React/src/features/system-status/systemStatusPresentation.ts docs/superpowers/visuals/anthropic-light-workbench/05-system-status-compact-diagnostics-workbench-react-prototype.html
git commit -m "test: add System Status presentation helpers"
```

## Task 2: Add Compact Workbench Integration Tests

**Files:**
- Modify: `src/LightRAGNet.React/tests/integration/features/system-status/SystemStatusWorkbench.test.tsx`

- [ ] **Step 1: Replace the existing test file with compact dashboard coverage**

Replace `src/LightRAGNet.React/tests/integration/features/system-status/SystemStatusWorkbench.test.tsx` with:

```tsx
import { resolve } from 'node:path';
import { readFileSync } from 'node:fs';
import { afterEach, describe, expect, test, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { getSystemHealth } = vi.hoisted(() => ({
  getSystemHealth: vi.fn()
}));

vi.mock('@/api/systemStatusApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/api/systemStatusApi')>()),
  getSystemHealth
}));

import { SystemStatusWorkbench } from '@/features/system-status/SystemStatusWorkbench';
import type { SystemHealthResponse } from '@/api/systemStatusApi';

const systemStatusWorkbenchPath = resolve(process.cwd(), 'src/features/system-status/SystemStatusWorkbench.tsx');

afterEach(() => {
  vi.restoreAllMocks();
  document.body.innerHTML = '';
});

describe('SystemStatusWorkbench source guard', () => {
  test('uses server-provided health aggregation fields without local aggregation', () => {
    const source = readFileSync(systemStatusWorkbenchPath, 'utf8');

    expect(source).toContain('health.status');
    expect(source).toContain('health.summary');
    expect(source).toContain('health.fixFirst');
    expect(source).toContain('health.featureImpacts');
    expect(source).not.toMatch(/\b(?:const|let|var)\s+fixFirst\s*=/);
    expect(source).not.toMatch(/\b(?:const|let|var)\s+overallStatus\s*=/);
  });
});

describe('SystemStatusWorkbench compact diagnostics dashboard', () => {
  test('renders summary tiles, evidence table, remediation, feature impact, and raw JSON', async () => {
    getSystemHealth.mockResolvedValue(createHealth());

    render(<SystemStatusWorkbench apiBase="/api-root" />);

    expect(await screen.findByRole('heading', { name: 'System Status' })).toBeInTheDocument();
    expect(screen.getByText('Real-time diagnostics and system operation overview')).toBeInTheDocument();

    const summary = screen.getByLabelText('System health summary');
    expect(within(summary).getByText('Overall Health')).toBeInTheDocument();
    expect(within(summary).getByText('Healthy')).toBeInTheDocument();
    expect(within(summary).getByText('Degraded')).toBeInTheDocument();
    expect(within(summary).getByText('Unhealthy')).toBeInTheDocument();
    expect(within(summary).getByText('Not measured')).toBeInTheDocument();
    expect(within(summary).getByText('Duration')).toBeInTheDocument();

    const evidence = screen.getByRole('table', { name: 'Backend measurements' });
    expect(within(evidence).getByRole('row', { name: /Qdrant Vector Store Degraded/i })).toBeInTheDocument();
    expect(within(evidence).getByText('collection: default, thresholdMs: 60')).toBeInTheDocument();

    expect(screen.getByRole('heading', { name: 'Remediation Priorities' })).toBeInTheDocument();
    expect(screen.getByText('Investigate vector store latency')).toBeInTheDocument();
    expect(screen.getByText('Check Qdrant container load.')).toBeInTheDocument();

    expect(screen.getByRole('heading', { name: 'Feature Impact' })).toBeInTheDocument();
    expect(screen.getByText('RAG Chat')).toBeInTheDocument();
    expect(screen.getByText('Vector retrieval may respond slowly.')).toBeInTheDocument();

    const rawPanel = screen.getByLabelText('Raw system health JSON');
    expect(within(rawPanel).getByText(/"status": "Degraded"/)).toBeInTheDocument();
  });

  test('copies raw JSON from the shared action and the raw panel action', async () => {
    const user = userEvent.setup();
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, {
      clipboard: { writeText }
    });
    getSystemHealth.mockResolvedValue(createHealth());

    render(<SystemStatusWorkbench apiBase="/api-root" />);

    await screen.findByRole('heading', { name: 'System Status' });
    await user.click(screen.getByRole('button', { name: 'Copy JSON' }));

    expect(writeText).toHaveBeenCalledWith(expect.stringContaining('"status": "Degraded"'));
    expect(screen.getByText('Copied.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Copy raw JSON' }));

    expect(writeText).toHaveBeenCalledTimes(2);
  });

  test('keeps existing health visible while refresh is pending', async () => {
    let resolveRefresh: (value: SystemHealthResponse) => void = () => undefined;
    getSystemHealth
      .mockResolvedValueOnce(createHealth())
      .mockImplementationOnce(() => new Promise<SystemHealthResponse>((resolve) => {
        resolveRefresh = resolve;
      }));

    render(<SystemStatusWorkbench apiBase="/api-root" />);

    expect(await screen.findByText('Investigate vector store latency')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Refresh Now' }));

    expect(screen.getByText('Investigate vector store latency')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh Now' })).toBeDisabled();

    resolveRefresh(createHealth({ status: 'Healthy' }));

    await waitFor(() => expect(screen.getByRole('button', { name: 'Refresh Now' })).not.toBeDisabled());
  });

  test('renders calm empty states for remediation and feature impact', async () => {
    getSystemHealth.mockResolvedValue(createHealth({ fixFirst: [], featureImpacts: [] }));

    render(<SystemStatusWorkbench apiBase="/api-root" />);

    expect(await screen.findByText('No action required.')).toBeInTheDocument();
    expect(screen.getByText('No feature impacts reported.')).toBeInTheDocument();
  });

  test('renders API errors with a shared alert surface', async () => {
    getSystemHealth.mockRejectedValue(new Error('Server unavailable'));

    render(<SystemStatusWorkbench apiBase="/api-root" />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Server unavailable');
  });
});

function createHealth(overrides: Partial<SystemHealthResponse> = {}): SystemHealthResponse {
  return {
    status: 'Degraded',
    generatedAt: '2026-05-27T12:01:44Z',
    durationMs: 148,
    summary: {
      healthy: 9,
      degraded: 2,
      unhealthy: 1,
      notMeasured: 1
    },
    checks: [
      {
        id: 'qdrant-latency',
        name: 'Qdrant',
        category: 'Vector Store',
        status: 'Degraded',
        message: 'Vector store is reachable but slower than the warning threshold.',
        evidence: {
          collection: 'default',
          thresholdMs: 60,
          measuredMs: 84
        },
        remediation: 'Check the Qdrant container.',
        affects: ['RAG Chat'],
        durationMs: 84
      },
      {
        id: 'api-server',
        name: 'API Server',
        category: 'Core Service',
        status: 'Healthy',
        message: 'API host responded within the expected window.',
        evidence: {
          endpoint: '/api/system/health',
          httpStatus: 200
        },
        remediation: '',
        affects: [],
        durationMs: 17
      }
    ],
    fixFirst: [
      {
        checkId: 'qdrant-latency',
        title: 'Investigate vector store latency',
        status: 'Degraded',
        remediation: 'Check Qdrant container load.',
        affects: ['RAG Chat']
      }
    ],
    featureImpacts: [
      {
        feature: 'RAG Chat',
        status: 'Degraded',
        reason: 'Vector retrieval may respond slowly.',
        affectedBy: ['qdrant-latency'],
        links: [{ label: 'Open chat', href: '/rag-chat' }]
      }
    ],
    ...overrides
  };
}
```

- [ ] **Step 2: Run the updated System Status integration tests and verify RED**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run tests/integration/features/system-status/SystemStatusWorkbench.test.tsx
```

Expected: FAIL because the current implementation still renders the old card-stack classes and lacks the compact dashboard structure.

- [ ] **Step 3: Commit failing integration tests**

```powershell
git add src/LightRAGNet.React/tests/integration/features/system-status/SystemStatusWorkbench.test.tsx
git commit -m "test: cover System Status compact workbench"
```

## Task 3: Add Page-Local Compact Workbench Components

**Files:**
- Create: `src/LightRAGNet.React/src/features/system-status/SystemStatusSummaryTiles.tsx`
- Create: `src/LightRAGNet.React/src/features/system-status/SystemStatusEvidenceTable.tsx`
- Create: `src/LightRAGNet.React/src/features/system-status/SystemStatusRemediationPanel.tsx`
- Create: `src/LightRAGNet.React/src/features/system-status/SystemStatusFeatureImpactPanel.tsx`
- Create: `src/LightRAGNet.React/src/features/system-status/SystemStatusRawJsonPanel.tsx`

- [ ] **Step 1: Add compact summary tiles**

Create `src/LightRAGNet.React/src/features/system-status/SystemStatusSummaryTiles.tsx`:

```tsx
import { Activity, CheckCircle2, CircleHelp, Clock3, OctagonAlert, TriangleAlert, type LucideIcon } from 'lucide-react';
import type { SystemHealthResponse, SystemHealthStatus } from '@/api/systemStatusApi';
import { formatDurationMs, formatGeneratedAt, getStatusTone } from './systemStatusPresentation';

type SystemStatusSummaryTilesProps = {
  health: SystemHealthResponse;
};

type SummaryTile = {
  label: string;
  value: string | number;
  note: string;
  icon: LucideIcon;
  status?: SystemHealthStatus;
};

export function SystemStatusSummaryTiles({ health }: SystemStatusSummaryTilesProps) {
  const tiles: SummaryTile[] = [
    {
      label: 'Overall Health',
      value: health.status,
      note: 'Current aggregate status',
      icon: getAggregateIcon(health.status),
      status: health.status
    },
    {
      label: 'Healthy',
      value: health.summary.healthy,
      note: 'Checks passing',
      icon: CheckCircle2,
      status: 'Healthy'
    },
    {
      label: 'Degraded',
      value: health.summary.degraded,
      note: 'Checks need attention',
      icon: TriangleAlert,
      status: 'Degraded'
    },
    {
      label: 'Unhealthy',
      value: health.summary.unhealthy,
      note: 'Checks failing',
      icon: OctagonAlert,
      status: 'Unhealthy'
    },
    {
      label: 'Not measured',
      value: health.summary.notMeasured,
      note: 'Checks skipped',
      icon: CircleHelp,
      status: 'NotMeasured'
    },
    {
      label: 'Duration',
      value: formatDurationMs(health.durationMs),
      note: `Generated ${formatGeneratedAt(health.generatedAt)}`,
      icon: Clock3
    }
  ];

  return (
    <section className="system-status__summary-tiles" aria-label="System health summary">
      {tiles.map((tile) => (
        <article className="system-status__summary-tile" key={tile.label}>
          <span className={`system-status__summary-icon system-status__summary-icon--${tile.status ? getStatusTone(tile.status) : 'neutral'}`}>
            <tile.icon size={18} aria-hidden="true" />
          </span>
          <div>
            <p className="system-status__summary-label">{tile.label}</p>
            <p className="system-status__summary-value">{tile.value}</p>
            <p className="system-status__summary-note">{tile.note}</p>
          </div>
        </article>
      ))}
    </section>
  );
}

function getAggregateIcon(status: SystemHealthStatus): LucideIcon {
  if (status === 'Healthy') {
    return CheckCircle2;
  }

  if (status === 'Degraded') {
    return TriangleAlert;
  }

  if (status === 'Unhealthy') {
    return OctagonAlert;
  }

  return Activity;
}
```

- [ ] **Step 2: Add the evidence table**

Create `src/LightRAGNet.React/src/features/system-status/SystemStatusEvidenceTable.tsx`:

```tsx
import { Braces, ClipboardList } from 'lucide-react';
import type { SystemHealthCheckResult } from '@/api/systemStatusApi';
import { DataTableSurface } from '@/shared/components/DataTable';
import { DiagnosticTable } from '@/shared/components/DiagnosticTable';
import { StatusPill } from '@/shared/components/StatusPill';
import { formatDurationMs, getStatusTone, summarizeEvidence } from './systemStatusPresentation';

type SystemStatusEvidenceTableProps = {
  checks: SystemHealthCheckResult[];
};

export function SystemStatusEvidenceTable({ checks }: SystemStatusEvidenceTableProps) {
  return (
    <section className="system-status__evidence-surface" aria-label="Evidence">
      <div className="system-status__surface-header">
        <div className="system-status__tabs" aria-label="System status sections">
          <span className="system-status__tab system-status__tab--active">
            <ClipboardList size={15} aria-hidden="true" />
            Evidence
          </span>
          <span className="system-status__tab">
            <Braces size={15} aria-hidden="true" />
            Raw Data
          </span>
        </div>
      </div>
      <DataTableSurface className="system-status__table-surface">
        <table className="lrn-data-table system-status__checks-table" aria-label="Backend measurements">
          <thead>
            <tr>
              <th>Component</th>
              <th>Category</th>
              <th>Status</th>
              <th>Evidence</th>
              <th>Duration</th>
            </tr>
          </thead>
          <tbody>
            {checks.map((check) => (
              <tr key={check.id}>
                <td>
                  <strong>{check.name}</strong>
                  <span className="system-status__check-message">{check.message}</span>
                </td>
                <td>{check.category}</td>
                <td>
                  <StatusPill tone={getStatusTone(check.status)}>{check.status}</StatusPill>
                </td>
                <td>{summarizeEvidence(check.evidence)}</td>
                <td>{formatDurationMs(check.durationMs)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </DataTableSurface>
      {checks.map((check) => (
        <details className="system-status__evidence-detail" key={`${check.id}-evidence`}>
          <summary>{check.name} evidence</summary>
          <DiagnosticTable
            rows={Object.entries(check.evidence).map(([label, value]) => ({
              label,
              value: formatEvidenceValue(value),
              monospace: true
            }))}
          />
        </details>
      ))}
    </section>
  );
}

function formatEvidenceValue(value: unknown): string {
  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }

  if (value === null) {
    return 'null';
  }

  return JSON.stringify(value);
}
```

- [ ] **Step 3: Add the remediation panel**

Create `src/LightRAGNet.React/src/features/system-status/SystemStatusRemediationPanel.tsx`:

```tsx
import type { SystemHealthFixFirstItem } from '@/api/systemStatusApi';
import { Panel } from '@/shared/components/Panel';
import { StatusPill } from '@/shared/components/StatusPill';
import { getStatusTone } from './systemStatusPresentation';

type SystemStatusRemediationPanelProps = {
  items: SystemHealthFixFirstItem[];
};

export function SystemStatusRemediationPanel({ items }: SystemStatusRemediationPanelProps) {
  return (
    <Panel as="section" className="system-status__side-surface" aria-label="Remediation priorities">
      <div className="system-status__side-header">
        <h2>Remediation Priorities</h2>
      </div>
      {items.length === 0 ? (
        <p className="system-status__empty">No action required.</p>
      ) : (
        <ol className="system-status__priority-stack">
          {items.map((item, index) => (
            <li className="system-status__priority-item" key={item.checkId}>
              <span className="system-status__priority-rank">{index + 1}</span>
              <div>
                <div className="system-status__priority-title">
                  <h3>{item.title}</h3>
                  <StatusPill tone={getStatusTone(item.status)}>{item.status}</StatusPill>
                </div>
                <p>{item.remediation}</p>
                <p className="system-status__muted">{item.affects.length > 0 ? item.affects.join(', ') : 'No user-facing impact'}</p>
              </div>
            </li>
          ))}
        </ol>
      )}
    </Panel>
  );
}
```

- [ ] **Step 4: Add the feature impact panel**

Create `src/LightRAGNet.React/src/features/system-status/SystemStatusFeatureImpactPanel.tsx`:

```tsx
import { ExternalLink } from 'lucide-react';
import type { SystemHealthFeatureImpact } from '@/api/systemStatusApi';
import { Panel } from '@/shared/components/Panel';
import { StatusPill } from '@/shared/components/StatusPill';
import { getStatusTone } from './systemStatusPresentation';

type SystemStatusFeatureImpactPanelProps = {
  items: SystemHealthFeatureImpact[];
};

export function SystemStatusFeatureImpactPanel({ items }: SystemStatusFeatureImpactPanelProps) {
  return (
    <Panel as="section" className="system-status__side-surface" aria-label="Feature impact">
      <div className="system-status__side-header">
        <h2>Feature Impact</h2>
      </div>
      {items.length === 0 ? (
        <p className="system-status__empty">No feature impacts reported.</p>
      ) : (
        <div className="system-status__impact-stack">
          {items.map((item) => (
            <article className="system-status__impact-item" key={item.feature}>
              <div className="system-status__impact-title">
                <h3>{item.feature}</h3>
                <StatusPill tone={getStatusTone(item.status)}>{item.status}</StatusPill>
              </div>
              <p>{item.reason}</p>
              <p className="system-status__muted">
                Affected by: {item.affectedBy.length > 0 ? item.affectedBy.join(', ') : 'None'}
              </p>
              {item.links.length > 0 ? (
                <div className="system-status__impact-links">
                  {item.links.map((link) => (
                    <a href={link.href} key={`${item.feature}-${link.href}`}>
                      <ExternalLink size={13} aria-hidden="true" />
                      {link.label}
                    </a>
                  ))}
                </div>
              ) : null}
            </article>
          ))}
        </div>
      )}
    </Panel>
  );
}
```

- [ ] **Step 5: Add the raw JSON panel**

Create `src/LightRAGNet.React/src/features/system-status/SystemStatusRawJsonPanel.tsx`:

```tsx
import { Copy } from 'lucide-react';
import type { SystemHealthResponse } from '@/api/systemStatusApi';
import { Button } from '@/shared/components/Button';
import { Panel } from '@/shared/components/Panel';
import { formatHealthJson } from './systemStatusPresentation';

type SystemStatusRawJsonPanelProps = {
  health: SystemHealthResponse;
  onCopy: () => void;
};

export function SystemStatusRawJsonPanel({ health, onCopy }: SystemStatusRawJsonPanelProps) {
  return (
    <Panel as="section" className="system-status__raw-surface" aria-label="Raw system health JSON">
      <div className="system-status__side-header">
        <h2>Raw Data (JSON)</h2>
        <Button className="system-status__compact-button" onClick={onCopy}>
          <Copy size={14} aria-hidden="true" />
          Copy raw JSON
        </Button>
      </div>
      <pre className="system-status__raw-code">{formatHealthJson(health)}</pre>
    </Panel>
  );
}
```

- [ ] **Step 6: Run TypeScript check for new components**

Run:

```powershell
npm run typecheck --prefix src/LightRAGNet.React
```

Expected: PASS after these files compile.

- [ ] **Step 7: Commit page-local components**

```powershell
git add src/LightRAGNet.React/src/features/system-status/SystemStatusSummaryTiles.tsx src/LightRAGNet.React/src/features/system-status/SystemStatusEvidenceTable.tsx src/LightRAGNet.React/src/features/system-status/SystemStatusRemediationPanel.tsx src/LightRAGNet.React/src/features/system-status/SystemStatusFeatureImpactPanel.tsx src/LightRAGNet.React/src/features/system-status/SystemStatusRawJsonPanel.tsx
git commit -m "feat: add System Status compact components"
```

## Task 4: Compose The Compact Workbench

**Files:**
- Modify: `src/LightRAGNet.React/src/features/system-status/SystemStatusWorkbench.tsx`

- [ ] **Step 1: Replace old card-stack composition**

Replace `src/LightRAGNet.React/src/features/system-status/SystemStatusWorkbench.tsx` with:

```tsx
import { RefreshCw } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';

import { getSystemHealth } from '@/api/systemStatusApi';
import type { SystemHealthResponse } from '@/api/systemStatusApi';
import '@/features/system-status/system-status.css';
import { Banner } from '@/shared/components/Banner';
import { Button } from '@/shared/components/Button';
import { PageHeader } from '@/shared/components/PageHeader';
import { SystemStatusEvidenceTable } from './SystemStatusEvidenceTable';
import { SystemStatusFeatureImpactPanel } from './SystemStatusFeatureImpactPanel';
import { SystemStatusRawJsonPanel } from './SystemStatusRawJsonPanel';
import { SystemStatusRemediationPanel } from './SystemStatusRemediationPanel';
import { SystemStatusSummaryTiles } from './SystemStatusSummaryTiles';
import { formatHealthJson } from './systemStatusPresentation';

type SystemStatusWorkbenchProps = {
  apiBase: string;
};

export function SystemStatusWorkbench({ apiBase }: SystemStatusWorkbenchProps) {
  const [health, setHealth] = useState<SystemHealthResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [copyMessage, setCopyMessage] = useState<string | null>(null);

  const loadHealth = useCallback(async () => {
    setIsLoading(true);
    setErrorMessage(null);

    try {
      const response = await getSystemHealth(apiBase);
      setHealth(response);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to load system status.');
    } finally {
      setIsLoading(false);
    }
  }, [apiBase]);

  useEffect(() => {
    void loadHealth();
  }, [loadHealth]);

  async function copyJson() {
    if (!health) {
      return;
    }

    try {
      await navigator.clipboard.writeText(formatHealthJson(health));
      setCopyMessage('Copied.');
    } catch {
      setCopyMessage('Copy unavailable.');
    }
  }

  const actions = (
    <>
      {copyMessage ? <span className="system-status__copy-message">{copyMessage}</span> : null}
      <Button disabled={!health} onClick={copyJson}>
        Copy JSON
      </Button>
      <Button disabled={isLoading} onClick={loadHealth} tone="primary">
        <RefreshCw size={15} aria-hidden="true" className={isLoading ? 'system-status__spin' : undefined} />
        Refresh Now
      </Button>
    </>
  );

  return (
    <section className="system-status" data-api-base={apiBase}>
      <PageHeader
        title="System Status"
        description="Real-time diagnostics and system operation overview"
        actions={actions}
      />

      {errorMessage ? (
        <Banner tone="danger" title="Unable to load system status">
          {errorMessage}
        </Banner>
      ) : null}

      {isLoading && !health ? (
        <Banner tone="info">Loading system status...</Banner>
      ) : null}

      {health ? (
        <div className="system-status__workbench" data-status={health.status}>
          <SystemStatusSummaryTiles health={health} />
          <div className="system-status__diagnostics-grid">
            <SystemStatusEvidenceTable checks={health.checks} />
            <div className="system-status__side-stack">
              <SystemStatusRemediationPanel items={health.fixFirst} />
              <SystemStatusFeatureImpactPanel items={health.featureImpacts} />
            </div>
            <SystemStatusRawJsonPanel health={health} onCopy={copyJson} />
          </div>
        </div>
      ) : null}
    </section>
  );
}
```

- [ ] **Step 2: Run System Status integration tests and verify component behavior is still RED for styling only**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run tests/integration/features/system-status/SystemStatusWorkbench.test.tsx
```

Expected: PASS for semantic DOM behavior. If it fails, fix component wiring before touching CSS.

- [ ] **Step 3: Commit workbench composition**

```powershell
git add src/LightRAGNet.React/src/features/system-status/SystemStatusWorkbench.tsx
git commit -m "feat: compose System Status compact workbench"
```

## Task 5: Replace System Status CSS With Compact Diagnostics Styling

**Files:**
- Modify: `src/LightRAGNet.React/src/features/system-status/system-status.css`
- Modify: `src/LightRAGNet.React/tests/unit/shared/styles/designSystemGuardrails.test.ts`

- [ ] **Step 1: Replace System Status CSS**

Replace `src/LightRAGNet.React/src/features/system-status/system-status.css` with:

```css
.system-status {
  min-width: 0;
  display: grid;
  gap: 16px;
}

.system-status__copy-message {
  align-self: center;
  color: var(--text-secondary);
  font-size: 12px;
}

.system-status__workbench {
  min-width: 0;
  display: grid;
  gap: 14px;
}

.system-status__summary-tiles {
  min-width: 0;
  display: grid;
  grid-template-columns: 1.08fr repeat(5, minmax(128px, .72fr));
  gap: 12px;
}

.system-status__summary-tile {
  min-width: 0;
  min-height: 88px;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 12px;
  align-items: center;
  padding: 14px;
  border: 1px solid var(--panel-border);
  border-radius: var(--radius-panel);
  background: var(--panel-bg);
}

.system-status__summary-icon {
  width: 42px;
  height: 42px;
  display: inline-grid;
  place-items: center;
  border: 1.5px solid currentColor;
  border-radius: 999px;
  color: var(--text-secondary);
}

.system-status__summary-icon--success {
  color: var(--success);
}

.system-status__summary-icon--warning {
  color: var(--warning);
}

.system-status__summary-icon--danger {
  color: var(--danger);
}

.system-status__summary-label,
.system-status__summary-note,
.system-status__check-message,
.system-status__muted,
.system-status__empty,
.system-status__impact-item p,
.system-status__priority-item p {
  margin: 0;
  color: var(--text-secondary);
}

.system-status__summary-label {
  font-size: 11px;
  font-weight: 760;
}

.system-status__summary-value {
  margin: 3px 0 0;
  color: var(--text-primary);
  font-size: 22px;
  font-weight: 850;
  line-height: 1.05;
  overflow-wrap: anywhere;
}

.system-status__summary-note {
  margin-top: 5px;
  font-size: 12px;
  line-height: 1.35;
}

.system-status__diagnostics-grid {
  min-width: 0;
  display: grid;
  grid-template-columns: minmax(0, 1.42fr) minmax(280px, .74fr) minmax(300px, .72fr);
  gap: 12px;
  align-items: start;
}

.system-status__evidence-surface,
.system-status__side-surface,
.system-status__raw-surface {
  min-width: 0;
  overflow: hidden;
  border: 1px solid var(--panel-border);
  border-radius: var(--radius-panel);
  background: var(--panel-bg);
}

.system-status__surface-header,
.system-status__side-header {
  min-height: 43px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border-bottom: 1px solid var(--panel-border);
  padding: 0 14px;
}

.system-status__side-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 14px;
}

.system-status__tabs {
  display: flex;
  align-items: stretch;
  min-height: 43px;
}

.system-status__tab {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  border-bottom: 2px solid transparent;
  color: var(--text-secondary);
  padding: 0 12px;
  font-size: 12px;
  font-weight: 760;
}

.system-status__tab--active {
  border-color: var(--accent);
  color: var(--accent-strong);
}

.system-status__table-surface {
  border: 0;
  border-radius: 0;
  box-shadow: none;
}

.system-status__checks-table {
  min-width: 760px;
}

.system-status__checks-table th,
.system-status__checks-table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--panel-border);
  text-align: left;
  vertical-align: middle;
  font-size: 12px;
}

.system-status__checks-table th {
  color: var(--text-secondary);
  background: var(--panel-bg-elevated);
  font-weight: 780;
}

.system-status__checks-table tr:last-child td {
  border-bottom: 0;
}

.system-status__check-message {
  display: block;
  margin-top: 4px;
  line-height: 1.35;
}

.system-status__evidence-detail {
  border-top: 1px solid var(--panel-border);
  padding: 10px 14px;
}

.system-status__evidence-detail summary {
  color: var(--accent-strong);
  cursor: pointer;
  font-size: 12px;
  font-weight: 760;
}

.system-status__side-stack {
  min-width: 0;
  display: grid;
  gap: 12px;
}

.system-status__priority-stack,
.system-status__impact-stack {
  display: grid;
  gap: 10px;
  margin: 0;
  padding: 14px;
}

.system-status__priority-stack {
  list-style: none;
}

.system-status__priority-item {
  min-width: 0;
  display: grid;
  grid-template-columns: 22px minmax(0, 1fr);
  gap: 10px;
  align-items: start;
}

.system-status__priority-rank {
  width: 20px;
  height: 20px;
  display: inline-grid;
  place-items: center;
  border-radius: 5px;
  color: var(--accent-on-fill);
  background: var(--accent-fill);
  font-size: 11px;
  font-weight: 850;
}

.system-status__priority-title,
.system-status__impact-title {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
}

.system-status__priority-title h3,
.system-status__impact-title h3 {
  margin: 0 0 4px;
  color: var(--text-primary);
  font-size: 12px;
}

.system-status__priority-item p,
.system-status__impact-item p {
  font-size: 11px;
  line-height: 1.42;
}

.system-status__muted {
  margin-top: 5px;
  font-size: 11px;
}

.system-status__empty {
  padding: 14px;
  font-size: 12px;
}

.system-status__impact-item {
  min-width: 0;
}

.system-status__impact-links {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 8px;
}

.system-status__impact-links a {
  min-height: 24px;
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 0 8px;
  color: var(--accent-strong);
  border: 1px solid var(--accent-border);
  border-radius: 6px;
  font-size: 11px;
  font-weight: 760;
}

.system-status__raw-surface {
  min-height: 514px;
}

.system-status__compact-button {
  min-height: 26px;
  padding: 0 9px;
  font-size: 11px;
}

.system-status__raw-code {
  height: 470px;
  margin: 0;
  overflow: auto;
  padding: 14px;
  color: #7a3217;
  font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace;
  font-size: 11px;
  line-height: 1.55;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.system-status__spin {
  animation: system-status-spin 900ms linear infinite;
}

@keyframes system-status-spin {
  to {
    transform: rotate(360deg);
  }
}

@media (prefers-reduced-motion: reduce) {
  .system-status__spin {
    animation: none;
  }
}

@media (max-width: 1180px) {
  .system-status__summary-tiles {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .system-status__diagnostics-grid {
    grid-template-columns: 1fr;
  }

  .system-status__raw-surface {
    min-height: 0;
  }
}

@media (max-width: 720px) {
  .system-status__summary-tiles {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .system-status__summary-tile:first-child {
    grid-column: 1 / -1;
  }
}

@media (max-width: 560px) {
  .system-status__summary-tiles {
    grid-template-columns: 1fr;
  }

  .system-status__surface-header,
  .system-status__side-header {
    align-items: stretch;
    flex-direction: column;
    height: auto;
    padding: 12px 14px;
  }

  .system-status__tabs {
    overflow-x: auto;
  }
}
```

- [ ] **Step 2: Update design-system guardrail allowances**

In `src/LightRAGNet.React/tests/unit/shared/styles/designSystemGuardrails.test.ts`:

1. Remove this entry from `allowedRootFontDebt`:

```ts
'system-status.css|.system-status|font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif'
```

2. Replace this entry in `allowedMonospaceFontDebt`:

```ts
'system-status.css|.system-status__evidence td|font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace'
```

with:

```ts
'system-status.css|.system-status__raw-code|font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace'
```

3. Replace the `system-status.css` entry in `allowedLocalUiDebt`:

```ts
[
  'system-status.css',
  [
    'system-status__button',
    'system-status__panel',
    'system-status__status'
  ]
]
```

with:

```ts
[
  'system-status.css',
  []
]
```

- [ ] **Step 3: Run guardrail tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run tests/unit/shared/styles/designSystemGuardrails.test.ts tests/unit/shared/styles/theme.test.ts
```

Expected: PASS. If the local UI debt test flags new `system-status` classes, rename the classes so they do not duplicate generic shared UI concepts such as button, panel, pill, table, toolbar, or dialog.

- [ ] **Step 4: Run System Status tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run tests/unit/features/system-status/systemStatusPresentation.test.ts tests/integration/features/system-status/SystemStatusWorkbench.test.tsx
```

Expected: PASS.

- [ ] **Step 5: Commit compact CSS and guardrail updates**

```powershell
git add src/LightRAGNet.React/src/features/system-status/system-status.css src/LightRAGNet.React/tests/unit/shared/styles/designSystemGuardrails.test.ts
git commit -m "style: compact System Status diagnostics layout"
```

## Task 6: Update Design Documentation And Audit

**Files:**
- Modify: `design-system/pages/system-status.md`
- Modify: `design-system/react-page-audit.md`

- [ ] **Step 1: Update the System Status page override**

In `design-system/pages/system-status.md`, replace the content with:

```md
# System Status 页面设计覆盖

- 页面路由：`/system-status`
- 页面类型：`Compact Diagnostic Workbench`
- 主参考：
  - `docs/superpowers/visuals/anthropic-light-workbench/04-system-cache-table-pages.png`
  - `docs/superpowers/visuals/anthropic-light-workbench/05-system-status-compact-diagnostics-workbench-react-prototype.html`
- 源文件：
  - `src/LightRAGNet.React/src/features/system-status/SystemStatusWorkbench.tsx`
  - `src/LightRAGNet.React/src/features/system-status/system-status.css`

## 页面角色

System Status 是精炼的诊断工作台。它应该先给出整体健康状态和摘要，再把用户引向证据表格、修复优先级、功能影响和 raw JSON。

## 必须使用的共享组件

- `PageHeader`
- `Panel`
- `Button`
- `StatusPill`
- `DataTableSurface`
- `DiagnosticTable`
- `Banner`

## 页面局部组件

- `SystemStatusSummaryTiles`
- `SystemStatusEvidenceTable`
- `SystemStatusRemediationPanel`
- `SystemStatusFeatureImpactPanel`
- `SystemStatusRawJsonPanel`

这些组件本轮保持页面局部。只有当 Cache Management、RAG Chat 或其他诊断页面复用同一结构时，才考虑提升共享。

## 允许保留

- System Status 的 dashboard grid。
- Summary tile 的页面专用布局。
- Raw JSON 面板尺寸和滚动。
- Evidence 展开区域的页面组织方式。

## 规则

- 不使用字符或 emoji 作为图标；统一使用 `lucide-react`。
- 不新增 API 字段、假指标或导出能力。
- Evidence table 是主扫读路径。
- Raw JSON 是二级诊断辅助区，不替代结构化 evidence。
- 页面 CSS 不定义根级字体栈。
- 页面局部 CSS 不复制通用 button、panel、pill、table、dialog 体系。

## 视觉 QA

- 桌面端 summary tiles + evidence table + side stack + raw JSON。
- 768px 下 summary tiles 换行，主表格优先。
- 375px 下单列布局，表格和 raw JSON 只在内部滚动。
- loading、refresh pending、error、empty fix-first、empty feature impact。
```

- [ ] **Step 2: Update React page audit**

In `design-system/react-page-audit.md`, update the `System Status` row from `中` to `高` and replace its main gap with:

```md
已按 compact diagnostics workbench 方向迁移；后续只需观察该页面局部组件是否被 Cache/RAG Chat 复用，再决定是否提升共享。
```

Also update the recommended migration slices so System Status is no longer listed as pending in the same way. Keep Cache Management as the next diagnostics/list workbench migration candidate.

- [ ] **Step 3: Commit docs updates**

```powershell
git add design-system/pages/system-status.md design-system/react-page-audit.md
git commit -m "docs: update System Status design audit"
```

## Task 7: Visual QA And Final Verification

**Files:**
- Verify changed System Status files.
- Optional screenshots under a temporary ignored location only, unless the reviewer asks to preserve them.

- [ ] **Step 1: Run focused System Status tests**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run tests/unit/features/system-status/systemStatusPresentation.test.ts tests/integration/features/system-status/SystemStatusWorkbench.test.tsx
```

Expected: PASS.

- [ ] **Step 2: Run full React test suite**

Run:

```powershell
npm test --prefix src/LightRAGNet.React -- --run
```

Expected: PASS for all React tests. The known npm warning about unknown CLI config `--run` is acceptable if tests pass.

- [ ] **Step 3: Run production build**

Run:

```powershell
npm run build --prefix src/LightRAGNet.React
```

Expected: PASS. The existing Vite large chunk warning is acceptable if it remains the only warning.

- [ ] **Step 4: Start the React dev server for visual QA**

Run:

```powershell
npm run dev --prefix src/LightRAGNet.React -- --host 127.0.0.1
```

Expected: Vite prints a local URL such as `http://127.0.0.1:5173/`.

- [ ] **Step 5: Compare `/system-status` against the committed prototype**

Open both:

```text
docs/superpowers/visuals/anthropic-light-workbench/05-system-status-compact-diagnostics-workbench-react-prototype.html
http://127.0.0.1:<vite-port>/system-status
```

Check:

- Right-side page content follows the prototype structure.
- The production app shell remains the existing app shell, not the prototype shell.
- Summary tiles are compact and light.
- Evidence table is the primary surface.
- Remediation and Feature Impact are side-stack panels.
- Raw JSON panel is secondary and scrollable.
- Icons are Lucide, not characters.
- No oversized health ring appears.

- [ ] **Step 6: Capture browser checks at target viewports**

Use Playwright or the existing browser QA workflow to inspect:

```text
1440 x 900
768 x 900
375 x 812
```

Expected:

- No incoherent overlap.
- No global horizontal page scroll at 375px.
- Table/raw JSON internal scroll is acceptable.
- Text fits buttons and tiles.
- Focus and hover states remain visible.

- [ ] **Step 7: Run whitespace check**

Run:

```powershell
git diff --check
```

Expected: no output.

- [ ] **Step 8: Commit final verification docs only if needed**

If visual QA produces preserved screenshots or notes requested by the reviewer, commit them with:

```powershell
git add <requested-visual-artifacts>
git commit -m "docs: add System Status visual QA evidence"
```

If no preserved artifacts are requested, do not create an extra docs-only commit.

## Implementation Handoff

After Task 7 passes, summarize:

```text
Summary:
- Refactored /system-status into a compact diagnostics workbench.
- Preserved /api/system/health contract and copy/refresh/error behavior.
- Added page-local summary/evidence/remediation/impact/raw JSON components.
- Aligned System Status CSS with the committed React/Lucide prototype.

Verification:
- npm test --prefix src/LightRAGNet.React -- --run
- npm run build --prefix src/LightRAGNet.React
- Browser visual QA at 1440, 768, and 375 widths

Visual reference:
- docs/superpowers/visuals/anthropic-light-workbench/05-system-status-compact-diagnostics-workbench-react-prototype.html
```

## Plan Self-Review

- Spec coverage:
  - API behavior preserved: Task 4 keeps `getSystemHealth`, copy, refresh, loading, and error behavior.
  - Visual reference preserved: Task 1 commits the React/Lucide prototype and Task 7 compares against it.
  - Compact summary tiles: Task 3 and Task 5.
  - Evidence table as primary surface: Task 3 and Task 5.
  - Remediation, feature impact, raw JSON panels: Task 3 and Task 5.
  - Lucide icons only: Task 3 component code uses `lucide-react`; Task 7 checks for no character placeholders.
  - Guardrails: Task 5 removes old System Status root font/local UI debt allowances.
  - Docs: Task 6.
- Scope check:
  - No backend/API changes.
  - No app shell rewrite.
  - No charting library.
  - No Cache/RAG Chat/Graph migration.
- Type consistency:
  - `getStatusTone`, `formatDurationMs`, `formatGeneratedAt`, `summarizeEvidence`, and `formatHealthJson` are defined in Task 1 and used by later components.
  - Component names match the spec and file structure.
