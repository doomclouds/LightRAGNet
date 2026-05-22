# Graph Curation React Workbench Archives

- Date: `2026-05-21`
- Topic slug: `graph-curation-react-workbench`
- Status: `Archived`
- Scope: `Feature`
- Tags: `lightrag-alignment`, `graph-curation`, `react-island`, `blazor-host`, `source-declaration`

## Summary

本轮交付把原本薄弱的 Blazor/Sigma 图谱页升级为由 Blazor Web 承载的 React/Vite 图谱工作台，并补齐实体/关系创建、编辑、删除和合并所需的图谱治理 API。该工作台按未来 React 迁移边界组织，UI 结构和产品语义明确参考 Python LightRAG 的图谱工作台，而不是声称本仓库完全原创设计。

## Delivered Scope

- 新增 React/Vite graph workbench island，由 `LightRAGNet.Web` 的 Blazor 页面作为临时宿主加载构建产物。
- 引入图谱搜索/加载、节点选择、边选择和属性面板等工作台体验，图谱缩放使用 Sigma 画布原生交互能力。
- 补齐实体与关系的 create/edit/delete/merge API 语义，并让前端可以通过属性面板、合并弹窗和确认弹窗完成治理操作。
- 将图谱治理操作与 graph store、vector store、KV metadata、tracking KV 和 workspace query revision 的一致性边界纳入实现与测试。
- 在 README 中声明 React graph workbench 的 Node/Vite 构建步骤和 Blazor Web 启动方式。

## Out of Scope

- 未迁移全站 Web 到 React，也未替换 Chat、Documents、Layout 等 Blazor 页面。
- 未实现多用户实时协同、undo/redo、自动实体去重推荐或复杂 schema designer。
- 未要求首版完全保留 Python LightRAG WebUI 的全部设置项、国际化和视觉细节。
- 未加入默认会访问真实 Qdrant/Neo4j 的集成测试；真实外部存储仍需显式 opt-in 验证。

## Verification Snapshot

- Focused core tests passed: `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphCuration" --verbosity minimal` (`34/34`).
- Focused server tests passed: `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphController" --verbosity minimal` (`11/11`).
- Focused web host tests passed: `dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphWorkbenchHostSourceTests" --verbosity minimal` (`2/2`).
- Frontend tests passed: `npm test` from `src/LightRAGNet.Web/ClientApp` (`22/22`).
- Frontend build passed: `npm run build` from `src/LightRAGNet.Web/ClientApp`.
- Solution tests passed: `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal` (`LightRAGNet.Tests 390/390`, `LightRAGNet.Server.Tests 77/77`, `LightRAGNet.Web.Tests 22/22`).
- Solution build passed: `dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal` (`0` warning, `0` error).
- Archive validation passed: `python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\archive-superpowers-feature\scripts\validate_archive_asset.py docs/superpowers/archives/2026-05/2026-05-21-graph-curation-react-workbench-archives.md`.
- Index validation passed: `python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\check_indexes.py . --json`.

## Source Documents

- Spec: [graph curation react workbench design](../../specs/2026-05-21-graph-curation-react-workbench-design.md)
- Visual: None found for this topic.
- Plan: [graph curation react workbench implementation plan](../../plans/2026-05-21-graph-curation-react-workbench-implementation-plan.md)

## Related Problems

- None.

## Reference Source Declaration

本实现明确参考 Python LightRAG 源码仓库：

- Source repository: `https://github.com/HKUDS/LightRAG`
- Local reference copy: `LightRAG/`
- Referenced frontend areas:
  - `LightRAG/lightrag_webui/src/features/GraphViewer.tsx`
  - `LightRAG/lightrag_webui/src/hooks/useLightragGraph.tsx`
  - `LightRAG/lightrag_webui/src/stores/graph.ts`
  - `LightRAG/lightrag_webui/src/stores/settings.ts`
  - `LightRAG/lightrag_webui/src/components/graph/PropertiesView.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/EditablePropertyRow.tsx`
  - `LightRAG/lightrag_webui/src/components/graph/MergeDialog.tsx`
  - `LightRAG/lightrag_webui/src/api/lightrag.ts`
