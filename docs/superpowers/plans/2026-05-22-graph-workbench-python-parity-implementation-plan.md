# Graph Workbench Python Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework the LightRAGNet graph workbench into a Python LightRAG-style full-canvas knowledge graph editor.

**Architecture:** Keep Blazor as a thin host and make the React island own the graph workspace. Move graph controls into focused React components and use Sigma reducers/store state for hover, focus, selection, and layout behavior.

**Tech Stack:** React 19, Vite, TypeScript, Sigma v3, react-sigma v5, graphology, LightRAGNet ASP.NET Core graph APIs.

---

### Task 1: Dependencies And Graph Store

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/package.json`
- Modify: `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.ts`
- Modify: `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.test.ts`

- [ ] Add Sigma parity dependencies: `@sigma/edge-curve`, `@sigma/node-border`, layout packages, and `lucide-react`.
- [ ] Extend graph state with focused node/edge, selected ids, sigma instance, and display toggles needed by reducers.
- [ ] Add focused store tests for hover/focus reset and selection exclusivity.

### Task 2: Full Canvas Workbench Shell

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/src/graph-workbench/GraphWorkbench.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/styles/graph-workbench.css`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/PropertiesPanel.tsx`

- [ ] Replace fixed toolbar/right-column layout with full-canvas shell.
- [ ] Move query controls into a top-left translucent overlay.
- [ ] Make properties panel a right-top floating panel that renders only for selected or focused graph elements.
- [ ] Preserve existing edit/delete/merge dialogs.

### Task 3: Sigma Rendering Parity

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphCanvas.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/graphologyAdapter.ts`

- [ ] Use Python-style Sigma settings: curved no-arrow edges, node border program, edge events, label thresholds.
- [ ] Apply ForceAtlas2 after load and keep deterministic seeded starting positions.
- [ ] Register node/edge hover and click events.
- [ ] Add reducers for selection, focus, neighbor highlight, edge highlight, and faded unrelated nodes.

### Task 4: Overlay Controls

**Files:**
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphQueryControls.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphViewportControls.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphLayoutControls.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphLegend.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphSearchBox.tsx`

- [ ] Add top-left label/depth/max-nodes/load controls.
- [ ] Add node search that selects and focuses matching nodes.
- [ ] Add bottom-left icon dock for layout, rotate, reset, zoom, and legend toggle.
- [ ] Add simple type-color legend from current graph attributes.

### Task 5: Verification And Asset Closeout

**Files:**
- Modify: `src/LightRAGNet.Web/wwwroot/graph-workbench/assets/graph-workbench.js`
- Modify: `src/LightRAGNet.Web/wwwroot/graph-workbench/assets/graph-workbench.css`
- Modify: `docs/superpowers/archives/2026-05/2026-05-21-graph-curation-react-workbench-archives.md`

- [ ] Run `npm test`, `npm run typecheck`, and `npm run build` in `src/LightRAGNet.Web/ClientApp`.
- [ ] Run focused Web host tests.
- [ ] Start dev scripts and verify `/graph-view` with browser screenshot.
- [ ] Update archive with Python reference source declaration and verification evidence.
