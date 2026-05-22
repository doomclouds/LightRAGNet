# Graph Workbench Functional Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the graph workbench validation gaps and bring node sizing, camera behavior, settings, labels, fullscreen, and property relationships closer to Python LightRAG.

**Architecture:** Keep the Blazor host thin. Add parity behavior inside the React island through focused graph helpers, store settings, and small overlay controls. Use tests for graph sizing and state boundaries before changing rendering code.

**Tech Stack:** React 19, TypeScript, Sigma v3, react-sigma v5, graphology, LightRAGNet graph APIs.

---

### Task 1: Graph Sizing Semantics

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/graphologyAdapter.ts`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/graphologyAdapter.test.ts`

- [ ] Add failing tests showing high-degree nodes are larger than low-degree nodes.
- [ ] Add failing tests showing relation `properties.weight` controls edge size.
- [ ] Implement Python-style degree and weight scaling.

### Task 2: Camera And Selection Boundary

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.ts`
- Modify: `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.test.ts`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphCanvas.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphSearchBox.tsx`

- [ ] Add failing store tests for `moveToSelectedNode`.
- [ ] Make hover focus never move the camera.
- [ ] Let search/relationship navigation select with move intent.
- [ ] Add drag custom bounding-box behavior matching Python.

### Task 3: Settings And Labels

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/src/stores/graphSettingsStore.ts`
- Modify: `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.test.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphSettingsPanel.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphQueryControls.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphCanvas.tsx`

- [ ] Add settings defaults and setters for Python-style graph controls.
- [ ] Add a Settings overlay for labels, edge labels, hide unrelated edges, edge size, layout iterations, and max nodes.
- [ ] Load `/api/graph/labels` into a native datalist and refresh the current graph.

### Task 4: More Python Parity Controls

**Files:**
- Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphFullscreenControl.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphViewportControls.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/PropertiesPanel.tsx`

- [ ] Add fullscreen toggle.
- [ ] Add relationships list to node properties.
- [ ] Ensure relationship click selects and moves to the neighbor.

### Task 5: Verification And Archive

**Files:**
- Modify: `src/LightRAGNet.Web/wwwroot/graph-workbench/assets/graph-workbench.js`
- Modify: `src/LightRAGNet.Web/wwwroot/graph-workbench/assets/graph-workbench.css`
- Create/modify: `docs/superpowers/archives/2026-05/*`

- [ ] Run `npm test`, `npm run typecheck`, `npm run build`.
- [ ] Run focused Web host tests.
- [ ] Start dev scripts and capture a Playwright screenshot after hover/controls.
- [ ] Update archive with reference-source declaration and verification evidence.
