# React Anthropic Light Shell Redesign

- Date: `2026-05-25`
- Topic slug: `react-anthropic-light-shell-redesign`
- Status: `Archived`
- Scope: `UI`
- Tags: `react`, `ui-shell`, `anthropic-light`, `documents`, `upload`, `preview`, `signalr`

## Summary

本阶段把独立 React 前端从上一轮深色 shell 调整为已确认的 Anthropic-like 浅色工作台风格，并先完成全局 UI 外框、图标标准、底部 SignalR 状态和三个文档相关页面的真实功能重构。非文档页面保持既有内容实现，只接入新的 shell 和导航框架，避免把本轮变成全站内容重写。

## Delivered Scope

- 独立 React shell 对齐已确认原型：暖白背景、赤陶色 active 状态、分组侧边栏、顶部全局栏、真实 LightRAGNet brand mark、底部 SignalR 状态和版本号。
- 建立浅色主题 token、共享基础 UI 组件和 `lucide-react` 图标使用标准，Documents、Upload、Document Preview 首批接入。
- `/documents` 保留真实列表、状态切换、刷新、删除、下载、安全预览和 preview drawer，同时重构为带摘要、工具栏、表格层级、遮罩和阴影的工作台界面。
- `/documents/upload` 保留真实文件选择、批量上传、清空、状态反馈和成功/失败结果展示，并按新框架重构 dropzone 与选中文件面板。
- `/document-preview` 与 `/document-preview/:id` 保留安全预览加载、错误态、空态和文档内容展示，改为浅色阅读工作区。
- Knowledge Graph、RAG Chat、System Status、Cache Management 未做内容重设计，但通过 smoke 测试确认可继续挂在新 shell 下。
- Follow-up on `2026-05-26`: `/documents` 按参考截图补齐细节，对摘要卡、状态 tabs、搜索/筛选工具栏、彩色文件类型图标、RAG Status 语义 badge、独立 Progress 列、行级 kebab 菜单和预览 drawer 做了二次对齐，并抽出 `MetricCard`、`Toolbar`、`ProgressBar`、`DataTableSurface`、`ActionMenu`、`FileTypeIcon` 等共享小组件。

## Out of Scope

- 未重设计 RAG Chat、Knowledge Graph、System Status 或 Cache Management 的页面内容。
- 未修改后端 API、SignalR 合同、文档生命周期或文件预览后端实现。
- 未增加主题切换、假指标、假筛选器、装饰性 chip 或不存在的命令。
- 未删除 Blazor 项目，也未改变现有 React 路由覆盖范围。

## Verification Snapshot

- Baseline React tests before implementation: `npm test --prefix src/LightRAGNet.React -- --run` -> `30` files / `216` tests passed.
- Final React tests: `npm test --prefix src/LightRAGNet.React -- --run` -> `30` files / `223` tests passed.
- TypeScript: `npm run typecheck --prefix src/LightRAGNet.React` passed.
- Production build: `npm run build --prefix src/LightRAGNet.React` passed.
- Visual QA screenshots were refreshed from the Vite dev server for Documents, Upload, Document Preview, and Knowledge Graph under `docs/superpowers/visuals/anthropic-light-workbench/implementation-checks/`.
- Final code review subagent approved after restoring app landmarks, avoiding nested `main` landmarks, and replacing viewport-scaled heading sizes with fixed page heading sizes.
- Follow-up verification on `2026-05-26`: `npm test --prefix src/LightRAGNet.React` -> `30` files / `229` tests passed.
- Follow-up verification on `2026-05-26`: `npm run build --prefix src/LightRAGNet.React` passed.
- Follow-up browser QA on `2026-05-26`: Vite dev server with a local mock API rendered the Documents list and opened the light preview drawer with metadata, content preview, footer actions, and non-transparent backdrop.

## Source Documents

- Spec: [React Anthropic Light Shell Redesign Design](../../specs/2026-05-25-react-anthropic-light-shell-redesign-design.md)
- Visual: [Approved Documents Drawer Prototype](../../visuals/anthropic-light-workbench/app-frame-documents-drawer-prototype.html)
- Visual: [Documents implementation screenshot](../../visuals/anthropic-light-workbench/implementation-checks/documents.png)
- Visual: [Upload implementation screenshot](../../visuals/anthropic-light-workbench/implementation-checks/upload.png)
- Visual: [Document Preview implementation screenshot](../../visuals/anthropic-light-workbench/implementation-checks/document-preview.png)
- Visual: [Knowledge Graph shell screenshot](../../visuals/anthropic-light-workbench/implementation-checks/graph-view.png)
- Plan: [React Anthropic Light Shell Redesign Implementation Plan](../../plans/2026-05-25-react-anthropic-light-shell-redesign-implementation-plan.md)

## Related Problems

- Inbox: [React shell foundation review signals](../../inbox/2026-05/2026-05-24-react-shell-foundation-review-signals-inbox.md)

## Notes

- Screenshots were captured without the backend API running, so network error and SignalR disconnected states are expected in visual evidence; the purpose was verifying shell/page layout and visible route mounting.
- The existing React shell foundation inbox remains relevant because it already tracks isolated worktree edit discipline and shell-level review guardrails that also applied to this redesign.
- Follow-up on `2026-05-26`: user validation found the RAG Chat query details modal still carried dark-ops assumptions after the light shell migration. The fix changed the portal dialog from the removed `lrn-dialog` primitive to `lrn-modal`, replaced dark hard-coded chat/detail colors with light theme tokens, and added regression coverage for the modal class and RAG Chat CSS dark-literal guard.
- Follow-up on `2026-05-26`: user validation also found the Documents list still missed many reference-image micro-components and looked visually rough in file icons, status badges, typography, and spacing. The second pass kept server-side API boundaries intact: Search/File Type are current-page local filters, Tag remains a visual-ready control until the API exposes tag data, and RAG Status remains the only server-backed list filter.
