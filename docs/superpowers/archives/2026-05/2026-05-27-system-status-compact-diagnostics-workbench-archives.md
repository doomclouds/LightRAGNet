# System Status Compact Diagnostics Workbench

- Date: `2026-05-27`
- Topic slug: `system-status-compact-diagnostics-workbench`
- Status: `Archived`
- Scope: `UI`
- Tags: `react`, `system-status`, `diagnostics`, `anthropic-light`, `visual-qa`

## Summary

本轮将 React `/system-status` 从旧的同权重卡片堆叠改造成紧凑诊断工作台：顶部保留整体健康与摘要，主扫读路径改为 evidence table，右侧承载 fix-first 和 feature impact，raw JSON 降级为二级诊断辅助区，同时保持现有 `/api/system/health` 合同、copy、refresh、loading 和 error 行为。

## Delivered Scope

- 新增页面局部 `SystemStatusSummaryTiles`、`SystemStatusEvidenceTable`、`SystemStatusRemediationPanel`、`SystemStatusFeatureImpactPanel` 和 `SystemStatusRawJsonPanel`，并用 `PageHeader`、`Button`、`Panel`、`StatusPill`、`DataTableSurface` 复用共享设计系统原语。
- 保留 server-provided `status`、`summary`、`checks`、`fixFirst`、`featureImpacts`，不新增假指标或后端字段，并补上 stale request guard 防止旧 health 响应覆盖新请求。
- 重写 `system-status.css` 为浅色 compact diagnostics layout，清理旧根级字体栈和页面局部 button/panel/status 债务，只保留 raw JSON 的受控 monospace 样式。
- 更新设计系统页面覆盖与 React 页面审计，将 System Status 标记为已迁移的 compact diagnostics workbench，并保留 Cache Management 作为后续诊断/list 工作台迁移候选。

## Out of Scope

- 不修改 `/api/system/health` 后端合同、健康检查语义、SignalR 连接或真实存储探测行为。
- 不重做应用 shell、sidebar、topbar、路由或其他 React 页面。
- 不引入图表库、大圆环 health gauge、导出报表能力或虚构运维指标。

## Verification Snapshot

- `npm test --prefix src/LightRAGNet.React -- --run tests/unit/features/system-status/systemStatusPresentation.test.ts tests/integration/features/system-status/SystemStatusWorkbench.test.tsx` 通过：2 个 test files，18 个 tests。
- `npm test --prefix src/LightRAGNet.React -- --run` 通过：35 个 test files，269 个 tests；仅保留 npm 对 `--run` 的已知 CLI warning。
- `npm run build --prefix src/LightRAGNet.React` 通过；仅保留既有 Vite chunk size warning。
- `git diff --check` 无输出。
- Playwright/Vite visual QA 使用 mock `/api/system/health` 数据检查 `1440x900`、`768x900`、`375x812`：无全局横向滚动，table/raw JSON 仅内部滚动，Lucide 图标和 compact workbench 结构可见。

## Source Documents

- Spec: [System Status Compact Diagnostics Workbench Design](../../specs/2026-05-27-system-status-compact-diagnostics-workbench-design.md)
- Visual: [System Cache Table Pages Reference](../../visuals/anthropic-light-workbench/04-system-cache-table-pages.png)
- Visual: [System Status React Prototype](../../visuals/anthropic-light-workbench/05-system-status-compact-diagnostics-workbench-react-prototype.html)
- Plan: [System Status Compact Diagnostics Workbench Implementation Plan](../../plans/2026-05-27-system-status-compact-diagnostics-workbench-implementation-plan.md)

## Related Problems

- None. 本轮发现的测试等待竞态、请求乱序、evidence 格式化分叉和 theme guardrail 语义色缺口均已直接收敛为实现修复与回归测试，未形成需要单独归档的稳定问题资产。

## Notes

- 375px 视觉检查中现有 app shell 会在 System Status 内容上方占位；本轮只重构右侧页面内容，未改变 shell 移动布局边界。
