# Graph Workbench Python Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for completion tracking.

**Goal:** 把 LightRAGNet Knowledge Graph tab 收敛为一个 Python LightRAG 风格的 React graph workbench：整屏 Sigma 画布、浮层控件、力导向布局、节点/边语义缩放、属性/关系交互、服务端配置化查询边界，以及可诊断的图谱加载稳定性。

**Architecture:** Blazor 只保留 thin host；React island 内部分层为 API client、Zustand stores、Graphology adapter、Sigma canvas、overlay controls 和 properties/editing panels。服务端负责图谱查询、labels、config 和参数验证。图谱渲染引用必须稳定，避免 React render 触发 Sigma 实例反复销毁/重建。

**Tech Stack:** .NET 10, ASP.NET Core, Blazor Server host, React 19, Vite, TypeScript, Sigma v3, react-sigma v5, graphology, Neo4j graph store.

---

## Task 1: React Island And Host Boundary

**Files:**
- Modify: `src/LightRAGNet.Web/Components/Pages/GraphView.razor`
- Modify: `src/LightRAGNet.Web/wwwroot/graph-workbench/assets/*`
- Modify: `tests/LightRAGNet.Web.Tests/GraphWorkbenchHostSourceTests.cs`

- [x] Keep Blazor as a thin host with `graph-workbench-root`.
- [x] Pass API base through `data-api-base`.
- [x] Mount/unmount React island through JS module lifecycle.
- [x] Assert build artifacts exist and host does not inline module scripts.

## Task 2: Full-Canvas Python-Style Workbench Shell

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/src/graph-workbench/GraphWorkbench.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/styles/graph-workbench.css`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/PropertiesPanel.tsx`

- [x] Replace fixed toolbar/right-column layout with full-canvas graph surface.
- [x] Move label/depth/max-nodes/load controls into a top-left overlay.
- [x] Move search below the query overlay.
- [x] Make properties panel float over the graph and render only for selected/focused elements.
- [x] Preserve property edit, delete, relation delete, and merge confirmation flows.

## Task 3: Graph Store And Settings Store

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.ts`
- Modify: `src/LightRAGNet.Web/ClientApp/src/stores/graphSettingsStore.ts`
- Modify: `src/LightRAGNet.Web/ClientApp/src/stores/graphStore.test.ts`

- [x] Track selected/focused node and edge independently.
- [x] Store Sigma instance for viewport/layout controls.
- [x] Add `moveToSelectedNode` so hover focus never moves camera.
- [x] Add graph display settings: node labels, edge labels, edge events, hide unrelated edges, edge size range, layout iterations, and max nodes.
- [x] Add `maxNodesLimit` and clamp current `maxNodes` when service config changes.
- [x] Cover store defaults, setters, selection exclusivity, and max-node clamping with tests.

## Task 4: Graphology Adapter And Python Visual Semantics

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/graphologyAdapter.ts`
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/graphologyAdapter.test.ts`

- [x] Seed non-circular deterministic starting positions before layout.
- [x] Keep backend node/edge domain type in `domainType` instead of Sigma renderer `type`.
- [x] Use stable fallback edge keys for empty backend edge ids.
- [x] Scale node size by relationship degree using Python-style `4..20` square-root mapping.
- [x] Scale edge size from relation `properties.weight` using settings min/max.
- [x] Cover non-circular seed, renderer/domain type separation, blank edge ids, degree scaling, and weight scaling.

## Task 5: Sigma Canvas Rendering And Interaction

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphCanvas.tsx`
- Modify: `tests/LightRAGNet.Web.Tests/GraphWorkbenchHostSourceTests.cs`

- [x] Use Sigma node border renderer and curved no-arrow edge renderer.
- [x] Register edge events and reducers for selected/focused node/edge.
- [x] Fade unrelated nodes/edges and optionally hide unrelated edges.
- [x] Apply ForceAtlas2 after graph load.
- [x] Add drag behavior with custom bounding-box protection.
- [x] Move camera only when `moveToSelectedNode` is explicitly set.
- [x] Keep renderer program classes and class maps as module-level stable constants.
- [x] Add source test guarding against `createEdgeCurveProgram()` being recreated inside JSX settings.

## Task 6: Overlay Controls

