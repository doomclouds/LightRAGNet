# Graph Workbench Camera Focus Coordinate Race Problem

- Date: `2026-05-22`
- Topic slug: `graph-workbench-camera-focus-coordinate-race`
- Status: `Captured`
- Scope: `UI`
- Tags: `knowledge-graph`, `sigma`, `react`, `camera`, `search`

## Symptom

Knowledge Graph tab 初始能正常显示节点和边，但在搜索框输入关键字并点击下拉结果节点后，选中属性面板正常出现，图谱画布却像“消失在视野范围外”。点击 Reset view 可以恢复图谱，说明 API、Graphology 数据和 Sigma renderer 并没有整体崩掉。

## Trigger / Context

- React graph workbench 使用 `@react-sigma/core`、Sigma v3 和 `useSetSettings` reducer 高亮选中节点。
- 搜索下拉调用 `useGraphStore.selectNode(node.id, true)`，触发 `moveToSelectedNode` 相机移动。
- 同一轮 React effect 中，`GraphReducers` 更新 Sigma settings/reducer，`GraphCameraFocus` 随后读取节点坐标并移动相机。
- 用户可见表现是“点击搜索结果后图谱飞走”，但 selection panel 和关系列表仍会显示目标节点。

## Root Cause

Sigma 的 `getNodeDisplayData` 依赖内部 `nodeDataCache`。当选中节点触发 reducer/settings 更新时，Sigma 会进入 refresh/render 流程；在同一轮 effect 中马上读取 `getNodeDisplayData(selectedNodeId)`，可能读到尚未完成归一化的原始 graph coordinates。

本次复现里，目标节点 `B` 的归一化 display 坐标约为 `x=0.686,y=0.858`，但点击搜索结果后相机状态被设置为原始坐标 `x=548,y=838`。Camera 以 framed graph coordinates 工作，拿到原始坐标后视口矩形会跳到极小且远离真实图谱的位置，于是画布看起来空白。

## Fix

- `GraphCameraFocus` 不再在同一轮 effect 内立即移动相机。
- 使用 `window.requestAnimationFrame` 延后一帧，等 Sigma refresh/render 完成并重新归一化节点缓存后，再读取 `sigma.getNodeDisplayData(selectedNodeId)`。
- 相机只使用归一化后的 `nodeDisplayData.x/y`，不再读取 Graphology raw `x/y`，也不强行写固定 zoom ratio。
- 搜索结果点击后关闭下拉框，避免 selection panel 打开后仍有结果浮层遮挡图谱。
- 添加 source tests 锁住相机聚焦必须延后读取 Sigma display coordinates，并锁住搜索结果选择后关闭。

## Why This Fix

这个修法对准的是 Sigma refresh 时序，而不是用 Reset view、硬编码缩放、延迟重载整张图或吞掉 selection 来掩盖问题。延后一帧可以让 Sigma 完成自己的坐标归一化，再用相机 API 进入正确的 framed graph 坐标系；同时保留搜索点击后聚焦目标节点的交互预期。

## Recognition Clues

- 初始图谱可见，搜索或关系按钮选择节点后画布变空。
- 右侧属性面板显示了目标节点，说明 store selection 成功。
- 点击 Reset view 后图谱恢复，说明 renderer 和数据没有完全白屏。
- 浏览器控制台没有 `bindFramebuffer` / WebGL context 错误。
- 调试相机状态时能看到 camera `x/y` 变成几百、几千这类 Graphology 原始坐标，而不是 `0..1` 附近的 Sigma framed coordinates。

## Applicability / Non-Applicability

### Applies When

- React + Sigma 图谱在节点选择、搜索聚焦、关系跳转后“飞出视野”。
- 选中状态和属性面板正常，但画布空白或只剩背景。
- 最近改动涉及 `getNodeDisplayData`、`getCamera().animate`、`useSetSettings` reducers、hover/selection 高亮或 layout refresh。

### Does Not Apply When

- `/api/graph/query` 返回空节点、后端报错或静态资源没有加载。
- Sigma renderer settings 引用不稳定导致 WebGL context 被销毁；这种情况应看 settings instability problem。
- 只是下拉框、属性面板或其它浮层遮住了一部分画布；这种情况应先检查 DOM 和 z-index。
- 图谱本身因布局参数全部聚集到画布边缘，但 Reset view 后仍不可见。

## Related Artifacts

- Spec: [graph workbench python parity design](../../specs/2026-05-22-graph-workbench-python-parity-design.md)
- Plan: [graph workbench python parity implementation plan](../../plans/2026-05-22-graph-workbench-python-parity-implementation-plan.md)
- Archive: [graph workbench python parity archives](../../archives/2026-05/2026-05-22-graph-workbench-python-parity-archives.md)
- Related Problems:
  - [graph workbench sigma settings instability problem](./2026-05-22-graph-workbench-sigma-settings-instability-problem.md)
- Code or Test:
  - [GraphCanvas.tsx](../../../../src/LightRAGNet.Web/ClientApp/src/components/graph/GraphCanvas.tsx)
  - [GraphSearchBox.tsx](../../../../src/LightRAGNet.Web/ClientApp/src/components/graph/GraphSearchBox.tsx)
  - [GraphWorkbenchHostSourceTests.cs](../../../../tests/LightRAGNet.Web.Tests/GraphWorkbenchHostSourceTests.cs)
