# Graph Curation React Workbench Design

- Date: `2026-05-21`
- Topic slug: `graph-curation-react-workbench`
- Status: `Ready for review`
- Scope: `Python-style graph workbench + graph curation APIs + React migration bridge`
- Tags: `lightrag-alignment`, `graph-curation`, `react-island`, `blazor-host`, `sigma`, `tdd`

## Purpose

LightRAGNet 当前的图谱页只是一个 Blazor 组件包着 CDN Sigma.js：能加载图、点节点、看一点属性，但交互薄、状态弱、扩展困难。继续在 `SigmaGraph.razor.js` 上补编辑能力，会把复杂图谱工作台写成一堆 JS interop 小补丁，后期迁 React 时还得重做一遍。

Python LightRAG WebUI 已经有成熟得多的图谱工作台：React、`@react-sigma/core`、graphology、搜索、标签、布局控制、节点拖拽、属性面板、编辑弹窗和合并弹窗。本阶段的目标不是用 Blazor 低配复刻，而是把图谱页作为 LightRAGNet 未来 React 迁移的第一块落点：在现有 Blazor Web 里嵌入 React/Vite graph workbench，并尽量复用 Python WebUI 的结构和交互语义。

本阶段同时补齐后端图谱治理能力，让用户可以真实完成“抽错了能改、重复了能合、脏数据能删”的闭环。图谱编辑必须能在 UI 中验收手感，不接受只落后端接口。

## Python Reference Semantics

Python LightRAG 的相关前端代码集中在 `LightRAG/lightrag_webui/src`：

- `features/GraphViewer.tsx`
  - `SigmaContainer` 承载图谱画布。
  - 集成搜索、标签、布局、缩放、全屏、图例、属性面板。
  - 支持节点拖拽和节点/边事件。
- `hooks/useLightragGraph.tsx`
  - 将 API 返回图数据转换为 graphology graph。
  - 管理 fetch、empty graph、局部扩展、剪枝、节点/边更新。
- `stores/graph.ts` 和 `stores/settings.ts`
  - 管理图谱状态、选中/聚焦节点、选中/聚焦边、布局和显示设置。
- `components/graph/PropertiesView.tsx`
  - 显示节点/边详情、邻居关系和可编辑属性行。
- `components/graph/EditablePropertyRow.tsx`
  - 调用 `updateEntity` / `updateRelation`。
  - 实体重命名时可检查重名并触发 merge。
  - 成功后更新前端 graph state 和搜索历史。
- `components/graph/MergeDialog.tsx`
  - 合并成功后让用户选择刷新到合并后实体，或保持当前起点刷新。
- `api/lightrag.ts`
  - 提供 `queryGraphs`、`getGraphLabels`、`checkEntityNameExists`、`updateEntity`、`updateRelation` 等 API wrapper。

后端 Python API 覆盖：

- `GET /graph/entity/exists`
- `POST /graph/entity/edit`
- `POST /graph/relation/edit`
- `POST /graph/entity/create`
- `POST /graph/relation/create`
- `POST /graph/entities/merge`
- document routes 中的 `delete_entity` / `delete_relation`

首版不要求逐文件照搬 Python WebUI，但应保留它的模块边界和关键体验：React graph workbench、属性面板编辑、合并确认、图谱刷新和状态同步。

## Current .NET Gap

当前 .NET 侧已有：

- `GraphViewController` 只提供 `GET /api/GraphView`。
- `GraphView.razor` 是 Blazor 页面，左侧设置、右侧简单画布。
- `SigmaGraph.razor.js` 动态加载 CDN graphology/sigma，并手动创建节点/边。
- `IGraphStore` 已有 node/edge get/upsert/delete、subgraph、label 查询基础。
- `IVectorStore` 已有 upsert/delete/get/query。
- 文档删除链路里已有实体/关系向量 id 计算、tracking KV 清理的一部分经验。

主要缺口：

