# Graph Workbench Sigma Settings Instability Problem

- Date: `2026-05-22`
- Topic slug: `graph-workbench-sigma-settings-instability`
- Status: `Captured`
- Scope: `UI`
- Tags: `knowledge-graph`, `sigma`, `react`, `webgl`, `blank-canvas`

## Symptom

Knowledge Graph tab 打开后页面不是接口报错，而是图谱区域一片空白。浏览器控制台出现 Sigma/WebGL 相关异常，例如 `Cannot read properties of undefined (reading 'bindFramebuffer')`。用户看到的是“图谱界面一片空白”，但 API 仍可能正常返回节点和边。

## Trigger / Context

- React graph workbench 使用 `@react-sigma/core` 的 `SigmaContainer`。
- `settings` prop 中包含每次 render 都会新建的对象或 program class，例如在 JSX 内直接调用 `createEdgeCurveProgram()`。
- reducer 或 effect 里重复设置 `nodeProgramClasses` / `edgeProgramClasses`，让 Sigma 认为 renderer 配置持续变化。
- 页面加载或图谱刷新时，旧 Sigma 实例清理与新实例加载图可能交叉，最终 WebGL context 被清理。

## Root Cause

`@react-sigma/core` 会比较 `SigmaContainer.settings`。当 settings 深层对象变化时，它会重建 Sigma 实例。renderer program class、program class map 和 `createEdgeCurveProgram()` 返回值如果在 React render 内创建，就会让 settings 看起来一直变化。

在本问题中，Sigma 实例被反复销毁/重建，旧实例清理了 WebGL layer，新实例或当前 render 随后访问 `webGLContexts.nodes`，触发 `bindFramebuffer` undefined，导致 canvas 白屏。

## Fix

- 把 `createEdgeCurveProgram()` 结果提升为模块级稳定常量。
- 把 `nodeProgramClasses` 和 `edgeProgramClasses` 提升为模块级稳定 class map。
- 使用 `NodeBorderProgram` 作为稳定节点 renderer。
- `GraphReducers` 只设置 reducer、标签显示和交互开关，不再重复注册 renderer program。
- 添加 source test，防止 `curvedNoArrow: createEdgeCurveProgram()` 再次出现在 JSX settings 中。
- 用 Playwright fresh open 验证 `/graph-view` 能显示节点/边且控制台无 Sigma/WebGL error。

## Why This Fix

这个修法直接消除 settings 引用不稳定的根因，而不是用延迟、重试、吞异常或刷新页面掩盖白屏。Sigma renderer program 是实例生命周期边界，不应该被普通 React render 反复构造；把它们提升为稳定常量也更贴近 Sigma 官方用法。

## Recognition Clues

- 图谱 API 能返回 nodes/edges，但画布空白。
- Console 出现 `bindFramebuffer`、`webGLContexts.nodes`、Sigma/WebGL frame buffer 相关错误。
- 最近改过 `GraphCanvas.tsx` 的 `SigmaContainer settings`、`nodeProgramClasses`、`edgeProgramClasses` 或 renderer program。
- 在 JSX settings 中能搜到 `createEdgeCurveProgram()` 或每次 render 新建的 class map。
- 重启服务或刷新页面偶尔改变表现，但不能稳定修复。

## Applicability / Non-Applicability

### Applies When

- React + `@react-sigma/core` 图谱页出现白屏或 WebGL context undefined。
- 图谱数据正常，但 Sigma canvas 不渲染。
- 近期改动涉及 Sigma renderer settings、program classes、reducers 或 layout lifecycle。

### Does Not Apply When

- `/api/graph/query` 本身返回空节点或后端 validation error。
- 页面静态资源 404 或 Blazor host 没有挂载 React island。
- 浏览器完全不支持 WebGL；这种情况应做能力检测或降级提示。
- 只是 ForceAtlas2 布局参数导致节点聚在视口外；那应检查布局和相机边界。

## Related Artifacts

- Spec: [graph workbench python parity design](../../specs/2026-05-22-graph-workbench-python-parity-design.md)
- Plan: [graph workbench python parity implementation plan](../../plans/2026-05-22-graph-workbench-python-parity-implementation-plan.md)
- Archive: [graph workbench python parity archives](../../archives/2026-05/2026-05-22-graph-workbench-python-parity-archives.md)
- Related Problems:
  - None.
- Code or Test:
  - [GraphCanvas.tsx](../../../../src/LightRAGNet.Web/ClientApp/src/components/graph/GraphCanvas.tsx)
  - [GraphWorkbenchHostSourceTests.cs](../../../../tests/LightRAGNet.Web.Tests/GraphWorkbenchHostSourceTests.cs)
  - [graph-workbench.js](../../../../src/LightRAGNet.Web/wwwroot/graph-workbench/assets/graph-workbench.js)
