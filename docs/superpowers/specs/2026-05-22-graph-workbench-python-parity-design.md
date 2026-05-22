# Graph Workbench Python Parity Design

## 背景

当前 React 图谱工作台已经能够加载、布局和编辑知识图谱，但视觉和交互仍偏“管理表单”：顶部固定查询栏、右侧固定属性栏、画布被网格背景和布局容器挤压。用户反馈整体效果不像 Python LightRAG 的知识图谱面板。

参考来源：

- Python LightRAG WebUI 源码仓库：`https://github.com/HKUDS/LightRAG`
- 本轮主要参考文件：`lightrag_webui/src/features/GraphViewer.tsx`、`components/graph/GraphControl.tsx`、`LayoutsControl.tsx`、`ZoomControl.tsx`、`GraphSearch.tsx`、`GraphLabels.tsx`、`PropertiesView.tsx`、`stores/graph.ts`

## 目标

把当前 Blazor 挂载的 React 图谱岛重构为接近 Python LightRAG 的图谱工作台：

- 整屏画布为第一视觉，不再像表单页面。
- 顶左浮层提供标签、刷新和搜索。
- 左下浮层提供布局、缩放、旋转、重置、图例等图谱操作。
- 右上浮层展示当前选中/悬停元素属性，未选中时不占据画布空间。
- Sigma 渲染行为对齐 Python 版：曲线边、节点边框、hover/focus/selection reducer、邻居高亮。
- 保留 LightRAGNet 已有实体/关系编辑、删除和合并确认能力。

## 范围

本阶段做 Python parity shell，不做完整 Python WebUI 全量移植：

- 做：视觉壳、布局控件、搜索、悬停/选中、属性浮层、图例、编辑入口。
- 暂缓：Python 版设置抽屉的全部选项、expand/prune 后端联动、pipeline busy 监听、搜索历史持久化。

## 架构

Blazor 仍只负责挂载 `graph-workbench-root`。React 岛内部保持后续迁移 React 的结构：API、store、graph components、workbench feature 分离。

`GraphWorkbench` 负责数据加载与页面壳。`GraphCanvas` 只负责 Sigma 容器和图加载。新增控件组件承担布局、搜索、图例、视角控制。`graphStore` 扩展 focused node/edge 与 sigma instance 状态，用于 reducer 和浮层显示。

## 验收

- 打开 `/graph-view` 后第一屏应像知识图谱工作台，而不是表单页。
- 默认布局不再是圆环；节点应以力导向形态聚类展开。
- 点击/悬停节点时邻居高亮，非关联元素弱化。
- 点击边时边高亮并展示关系属性。
- 属性面板浮在图上，不长期占据右侧列。
- 前端 build/typecheck/test 通过，已构建产物同步到 `wwwroot/graph-workbench`。
