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