- 没有实体/关系创建、编辑、合并和按实体/关系删除的后端服务边界。
- 没有 entity/relation vector 和 graph store 的一致性更新服务。
- 没有 `entity_chunks` / `relation_chunks` tracking KV 的编辑同步语义。
- 前端没有边选择、属性编辑、合并提示、局部刷新、搜索标签和布局工具。
- 当前图谱前端不是未来 React 迁移友好的资产。

## Product Decision

采用 `React Graph Island` 方案：

- 在 `src/LightRAGNet.Web` 下新增独立 React/Vite 子应用，首个入口是 graph workbench。
- Blazor 仍作为现阶段 Web 宿主和导航壳，Graph 页面嵌入 React island 的构建产物。
- React 代码按未来可独立迁移的方式组织，不把业务逻辑写在 Razor 或 JS interop 中。
- 旧 `GraphView.razor` 和 `SigmaGraph.razor.js` 不再继续扩展；首版可以保留为 fallback，但默认入口切到 React workbench。
- React workbench 尽量沿用 Python WebUI 的目录思想：`features/GraphWorkbench`、`components/graph`、`hooks`、`stores`、`api`。
- 后端 API 尽量贴近 Python 路由语义，前端 wrapper 只做最小适配。

这个方案的重点不是“引入 React 很时髦”，而是让这块复杂前端成为后续全站 React 化的先导模块。图谱工作台复杂度已经超过 Blazor 小组件适合承载的范围。

## Frontend Architecture

推荐目录：

```text
src/LightRAGNet.Web/
  ClientApp/
    package.json
    vite.config.ts
    tsconfig.json
    src/
      graph-workbench/
        main.tsx
        GraphWorkbench.tsx
      api/
        graphApi.ts
      components/
        graph/
          GraphCanvas.tsx
          GraphToolbar.tsx
          GraphSearch.tsx
          GraphLabels.tsx
          PropertiesPanel.tsx
          EditablePropertyRow.tsx
          PropertyEditDialog.tsx
          MergeDialog.tsx
          LayoutControls.tsx
          ZoomControls.tsx
      hooks/
        useGraphWorkbench.ts
      stores/
        graphStore.ts
        graphSettingsStore.ts
      types/
        graph.ts
```

Blazor 页面只负责：

- 提供一个 stable mount div，例如 `#graph-workbench-root`。
- 加载 Vite 构建出的 JS/CSS。
- 传入 API base path 和必要配置。
- 不参与图谱内部状态管理。

React 内部负责：

- 调用 `GET /api/graph/query` 或兼容路由加载子图。
- 用 graphology 构建 Sigma graph。
- 管理 selected/focused node/edge。
- 支持布局、缩放、全屏、搜索、标签、图例。
- 属性面板支持节点/边编辑。
- 合并成功后提供刷新策略选择。
- 保存后更新本地 graph state，并在必要时重新拉取当前视图。

首版 UI 不做花哨建模器，做“治理工作台”：

- 点节点看实体详情和邻居。
- 点边看关系详情。
- 编辑实体 `entity_name`、`entity_type`、`description`。
- 编辑关系 `description`、`keywords`、`weight`。
- 创建实体、创建关系、合并实体、删除实体、删除关系用明确按钮和确认弹窗承载。
- 大范围结构变化后刷新当前图谱，避免前端局部状态猜错。

## Backend Architecture

新增 `GraphCurationService`，位置建议：

```text
src/LightRAGNet/Services/GraphCuration/
```

职责：

- 读写 graph store。
- 维护 entity/relation vector store。
- 更新 `full_entities` / `full_relations` KV。
- 同步 `entity_chunks` / `relation_chunks` tracking KV。
- 对图谱治理操作做 per-entity/per-relation 串行保护。
- 成功修改图谱后 bump workspace query revision，避免查询答案缓存继续命中过时图谱。

推荐后端 API：

```text
GET    /api/graph/entity/exists?name=
GET    /api/graph/entity/{name}
GET    /api/graph/relation?source=&target=
POST   /api/graph/entity
POST   /api/graph/relation
PATCH  /api/graph/entity/{name}
PATCH  /api/graph/relation
POST   /api/graph/entities/merge
DELETE /api/graph/entity/{name}
DELETE /api/graph/relation?source=&target=
GET    /api/graph/query?label=&maxDepth=&maxNodes=
GET    /api/graph/labels
```

