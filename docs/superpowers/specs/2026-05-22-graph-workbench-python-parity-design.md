# Graph Workbench Python Parity Design

## 背景

LightRAGNet 的 Knowledge Graph tab 已经具备图谱查询、属性编辑、删除和实体合并能力，但前端形态一开始更像“管理表单 + 图谱预览”：查询区、属性区和画布互相挤占空间，节点布局接近圆环，节点大小没有表达关系密度，部分操作还会让用户感知为图谱消失。

用户明确希望当前 Blazor 宿主只是过渡形态，后续可以迁移到 React。因此本需求把图谱体验收敛为一个 React island 内部自洽的知识图谱工作台：Blazor 只负责挂载和传递 API base，React 负责图谱数据加载、Sigma 渲染、控制浮层、交互状态和编辑入口。

## Reference Source Declaration

本需求明确参考 Python LightRAG 源码仓库：

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

## 目标

- 第一屏呈现知识图谱工作台，而不是表单页面。
- 默认布局使用力导向形态，不再把节点摆成单一圆环。
- 节点大小表达关系密度：关系越多，节点越大。
- 边宽表达关系强度：优先读取 `properties.weight` 并按设置范围映射。
- hover 只做高亮，不移动相机；搜索和 relationships 跳转才移动到目标节点。
- 图谱控件以浮层形式出现：查询/搜索、布局/视角、Settings、Fullscreen、图例和属性面板都不长期挤占画布。
- 保留 LightRAGNet 已有编辑能力：属性编辑、实体/关系删除、实体合并确认。
- 最大节点上限由服务端配置，避免前端硬编码造成“输入 2000 但后端仍按 1000 拦截”的错觉。
- 图谱页加载链路要能定位和规避白屏、接口 500、静态产物未刷新这类回归。

## 用户体验设计

### 画布与布局

图谱页主体是全画布 Sigma surface。查询控件在左上角，布局/视角 dock 在左下角，属性面板在右上角，图例在右下角。所有控件都以浮层方式覆盖画布，避免把图谱压成一个小预览框。

默认加载后使用稳定随机初始坐标，再执行 ForceAtlas2，使图谱聚类展开。保留 Force Atlas、Force Directed、Noverlap、Random、Circular 等布局入口，便于调试不同图谱规模和关系形态。

### 节点、边与高亮

节点 renderer 使用 Sigma node border program。节点 domain type 存入 `domainType`，不污染 Sigma 的 renderer `type`。节点大小按 degree 做平方根缩放，范围 `4..20`。边默认使用曲线无箭头 program，边宽按关系 weight 缩放，范围由 Settings 控制。

hover 节点时只高亮当前节点和邻居，弱化非关联元素。点击节点或搜索结果时选中节点，属性面板展示实体属性和 relationships 列表。点击边时高亮关系并展示关系属性。点击 relationships 中的邻居时，选中该节点并移动视角。

### 控件与配置

查询浮层提供 label、depth、max nodes 和 refresh。Label 使用 `/api/graph/labels` 加载 datalist，并保留 `*` 全局查询。Max nodes 上限通过 `/api/graph/config` 获取服务端 `GraphView:MaxNodesLimit`，默认 `2000`，前端输入控件和设置面板都使用同一个上限。

Settings 浮层包括节点标签、边标签、edge events、隐藏非关联边、边大小 min/max、layout iterations 和 max nodes。Fullscreen 是真实全屏控制，不做假按钮。

## 架构设计

- `GraphView.razor`：Blazor thin host，只挂载 `graph-workbench-root`、传入 `data-api-base`、加载 React island 产物。
- `GraphWorkbench.tsx`：负责 API base、图谱查询、配置加载、全局 loading/error 状态和页面壳。
- `GraphCanvas.tsx`：负责 Sigma 容器、图加载、layout、事件注册、reducer 和相机边界。
- `graphologyAdapter.ts`：把后端 `GraphViewDto` 转为 Graphology graph，处理节点 degree size、边 weight size、renderer type、domain type 和空 edge id fallback。
- `graphSettingsStore.ts`：集中管理图谱显示设置和服务端 max nodes limit。
- `graphStore.ts`：集中管理 selected/focused node/edge、sigma instance、move intent 和属性面板状态。
- `GraphQueryControls` / `GraphSearchBox` / `GraphViewportControls` / `GraphSettingsPanel` / `GraphLegend` / `PropertiesPanel`：保持控件组件化，方便后续迁移到纯 React。
- `GraphController` / `GraphViewController`：查询参数边界由服务端兜底，`/api/graph/config` 暴露前端需要的图谱配置。

## 稳定性边界

- Sigma renderer class、program class map、`createEdgeCurveProgram()` 结果必须是稳定引用，不能在 JSX render 内重复创建，否则 `@react-sigma/core` 会认为 settings 变化并重建 Sigma 实例，可能导致 WebGL context 被旧实例清理后白屏。
- Graph reducer 只更新 reducer、标签显示和交互设置，不重复注册 node/edge renderer program。
- hover/focus 与 camera move intent 分离，避免鼠标移动把相机频繁拉走。
- `/api/graph/labels` 的 Neo4j query 必须先 `UNWIND labels(n)`，再 `WITH label`，再过滤 workspace label；不能在 `UNWIND` 后直接拼接 `WHERE`。
- Server/API 测试默认不访问真实开发 Qdrant/Neo4j；真实图谱烟测只在本地运行服务后手动/API 验证。

## 范围

### 本需求包含

- Python LightRAG 风格整屏 graph shell。
- Sigma 曲线边、节点边框、ForceAtlas2、邻居高亮、边/节点选中。
- 节点 degree scaling 和边 weight scaling。
- 搜索、布局、缩放、旋转、重置、图例、Settings、Fullscreen。
- Label datalist 和刷新当前图谱。
- 属性面板关系列表和关系邻居跳转。
- 服务端可配置 max nodes limit 与前端同步。
- Neo4j labels 查询修复。
- 白屏回归修复和源码级 guardrail。
- React island 构建产物同步到 `wwwroot/graph-workbench`。

### 暂缓范围

- Python LightRAG 完整 i18n、搜索历史持久化和 pipeline busy 监听。
- expand/prune 后端联动。
- 全站从 Blazor 迁移到 React。
- 默认启用真实 Qdrant/Neo4j 集成测试。
- 大规模 2000+ 节点下的性能专项优化；当前只提供配置上限和基础验证。

## 验收标准

- 打开 `/graph-view` 后能看到力导向知识图谱，不是一片空白，也不是圆环散点。
- 节点关系越多越大，边 weight 越大越粗。
- hover/drag 不会让图谱消失或相机跳飞。
- Search、relationships 跳转会明确选中并移动到目标节点。
- Settings、Label、Fullscreen、图例和属性面板可用。
- 输入 max nodes 上限时，前端和服务端使用同一个配置；超过上限返回清晰 validation error。
- `/api/graph/labels` 不因 Neo4j Cypher 语法返回 500。
- `npm test`、`npm run typecheck`、`npm run build`、focused Web host tests、GraphController tests 和相关 source tests 通过。
- Playwright fresh open 能看到图谱画布，控制台没有 Sigma/WebGL error。
