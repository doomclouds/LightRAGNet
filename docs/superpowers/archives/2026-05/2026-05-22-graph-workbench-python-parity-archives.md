# Graph Workbench Python Parity Archives

- Date: `2026-05-22`
- Topic slug: `graph-workbench-python-parity`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `knowledge-graph`, `react-island`, `sigma`, `source-declaration`

## Summary

本轮交付把 LightRAGNet 的 Knowledge Graph tab 从“能加载图的管理表单”改造成接近 Python LightRAG 的整屏知识图谱工作台。核心变化是让 React 岛内部拥有完整图谱体验：Sigma 画布铺满工作区，查询/搜索、布局/视角、图例和属性面板都变成画布浮层，而不是挤占图谱空间的固定页面栏。

实现明确参考 Python LightRAG WebUI 的图谱工作台结构，尤其是 `GraphViewer`、`GraphControl`、布局控制、搜索、标签和属性面板相关实现；同时保留 LightRAGNet 已有的实体/关系编辑、删除和合并提示能力。

## Delivered Scope

- 新增 Python 风格 graph shell：顶左查询/搜索浮层、左下布局/视角 dock、右上悬浮属性面板、右下图例。
- 对齐 Sigma 渲染：曲线无箭头边、节点边框 program、edge events、节点/边 reducer、邻居高亮、选中边高亮和非关联元素弱化。
- 补齐布局入口：Force Atlas、Force Directed、Noverlap、Random、Circular。
- 补齐图谱搜索：按节点 id、label、type 和常见属性过滤，点击结果后选中并聚焦节点。
- 保留既有编辑能力：属性编辑、实体/关系删除、实体合并提示弹窗仍由当前 React island 承担。
- 新增 spec/plan 资产，并在完成归档里声明 Python LightRAG 参考来源。

## Out of Scope

- 未全量移植 Python LightRAG WebUI 的 settings store、i18n、pipeline busy 监听和搜索历史。
- 未实现 expand/prune 的后端联动。
- 未把 Blazor 全站迁移为 React；Blazor 仍只是当前阶段的宿主。
- 未加入真实 Qdrant/Neo4j 集成测试；数据库环境仍按本阶段约束不处理。

## Verification Snapshot

- Frontend tests passed: `npm test` from `src/LightRAGNet.Web/ClientApp` (`27/27`).
- Frontend typecheck passed: `npm run typecheck` from `src/LightRAGNet.Web/ClientApp`.
- Frontend build passed: `npm run build` from `src/LightRAGNet.Web/ClientApp`, producing `wwwroot/graph-workbench` assets.
- Focused Web host tests passed: `dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphWorkbenchHostSourceTests" --verbosity minimal` (`2/2`).
- Dev script smoke check passed: `.\scripts\dev-start.ps1 -SkipNpmInstall -SkipClientBuild -ReadyTimeoutSeconds 90` started Server/Web and reported `/graph-view` ready.
- Visual check passed: Playwright screenshot `output/playwright/graph-view-python-parity.png` confirmed `/graph-view` renders a force-directed knowledge graph surface with floating controls and no fixed right column.

## Source Documents

- Spec: [graph workbench python parity design](../../specs/2026-05-22-graph-workbench-python-parity-design.md)
- Visual: None found for this topic; local verification screenshot was captured at `output/playwright/graph-view-python-parity.png`.
- Plan: [graph workbench python parity implementation plan](../../plans/2026-05-22-graph-workbench-python-parity-implementation-plan.md)

## Related Problems

- None.

## Reference Source Declaration

本实现明确参考 Python LightRAG 源码仓库：

- Source repository: `https://github.com/HKUDS/LightRAG`
- Local reference copy: `LightRAG/`
- Referenced frontend areas:
  - `LightRAG/lightrag_webui/src/features/GraphViewer.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/GraphControl.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/LayoutsControl.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/ZoomControl.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/GraphSearch.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/GraphLabels.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/PropertiesView.tsx`
  - `LightRAG/lightrag_webui/src/stores/graph.ts`

本仓库实现参考并移植了 Python LightRAG 的图谱工作台视觉骨架、Sigma 交互方式、布局菜单、搜索/聚焦、属性浮层和选中高亮语义；不声称该图谱工作台设计完全原创。

## Notes

- React island 仍保持可迁移边界，Blazor 只负责挂载和传入 `ApiBaseUrl`。
- 当前 parity shell 优先解决真实使用感；Python 版的 expand/prune 和完整 settings 可作为下一阶段继续对齐。