可以保留现有 `api/GraphView` 作为兼容入口，但 React workbench 使用新的 `/api/graph/*` 路由。

## Curation Contracts

### Entity edit

- 支持修改 `description`、`entity_type`、`entity_name`。
- `description` 不允许空白。
- 重命名时：
  - `allowRename=false` 返回 409 或 400。
  - 新名不存在时执行 rename。
  - 新名存在且 `allowMerge=false` 返回冲突。
  - 新名存在且 `allowMerge=true` 执行 merge。
- 更新 graph node 后必须重建 entity vector。
- 重命名后必须迁移与该实体相连的关系 vector，并更新 relation tracking key。

### Relation edit

- 支持修改 `description`、`keywords`、`weight`。
- `description` 不允许空白。
- 关系按无向边处理，source/target 需要规范化。
- 更新 graph edge 后必须重建 relation vector。
- 若 `source_id` 变更，更新 relation chunk tracking。

### Entity create

- 实体名必须唯一。
- 默认字段：
  - `entity_id = entity_name`
  - `entity_type = UNKNOWN`
  - `description`
  - `source_id = manual_creation`
  - `file_path = manual_creation`
- 创建 graph node 后写 entity vector 和 tracking KV。

### Relation create

- source 和 target 实体必须存在。
- 重复关系返回冲突。
- 默认 `weight = 1.0`。
- 创建 graph edge 后写 relation vector 和 tracking KV。

### Entity merge

- target entity 必须存在。
- source entities 必须存在且不能包含 target。
- 合并后删除 source nodes。
- source 关系迁移到 target，遇到重复关系时按 Python 思路合并 description、keywords、source_id、file_path 和 weight。
- 删除 source entity vectors，更新 target entity vector。
- 删除旧 relation vectors，写入新 relation vectors。
- 更新 entity/relation tracking KV。
- 返回 operation summary，供 UI 判断是否刷新到 target entity。

### Delete entity / relation

- 删除实体会删除实体 vector，并删除或迁移其相关关系。
- 删除关系会删除 relation vector 和 tracking KV。
- 删除操作必须有 UI 确认。
- 首版不做 undo；响应文案必须明确不可撤销。

## Data Consistency

图谱治理操作至少涉及四类存储：

- Graph store：Neo4j 或测试 double。
- Vector store：entities / relationships collection。
- KV store：`full_entities` / `full_relations`。
- Tracking KV：`entity_chunks` / `relation_chunks`。

首版采用 best-effort atomic stage 设计，不承诺跨存储分布式事务。服务必须按稳定顺序执行，并在失败时返回失败阶段和错误摘要。测试要覆盖“图已更新但 vector 更新失败”这类风险识别，避免静默成功。

成功完成结构性变更后，必须 bump workspace query revision：

- entity edit/create/delete
- relation edit/create/delete
- entity merge

这样 query answer cache 不会继续复用旧图谱答案。

## Migration Strategy

这块前端按未来 React 迁移来设计：

- React workbench 内部不依赖 Blazor 组件。
- API client 使用 fetch/axios wrapper，不依赖 Blazor `ApiClient`。
- 状态管理用 zustand 或等价轻量 store。
- 视觉系统优先复用 Python WebUI 的组件思想，但首版可用轻量 CSS，不强行引入整套 shadcn。
- Blazor 只作为临时 host，未来全站 React 化时可以直接把 `ClientApp/src` 提到主前端工程。
- 不把 React 组件打散塞进 Razor 文件。

构建接入建议：

- `ClientApp` 使用 Vite。
- `dotnet build` 不强制执行 npm build，避免没有 Node 的后端测试环境失败。
- Web 开发和发布脚本显式执行 React build。
- 构建产物输出到 `wwwroot/graph-workbench/`。
- README 或开发文档说明 Node 依赖和构建命令。

## Reference Source Declaration

本阶段明确参考 Python LightRAG 源码仓库：

