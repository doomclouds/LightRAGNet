# Graph Workbench Python Parity Archives

- Date: `2026-05-22`
- Topic slug: `graph-workbench-python-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `knowledge-graph`, `react-island`, `sigma`, `graph-settings`, `source-declaration`

## Summary

本轮交付把 LightRAGNet 的 Knowledge Graph tab 收敛为一个 Python LightRAG 风格的 React graph workbench：Blazor 只作为 thin host，React island 承担整屏 Sigma 画布、浮层控件、力导向布局、搜索/聚焦、属性/关系面板、节点/边视觉语义和图谱编辑入口。后续用户验收反馈中的“节点关系越多应越大”“拖动/操作后图谱消失”“max nodes 输入不生效”“图谱页空白”等问题，也纳入同一需求线完成修复和归档。

## Delivered Scope

- 将固定表单式页面重构为 Python LightRAG 风格整屏图谱工作台：顶左查询/搜索浮层、左下布局/视角 dock、右上属性面板、右下图例。
- 对齐 Sigma 视觉与交互：曲线无箭头边、节点边框 renderer、ForceAtlas2、hover/focus/selection reducer、邻居高亮、非关联元素弱化和边选中高亮。
- 对齐 Python 语义：节点按 degree 做 `4..20` 平方根缩放，边按 `properties.weight` 映射到配置化 min/max。
- 修正相机移动边界：hover/drag 不移动相机，搜索结果和 relationships 跳转才设置 move intent。
- 补齐高价值控件：布局、缩放、旋转、重置、Settings、Label datalist、Fullscreen、图例和 relationships 列表。
- 保留并整合既有编辑能力：属性编辑、实体/关系删除、实体合并确认仍在 React island 内可用。
- 增加服务端图谱配置：`GraphView:MaxNodesLimit` 默认 `2000`，`/api/graph/config` 暴露给前端，GraphController 和 legacy GraphViewController 都按配置校验 `maxNodes`。
- 修复 `/api/graph/labels` 的 Neo4j Cypher 查询，避免图谱页加载 label 控件时返回 500。
- 修复 React Sigma settings 不稳定导致的白屏：renderer program class 和 class map 改为模块级稳定常量，GraphReducers 不再重复注册 node program。
- 更新 React workbench 构建产物到 `wwwroot/graph-workbench`。

## Out of Scope

- 未全量移植 Python LightRAG WebUI 的完整 i18n、搜索历史持久化和 pipeline busy 自动刷新。
- 未实现 expand/prune 的后端联动。
- 未把 Blazor 全站替换为 React；当前仍是 Blazor host + React island。
- 未默认启用真实 Qdrant/Neo4j 集成测试。
- 未做 2000+ 节点性能专项优化；当前只提供配置上限和基础烟测。

## Verification Snapshot

- Frontend tests passed: `npm test` from `src/LightRAGNet.Web/ClientApp` (`33/33`).
- Frontend typecheck passed: `npm run typecheck`.
- Frontend build passed: `npm run build`, producing updated `wwwroot/graph-workbench` assets.
- Focused Web host tests passed: `dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphWorkbenchHostSourceTests" --verbosity minimal` (`3/3`).
- Focused Server tests passed: `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphControllerTests" --verbosity minimal` (`14/14`).
- Neo4j labels source regression passed: `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~Neo4jGraphStoreSourceTests" --verbosity minimal` (`1/1`).
- Runtime API smoke passed after `scripts/dev-start.ps1`: `/api/graph/config` returned `maxNodesLimit = 2000`; `/api/graph/query?...maxNodes=2000` was accepted; `/api/graph/query?...maxNodes=2001` returned validation error `maxNodes must be between 1 and 2000.`; `/api/graph/labels` returned labels instead of 500.
- Fresh Playwright verification passed: `/graph-view` showed a visible force-directed graph with nodes/edges, SignalR connected, and no Sigma/WebGL console error.
- Asset validation passed for this archive and the related problem assets; Superpowers indexes passed.

## Source Documents

- Spec: [graph workbench python parity design](../../specs/2026-05-22-graph-workbench-python-parity-design.md)
- Visual: None found for this topic; local verification screenshots were captured under `.playwright-cli/` and `output/playwright/` during implementation.
- Plan: [graph workbench python parity implementation plan](../../plans/2026-05-22-graph-workbench-python-parity-implementation-plan.md)

## Related Problems

- [Graph Workbench Sigma Settings Instability Problem](../../problems/2026-05/2026-05-22-graph-workbench-sigma-settings-instability-problem.md)
- [Neo4j Labels Unwind Filter Problem](../../problems/2026-05/2026-05-22-neo4j-labels-unwind-filter-problem.md)

## Reference Source Declaration

本实现明确参考 Python LightRAG 源码仓库：

- Source repository: `https://github.com/HKUDS/LightRAG`
- Local reference copy: `LightRAG/`
- Referenced frontend areas:
  - `LightRAG/lightrag_webui/src/features/GraphViewer.tsx`
  - `LightRAG/lightrag_webui/src/hooks/useLightragGraph.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/GraphControl.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/LayoutsControl.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/ZoomControl.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/GraphSearch.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/GraphLabels.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/Settings.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/FocusOnNode.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/PropertiesView.tsx`
  - `LightRAG/lightrag_webui/src/stores/graph.ts`

本仓库实现参考并移植了 Python LightRAG 的图谱工作台视觉骨架、Sigma 交互方式、布局菜单、搜索/聚焦、节点 degree size、边 weight size、相机移动边界、图谱设置项、标签查询和属性关系展示语义；不声称该图谱工作台设计完全原创。

## Notes

- React island 仍保持可迁移边界，后续可以把 Blazor host 替换为 React route，而不需要重写图谱核心组件。
- `@react-sigma/core` 会在 `settings` 深比较变化时重建 Sigma 实例；renderer class、program class map、`createEdgeCurveProgram()` 这类对象必须保持稳定。
- Max nodes 的默认上限是 `2000`，后续若真实图谱需要更大规模，应先调 `GraphView:MaxNodesLimit` 并观察 Neo4j 查询、Sigma 渲染和布局耗时。