- Referenced backend areas:
  - `LightRAG/lightrag/api/routers/graph_routes.py`
  - `LightRAG/lightrag/lightrag.py`
  - `LightRAG/lightrag/utils_graph.py`

本仓库实现参考并移植了 Python LightRAG 的产品语义、图谱编辑合同、属性面板结构和实体合并交互语义；不声称图谱工作台设计完全原创。

## Notes

- React graph workbench 保持独立于 Blazor 组件内部状态，便于后续从 island 平滑迁移为主 React 前端模块。
- README 中的 `npm install` / `npm run build` 是显式开发步骤；`dotnet build` 仍不强制触发 Node 构建，避免没有 Node 的后端测试环境被前端工具链阻断。

## Post-Archive Fixes

- `2026-05-22`: 修复真实图谱数据中 `node.type = "concept"` / `edge.type = "related"` 被直接传给 Sigma renderer `type` 后导致 Knowledge Graph tab 空白的问题；业务类型改存为 Graphology `domainType`，Sigma 的视觉 renderer type 保持默认。
- 同步修复 `scripts/dev-start.ps1` 的 ready 等待逻辑：启动脚本现在会轮询 Server/Web URL，并接受 Server 根路径 `404` 作为“进程已可响应”的 ready 信号，避免过早打印可访问地址。
- 追加验证：`npm test` (`23/23`)、`npm run build`、`dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --verbosity minimal` (`27/27`)、Playwright 访问 `http://localhost:5241/graph-view` 后无 Sigma console error，且 `graph-workbench` / Sigma container / canvas 已挂载。
- `2026-05-22`: 继续修复用户验收反馈中的“图谱像圆环/散点图，不像知识图谱”问题；对齐 Python LightRAG 的 ForceAtlas2 布局方向，引入 `@react-sigma/layout-forceatlas2`，将圆周初始布局改为稳定随机撒点后自动 ForceAtlas2 布局，并加深/加粗边、降低节点标签显示阈值。
- 同步修复后端 `GraphViewController` 只处理 `null` edge id、不处理空字符串的问题；真实 API 中空 edge id 会导致前端 Graphology edge key 冲突，最终大量边被跳过。现在后端为空 edge id 生成稳定 `source->target:index`，前端也做同样兜底。
- 追加验证：TDD 红灯覆盖圆周布局和空 edge id 两类问题；`npm test` (`25/25`)、`npm run build`、`dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphControllerTests"` (`11/11`)、`dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --verbosity minimal` (`27/27`) 通过；Playwright 截图确认 `http://localhost:5241/graph-view` 已显示节点、边和标签。
- `2026-05-22`: 针对“整体效果不像 Python 版”的验收反馈，补做 Python parity graph shell：把 React 岛从“顶部表单 + 右侧固定属性栏”重构为“整屏 Sigma 画布 + 顶左查询/搜索浮层 + 左下布局/视角 dock + 右上悬浮属性面板 + 右下图例”的 Python LightRAG 风格工作台。
- 本次更明确参考 Python LightRAG 的 `GraphViewer.tsx`、`GraphControl.tsx`、`LayoutsControl.tsx`、`ZoomControl.tsx`、`GraphSearch.tsx`、`GraphLabels.tsx` 和 `PropertiesView.tsx`；移植了曲线边、节点边框、ForceAtlas/Force/Noverlap/Random/Circular 布局入口、搜索选中、hover/focus/selection reducer、邻居高亮、图例浮层和拖拽节点行为。
- 追加验证：`npm test` (`27/27`)、`npm run typecheck`、`npm run build`、`dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~GraphWorkbenchHostSourceTests" --verbosity minimal` (`2/2`) 通过；`scripts/dev-start.ps1 -SkipNpmInstall -SkipClientBuild` 启动后，Playwright 截图 `output/playwright/graph-view-python-parity.png` 确认 `/graph-view` 已呈现力导向知识图谱工作台视觉。
