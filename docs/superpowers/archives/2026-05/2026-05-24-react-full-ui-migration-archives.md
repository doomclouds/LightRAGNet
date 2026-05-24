# React Full UI Migration

- Date: `2026-05-24`
- Topic slug: `react-full-ui-migration`
- Status: `Archived`
- Scope: `UI`
- Tags: `react`, `frontend-migration`, `dark-ops`, `rag-chat`, `documents`, `graph`

## Summary

本阶段把 Blazor 项目下已经完成的 React islands 迁入 `src/LightRAGNet.React`，并把独立 React/Vite 前端升级为完整深色 UI shell。RAG Chat、Knowledge Graph、System Status、Cache Management 以直接迁移为主，Documents 和 Upload 按已确认的 dark-ops 方向重设计，最终由 `LightRAGNet.Server` 继续承担 API、SignalR 和安全 preview 后端。

## Delivered Scope

- 独立 React shell 承载 `/`, `/rag-chat`, `/documents`, `/documents/upload`, `/document-preview`, `/document-preview/:id`, `/graph-view`, `/system-status`, `/cache-management`，包含顶部栏、左侧导航、Clear All Data 和底部 SignalR 状态。
- RAG Chat 保留完整 query settings、流式/非流式链路、query details、引用 preview 入口，并修复 pending details dialog 关闭后的 loading 归还。
- Documents/Upload 迁入深色工作台样式，Documents 表格、状态切换、下载/删除、同页 preview drawer、scrim、阴影和 full preview 链路可用。
- Knowledge Graph、System Status、Cache Management 从 Blazor-hosted React 迁入独立 React，保留图谱控件和运营页信息架构。
- Dev startup 和 README route 清单更新为 standalone React routes，并防止 `dev-start` 误复用不属于当前 worktree 的旧 React dev server；Development CORS 支持本机 React 自定义端口。

## Out of Scope

- 未删除 `src/LightRAGNet.Web` 或 Blazor/MudBlazor 主项目。
- 未重写 Knowledge Graph 图谱按钮、浮层、设置面板或交互语义。
- 未引入主题切换 UI，也未改变后端核心 RAG API 语义。
- 未把截图纳入 git；视觉 QA 截图保存在被 `.gitignore` 忽略的 `output/playwright/`。

## Verification Snapshot

- React full suite: `npm run test --prefix src\LightRAGNet.React` -> `30` files / `216` tests passed.
- React typecheck/build: `npm run typecheck --prefix src\LightRAGNet.React` passed; `npm run build --prefix src\LightRAGNet.React` passed.
- Targeted server checks: `dotnet test tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter ReactDevCorsSourceTests --no-restore --verbosity minimal -p:IsTestProject=true` -> `2` tests passed.
- Full solution: `dotnet test LightRAGNet.slnx --no-restore --verbosity minimal` -> `LightRAGNet.Tests` `429` passed, `LightRAGNet.Web.Tests` `36` passed, `LightRAGNet.Server.Tests` `222` passed.
- Visual QA: standalone React ran from the current worktree on `http://127.0.0.1:5174`; screenshots covered shell, RAG Chat, Documents, Upload, Document Preview, Graph, System Status, Cache Management, and Documents preview drawer. Graph canvas was nonblank, drawer scrim/shadow/full preview were present, and no clipped button text was detected.
- Final code review subagent approved after CORS custom-port and query details abort fixes.

## Source Documents

- Spec: [React full UI migration design](../../specs/2026-05-24-react-full-ui-migration-design.md)
- Visual: [React full UI migration concepts](../../visuals/2026-05-24-react-full-ui-migration-concepts.html)
- Plan: [React full UI migration implementation plan](../../plans/2026-05-24-react-full-ui-migration-implementation-plan.md)

## Related Problems

- Inbox: [React shell foundation review signals](../../inbox/2026-05/2026-05-24-react-shell-foundation-review-signals-inbox.md)

## Notes

- The existing inbox note remains open because several guardrails are reusable beyond this requirement and may later be promoted into formal problem assets.
