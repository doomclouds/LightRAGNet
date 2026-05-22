# Graph Workbench Functional Parity Design

## 背景

上一轮已经把 Knowledge Graph tab 改成接近 Python LightRAG 的整屏图谱工作台，但用户继续验收后指出三个问题：

- 节点关系越多应越大，目前没有按 Python 版 degree size 语义显示。
- 拖动、移动或其他操作后图谱看起来会消失。
- 整体功能仍漏掉 Python 版很多关键图谱控件。

参考来源：

- Python LightRAG source repository: `https://github.com/HKUDS/LightRAG`
- Local reference copy: `LightRAG/`
- 重点参考：`lightrag_webui/src/hooks/useLightragGraph.tsx`、`features/GraphViewer.tsx`、`components/graph/GraphControl.tsx`、`Settings.tsx`、`GraphLabels.tsx`、`PropertiesView.tsx`、`FocusOnNode.tsx`

## 已确认差异

Python 版节点大小按 degree 计算：先统计每个节点相邻边数量，再用 `minNodeSize = 4`、`maxNodeSize = 20` 和平方根缩放。当前 LightRAGNet 前端主要使用后端 `node.size` 并设置较高下限，导致“关系越多越大”的知识图谱视觉语义丢失。

Python 版普通 hover 只高亮节点和邻居；只有搜索/明确选择触发 `moveToSelectedNode` 时才移动相机。当前实现把 `focusedNode ?? selectedNode` 都用于相机动画，hover 或移动鼠标可能把视角频繁拉走，用户会感知为图谱消失。

## 目标

- 节点大小、边宽和高亮行为对齐 Python 版核心语义。
- 操作图谱时不会因为 hover/drag 导致相机跳走。
- 补齐第一批高价值 Python 功能：Settings、Label refresh/select、Fullscreen、关系列表。

## 本阶段范围

- 节点 degree scaling：`4..20`，平方根缩放。
- 边 weight scaling：从关系 `properties.weight` 读取并按配置范围缩放。
- 相机移动边界：hover 不移动，搜索/明确请求才移动。
- 设置面板：节点标签、边标签、edge events、隐藏非关联边、边大小 min/max、layout iterations、max nodes。
- Label 控件：加载 `/api/graph/labels`，支持 `*` 和刷新当前图谱。
- Fullscreen 控制。
- 属性浮层补 relationships，点击关系邻居可选中并移动过去。

## 暂缓范围

- Python 版完整 i18n、搜索历史、pipeline busy 刷新。
- expand/prune 后端语义。当前阶段只保留为后续可接入方向，不做假功能。

## 验收

- degree 高的节点明显更大。
- hover/drag 后图谱不会消失或被相机拉飞。
- Settings 改动即时影响画布。
- Label 下拉和刷新可用。
- 属性面板可看到节点关系列表。
- 前端测试、typecheck、build、focused Web tests 和浏览器截图验证通过。