**Files:**
- Modify/Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphQueryControls.tsx`
- Modify/Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphSearchBox.tsx`
- Modify/Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphViewportControls.tsx`
- Modify/Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphLayoutControls.tsx`
- Modify/Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphSettingsPanel.tsx`
- Modify/Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphFullscreenControl.tsx`
- Modify/Create: `src/LightRAGNet.Web/ClientApp/src/components/graph/GraphLegend.tsx`

- [x] Add label/depth/max-nodes/refresh controls.
- [x] Load labels from `/api/graph/labels` into a native datalist.
- [x] Add node search over id, label, type, and common properties.
- [x] Add layout, rotate, reset, zoom, fullscreen, settings, and legend controls.
- [x] Add node relationships list and neighbor jump behavior in properties panel.

## Task 7: Server Config And Label Query Boundary

**Files:**
- Create: `src/LightRAGNet.Share/Models/GraphViewConfigDto.cs`
- Modify: `src/LightRAGNet.Server/Controllers/GraphController.cs`
- Modify: `src/LightRAGNet.Server/Controllers/GraphViewController.cs`
- Modify: `src/LightRAGNet.Server/appsettings.json`
- Modify: `src/LightRAGNet.Storage/Neo4jGraphStore.cs`
- Modify: `tests/LightRAGNet.Server.Tests/GraphControllerTests.cs`
- Modify: `tests/LightRAGNet.Server.Tests/LightRagServerFactory.cs`
- Create: `tests/LightRAGNet.Tests/Storage/Neo4jGraphStoreSourceTests.cs`

- [x] Add `GraphView:MaxNodesLimit` with default `2000`.
- [x] Add `/api/graph/config` returning `GraphViewConfigDto`.
- [x] Validate `maxNodes` against configured limit in both current and legacy graph controllers.
- [x] Let server tests override configuration through `LightRagServerFactory`.
- [x] Add tests for allowed configured max, rejected over-limit max, and config endpoint.
- [x] Fix Neo4j labels query by adding `WITH label` before filtering workspace labels.
- [x] Add source test to guard the Neo4j labels query shape.

## Task 8: Verification, Runtime Smoke, And Asset Closeout

**Files:**
- Modify: `src/LightRAGNet.Web/wwwroot/graph-workbench/assets/graph-workbench.js`
- Modify: `src/LightRAGNet.Web/wwwroot/graph-workbench/assets/graph-workbench.css`
- Modify: `docs/superpowers/specs/2026-05-22-graph-workbench-python-parity-design.md`
- Modify: `docs/superpowers/plans/2026-05-22-graph-workbench-python-parity-implementation-plan.md`
- Modify: `docs/superpowers/archives/2026-05/2026-05-22-graph-workbench-python-parity-archives.md`
- Create: `docs/superpowers/problems/2026-05/*`

- [x] Run `npm test`.
- [x] Run `npm run typecheck`.
- [x] Run `npm run build` and commit updated workbench assets.
- [x] Run focused Web host source tests.
- [x] Run focused GraphController tests.
- [x] Run Neo4j labels source regression test.
- [x] Start `scripts/dev-start.ps1` and verify `/graph-view`.
- [x] Verify `/api/graph/config`, `/api/graph/query?maxNodes=2000`, `/api/graph/query?maxNodes=2001`, and `/api/graph/labels`.
- [x] Verify fresh Playwright page load shows graph canvas with nodes/edges and no Sigma/WebGL console error.
- [x] Merge Python parity and functional parity assets into this single requirement line.
- [x] Archive recent knowledge graph blank-screen and labels-query failure modes as problem assets.

## Verification Commands

```powershell
npm test
npm run typecheck
npm run build
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphWorkbenchHostSourceTests" --verbosity minimal
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphControllerTests" --verbosity minimal
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~Neo4jGraphStoreSourceTests" --verbosity minimal
.\scripts\dev-start.ps1 -SkipNpmInstall -SkipClientBuild
```

## Runtime Smoke Checklist

- `/graph-view` loads a visible force-directed graph surface.
- Browser console has no Sigma/WebGL error.
- SignalR status reconnects to connected after service restart.
- `/api/graph/config` returns `maxNodesLimit = 2000`.
- `/api/graph/query?...maxNodes=2000` is accepted.
- `/api/graph/query?...maxNodes=2001` returns validation error.
- `/api/graph/labels` returns labels instead of 500.
