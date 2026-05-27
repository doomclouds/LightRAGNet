# React Design System Guardrails

- Date: `2026-05-27`
- Topic slug: `react-design-system-guardrails`
- Status: `Archived`
- Scope: `UI`
- Tags: `react`, `design-system`, `guardrails`, `components`, `visual-qa`

## Summary

本轮把 React `anthropic-light` 设计系统从文档标准推进到工程约束：补齐页面反馈、确认弹窗、小型模式切换、表单字段和诊断表格五个共享原语，并用 Vitest source/CSS 护栏约束共享 class、页面根级字体栈、硬编码颜色和页面局部通用 UI 债务，防止后续页面继续各写一套按钮、面板、pill、表格和 dialog。

## Delivered Scope

- 新增 `Banner`、`ConfirmDialog`、`SegmentedControl`、`Field`、`DiagnosticTable` 共享组件，并在 `app.css` 中补齐对应 `.lrn-*` 样式。
- 增加设计系统护栏测试，覆盖共享 class 存在性、页面 CSS 字体栈、硬编码 hex、局部通用 UI class 债务和 feature CSS 自动发现。
- 更新 `design-system/MASTER.md`、`design-system/pages/README.md` 与 React 页面审计记录，明确组件清单、例外边界和后续页面迁移入口。
- 修正实现期审查发现的无障碍和护栏漏洞，包括确认弹窗 focus 管理、唯一 ARIA id、`Field` 描述关联保留，以及静态 CSS 文件清单绕过风险。

## Out of Scope

- 本轮不完整迁移 `System Status`、`Cache Management`、`RAG Chat`、`Knowledge Graph` 或 `Document Preview` 页面。
- 不修改 API、SignalR、图谱算法、文档上传、缓存清理或其他业务语义。
- 不引入新组件框架、主题切换，或强制消除所有历史页面局部 CSS。

## Verification Snapshot

- `npm test --prefix src/LightRAGNet.React -- --run` 通过：34 个 test files，253 个 tests；仅保留 npm 对 `--run` 的已知 CLI warning。
- `npm run build --prefix src/LightRAGNet.React` 通过：`tsc --noEmit --pretty false && vite build`；仅保留 Vite chunk size warning。
- `git diff --check HEAD~12..HEAD` 无输出。
- 最终代码审查复核通过，最后一项静态 feature CSS 清单绕过风险已由递归发现测试覆盖。

## Source Documents

- Spec: [React Design System Guardrails Design](../../specs/2026-05-27-react-design-system-guardrails-design.md)
- Visual: None found for this topic.
- Plan: [React Design System Guardrails Implementation Plan](../../plans/2026-05-27-react-design-system-guardrails-implementation-plan.md)

## Related Problems

- None. 本轮审查发现的无障碍、CSS token 和护栏覆盖问题已直接收敛到组件实现与回归测试中，未形成独立稳定问题资产。

## Notes

- 这次交付是后续 P1 页面迁移前置基础：先把通用 UI 语法和测试护栏立住，再逐页替换现有局部债务。
