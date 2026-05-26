# Cache Management 页面设计覆盖

- 页面路由：`/cache-management`
- 页面类型：`List Workbench` + `Diagnostic Workbench`
- 源文件：
  - `src/LightRAGNet.React/src/features/cache-management/CacheManagementWorkbench.tsx`
  - `src/LightRAGNet.React/src/features/cache-management/cache-management.css`

## 页面角色

Cache Management 是混合型运维页面：KPI 卡片、筛选控件、family 表格、趋势图、insights、clear plans 和 cache samples 同时存在。

## 必须使用的共享组件

- `PageHeader`
- `Panel`
- `Toolbar`
- `Button`
- `StatusPill`
- `DataTableSurface`
- `EmptyState`
- `ErrorState`
- `ConfirmDialog` 或等价共享确认模式

## 允许偏离

- 缓存趋势条可以保留局部可视化样式。
- 缓存专用 rate bar 和 cache-family dot 可以继续作为局部数据可视化原语。
- 如果工作流刻意保持轻量，clear plan 确认可以保持行内形式。

## 规则

- 当行为能匹配共享原语时，用共享组件替换 `cache-button`、`cache-panel`、`cache-pill`。
- workspace/window 控件保持密集，并靠近页面头部操作区。
- 破坏性 cache clear 操作必须使用 danger 样式，并在需要时显式确认。
- 表格应使用共享表格表面和 token 化表格颜色。

## 视觉 QA

- 初始加载状态。
- 没有测量数据的空窗口。
- 含 summary cards、family table、trend、insights、clear plans 和 samples 的完整 overview。
- 高风险 clear plan 确认。
- 移动端 toolbar 和 clear actions 换行。