- Source repository: `https://github.com/HKUDS/LightRAG`
- Local reference copy: `LightRAG/`
- Referenced frontend areas:
  - `LightRAG/lightrag_webui/src/features/GraphViewer.tsx`
  - `LightRAG/lightrag_webui/src/hooks/useLightragGraph.tsx`
  - `LightRAG/lightrag_webui/src/stores/graph.ts`
  - `LightRAG/lightrag_webui/src/stores/settings.ts`
  - `LightRAG/lightrag_webui/src/components/graph/*`
  - `LightRAG/lightrag_webui/src/api/lightrag.ts`
- Referenced backend areas:
  - `LightRAG/lightrag/api/routers/graph_routes.py`
  - `LightRAG/lightrag/lightrag.py`
  - `LightRAG/lightrag/utils_graph.py`

实现和归档都必须保留这个声明。最终 archive 需要单独写出“参考了 Python LightRAG 的图谱工作台 UI、图谱 API 合同、实体/关系编辑与合并语义”，避免把大量参考来源隐去。

## Out of Scope

- 不迁移全站 Web 到 React。
- 不重写 Chat、Documents、Layout 等 Blazor 页面。
- 不做多用户实时协同编辑。
- 不做 undo/redo。
- 不做复杂拖拽建模器或可视化 schema designer。
- 不做自动实体去重推荐。
- 不要求首版完全保留 Python WebUI 的全部设置项和国际化。
- 不接真实外部 graph provider 的集成测试。

## Testing Strategy

Use strict TDD for backend curation behavior. Frontend tests以可维护为先，覆盖 API wrapper、state reducer 和关键源码结构，不追求端到端全覆盖。

Backend tests:

- entity exists returns true/false from graph store。
- create entity writes graph node, entity vector, full entity KV, tracking KV。
- edit entity description updates graph and entity vector。
- rename entity updates graph node identity and related relation vectors。
- rename to existing entity without allow merge returns conflict。
- rename to existing entity with allow merge returns operation summary。
- edit relation updates graph edge and relation vector。
- create relation requires both endpoints。
- merge entities transfers relationships and deletes source entity vectors。
- delete relation deletes graph edge, relation vector, and tracking KV。
- delete entity deletes node and related vectors/tracking records。
- successful mutations bump query revision。

Server/API tests:

- API route names and request/response contracts match React API client。
- validation failures return 400/409, not 500。
- destructive operations require explicit endpoint and return clear result。

Frontend tests:

- graph API client serializes entity/relation edit requests correctly。
- graph store updates selected node after entity edit。
- graph store updates selected edge after relation edit。
- merge response opens merge refresh decision state。
- Graph workbench page mounts React bundle from Blazor host。

Manual verification:

- load graph from current server。
- select node and edit description。
- rename entity and refresh graph。
- select edge and edit keywords/description。
- merge duplicate entity and choose merged entity as new start point。
- delete relation with confirmation。

## Acceptance Criteria

- Current Graph page is no longer limited to the old Blazor Sigma wrapper for the primary experience。
- React graph workbench mounts inside `LightRAGNet.Web` and can be migrated later as an independent React module。
- Workbench supports graph search/load, node selection, edge selection, layout/zoom controls, and property panel。
- Entity edit and relation edit work from UI and persist through backend APIs。
- Entity create, relation create, entity merge, entity delete, and relation delete have usable UI actions or explicit first-phase buttons/dialogs。
- Backend graph curation operations keep graph store, vectors, KV metadata, tracking KV, and query revision aligned。
- Existing document/query tests remain isolated from real Qdrant/Neo4j。
- Focused backend/API/frontend tests pass, and full solution build remains green。

## Implementation Planning Notes

Recommended slices:

1. Backend graph curation contracts and test doubles.
2. Entity/relation get/create/edit APIs.
3. Entity merge and delete APIs.
4. React/Vite graph workbench scaffold inside Web.
5. Port Python graph data loading, graphology conversion, selection, layout, zoom, labels, and search.
6. Port property panel editing and merge dialog.
7. Wire destructive actions with confirmation.
8. Build/dev documentation, verification, and asset closeout.

The main design rule is simple: backend correctness first, React workbench as the real user-facing validation surface, Blazor only as a temporary shell.
