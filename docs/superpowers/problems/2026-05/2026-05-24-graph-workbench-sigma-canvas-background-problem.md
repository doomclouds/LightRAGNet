# Graph Workbench Sigma Canvas Background Problem

- Date: `2026-05-24`
- Topic slug: `graph-workbench-sigma-canvas-background`
- Status: `Captured`
- Scope: `UI`
- Tags: `react-sigma`, `graph-workbench`, `dark-ops`, `canvas`, `visual-qa`

## Symptom

Knowledge Graph 在暗色模式下看起来没有加载任何内容：查询控件、搜索框和 SignalR 状态正常，画布是深色，但节点和边完全不可见。

## Trigger / Context

- React graph workbench 使用 `@react-sigma/core`，Sigma 会创建多层 canvas：edges、nodes、labels、hovers、mouse 等。
- 为了修掉 Sigma 默认白底，CSS 把 `.graph-workbench__sigma canvas` 也设置成 `background: var(--app-bg)`。
- API 实际返回了节点和边，前端空态覆盖层也没有出现。

## Root Cause

Sigma 的 canvas 层依赖透明背景叠加渲染。把背景色设置到每一层 canvas 后，位于上层的空白交互层和 hover 层会用深色背景覆盖下面已经绘制好的 nodes/edges 层。结果不是图没加载，而是图被上层 canvas 自己盖住了。

## Fix

- 深色背景只保留在 Sigma React 容器和 `.sigma-container` 上。
- `.graph-workbench__sigma canvas` 改为 `background: transparent`，让多层 canvas 正常叠加。
- 更新 `reactPageThemeUsage.test.ts`，锁住“容器深色、canvas 透明”的约束。
- 用 Playwright 验证 `/graph-view`：`/api/graph/query` 返回 `100` 个节点和 `79` 条边，所有 Sigma canvas computed background 为透明，截图能看到力导向图谱。

## Why This Fix

修 API、重载图谱或调整节点颜色都不能解决上层 canvas 覆盖问题。正确边界是让容器承载底色，让 Sigma 的渲染层保持透明；这符合 Sigma 多 canvas 的渲染模型，也保留暗色主题视觉。

## Recognition Clues

- Network 里 `/api/graph/query` 是 `200 OK`，JSON 里 `nodes.length > 0`。
- 页面没有显示 `No graph loaded` 或错误覆盖层。
- DevTools / Playwright 中能看到多个 `.graph-workbench__sigma canvas`，且最上层 canvas 的 computed background 不是透明。
- 取消 canvas 背景后，节点和边立刻出现。

## Applicability / Non-Applicability

### Applies When

- React/Sigma、多层 canvas、WebGL/Canvas 混合渲染的图谱或可视化页面出现“数据已到但画布空白”。
- 近期改过暗色模式、背景色、canvas 或容器样式。

### Does Not Apply When

- API 返回空节点；这时应排查 Neo4j/GraphStore/查询条件，并会走页面空态。
- 控制台有 WebGL context lost、shader、program class 或 Sigma settings 重建错误；这类问题应先看 Sigma settings instability problem。
- 图谱只是在搜索聚焦后飞出视野；这类问题应看 camera focus coordinate race problem。

## Related Artifacts

- Spec: [React UI Standardization and RAG Chat Workbench Design](../../specs/2026-05-24-react-ui-standardization-rag-chat-workbench-design.md)
- Plan: [React UI Standardization and RAG Chat Workbench Implementation Plan](../../plans/2026-05-24-react-ui-standardization-rag-chat-workbench-implementation-plan.md)
- Archive: [React UI Standardization and RAG Chat Workbench Archive](../../archives/2026-05/2026-05-24-react-ui-standardization-rag-chat-workbench-archives.md)
- Related Problems:
  - [Graph Workbench Sigma Settings Instability Problem](./2026-05-22-graph-workbench-sigma-settings-instability-problem.md)
  - [Graph Workbench Camera Focus Coordinate Race Problem](./2026-05-22-graph-workbench-camera-focus-coordinate-race-problem.md)
- Code or Test:
  - [graph-workbench.css](../../../../src/LightRAGNet.Web/ClientApp/src/styles/graph-workbench.css)
  - [reactPageThemeUsage.test.ts](../../../../src/LightRAGNet.Web/ClientApp/src/styles/reactPageThemeUsage.test.ts)
