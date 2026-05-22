# Graph Workbench Functional Parity Archives

- Date: `2026-05-22`
- Topic slug: `graph-workbench-functional-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `knowledge-graph`, `sigma`, `graph-settings`, `source-declaration`

## Summary

本轮交付继续对齐 Python LightRAG 图谱工作台，重点修复用户验收指出的节点大小语义和交互后图谱消失问题，并补齐一批高价值功能入口。

实现从 Python LightRAG 的 `useLightragGraph.tsx`、`GraphControl.tsx`、`FocusOnNode.tsx`、`Settings.tsx`、`GraphLabels.tsx` 和 `PropertiesView.tsx` 中提取关键行为：节点按 degree 缩放，边按 weight 缩放，hover 只高亮不移动相机，搜索/关系跳转才移动视角，设置项即时影响 Sigma 渲染。

## Delivered Scope

- 节点大小改为 Python-style degree scaling：关系越多节点越大，范围 `4..20`，平方根缩放。
- 边宽改为 Python-style weight scaling：从关系 `properties.weight` 读取并映射到配置范围。
- 修正相机移动边界：hover/focus 不再移动相机；搜索结果和 relationships 跳转才触发 `moveToSelectedNode`。
- 增加拖拽时 custom bounding box 保护，减少拖动节点后画布缩放/位置异常。
- 增加 Settings 浮层：节点标签、边标签、edge events、隐藏非关联边、边大小 min/max、layout iterations、max nodes。
- Label 控件接入 `/api/graph/labels` native datalist，并保留 `*` 全局查询和刷新当前图谱。
- 增加 Fullscreen 控制。
- 属性浮层补 relationships 列表，点击邻居会选中并移动到该节点。

## Out of Scope

- 未全量移植 Python LightRAG 的 i18n、搜索历史和 pipeline busy 自动刷新。
- 未实现 expand/prune 后端联动。
- 未替换 Blazor 全站；当前仍是 Blazor host + React island。
- 未引入真实数据库/图存储集成测试。

## Verification Snapshot

- RED verification: `npm test -- src/components/graph/graphologyAdapter.test.ts` initially failed because high-degree node size was `8` instead of `20`, and low-weight edge size was `1.75` instead of `1.25`.
- RED verification: `npm test -- src/stores/graphStore.test.ts` initially failed because `moveToSelectedNode` and Python-style display settings did not exist.
- Frontend tests passed: `npm test` from `src/LightRAGNet.Web/ClientApp` (`31/31`).
- Frontend typecheck passed: `npm run typecheck`.
- Frontend build passed: `npm run build`, producing updated `wwwroot/graph-workbench` assets.
- Focused Web host tests passed: `dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphWorkbenchHostSourceTests" --verbosity minimal` (`2/2`).
- Dev smoke check passed: `.\scripts\dev-start.ps1 -SkipNpmInstall -SkipClientBuild -ReadyTimeoutSeconds 90`.
- Visual check passed: Playwright screenshots `output/playwright/graph-view-functional-parity.png` and `output/playwright/graph-view-functional-parity-v2.png` show degree-scaled nodes, visible settings/fullscreen controls, and stable graph rendering.

## Source Documents

- Spec: [graph workbench functional parity design](../../specs/2026-05-22-graph-workbench-functional-parity-design.md)
- Visual: None found for this topic; local verification screenshots were captured under `output/playwright/`.
- Plan: [graph workbench functional parity implementation plan](../../plans/2026-05-22-graph-workbench-functional-parity-implementation-plan.md)

## Related Problems

- None.

## Reference Source Declaration

本实现明确参考 Python LightRAG 源码仓库：

- Source repository: `https://github.com/HKUDS/LightRAG`
- Local reference copy: `LightRAG/`
- Referenced frontend areas:
  - `LightRAG/lightrag_webui/src/hooks/useLightragGraph.tsx`
  - `LightRAG/lightrag_webui/src/features/GraphViewer.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/GraphControl.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/FocusOnNode.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/Settings.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/GraphLabels.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/PropertiesView.tsx`

本仓库实现参考并移植了 Python LightRAG 的节点 degree size、边 weight size、相机移动边界、图谱设置项、标签查询和属性关系展示语义；不声称该图谱交互设计完全原创。

## Notes

- Settings 是轻量版 parity，只覆盖当前最影响可用性的图谱显示和布局参数。
- expand/prune 仍应作为后续后端能力对齐，不应在前端做假按钮。
