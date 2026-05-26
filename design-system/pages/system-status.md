# System Status 页面设计覆盖

- 页面路由：`/system-status`
- 页面类型：`Diagnostic Workbench`
- 源文件：
  - `src/LightRAGNet.React/src/features/system-status/SystemStatusWorkbench.tsx`
  - `src/LightRAGNet.React/src/features/system-status/system-status.css`

## 页面角色

System Status 是诊断页面。它应帮助用户理解健康状态、证据、可能影响和优先修复步骤，而不是把 raw JSON 推到主界面。

## 必须使用的共享组件

- `PageHeader`
- `Panel`
- `Button`
- `StatusPill`
- `ErrorState`

## 允许偏离

- 证据表格可以使用局部紧凑表格样式。
- 严重程度卡片可以保持诊断专用布局。
- spinner 动画可以保持局部实现，但触及时必须考虑 reduced-motion。

## 规则

- 迁移时用共享组件替换页面局部 header 和按钮样式。
- 健康状态使用语义状态 pill。
- 证据必须可检查、可复制。
- 严重程度不能只靠颜色表达。
- raw JSON copy 放在页面头部操作区或二级诊断面板里。

## 视觉 QA

- Healthy、degraded、unhealthy、not-measured 状态。
- 加载和 API 错误状态。
- 展开长值证据。
- 移动端操作按钮堆叠。
