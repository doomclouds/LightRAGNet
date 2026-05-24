# React Standalone Documents Migration

- Date: `2026-05-24`
- Topic slug: `react-standalone-documents-migration`
- Status: `Archived`
- Scope: `Feature`
- Tags: `react`, `vite`, `documents`, `frontend-migration`, `blazor-coexistence`, `testing`

## Summary

本需求把文档上传、文档列表和文档展示从 Blazor 迁移到一个独立的 `src/LightRAGNet.React` Vite/React 前端，同时保留 `LightRAGNet.Server` 作为后端 API/SignalR 服务，并保持 Blazor 项目在本阶段不被删除、不被改动。

## Delivered Scope

- 新建独立 `src/LightRAGNet.React` 工程，包含 Vite、React、TypeScript、Vitest、SignalR client、共享样式接收层和 `src/` / `tests/` 分离的测试结构。
- 迁移 `/documents/upload`，支持 `.md`、`.markdown`、`.pdf`、`.docx` 批量上传、10 个文件上限、10 MB 单文件上限、重复文件名过滤、上传状态和成功反馈。
- 迁移 `/documents`，支持分页、状态筛选、加载/空/错误状态、View、Download、Add to RAG、Retry、Cancel、Delete、失败错误摘要、Added Time、进度条和文档预览。
- 接入 React 侧 SignalR lifecycle refresh，处理 `TaskStatusUpdated`、`DataCleared`、筛选边界刷新、missing row 刷新、删除任务完成刷新和防抖 reload。
- 后端仅增加 React Vite dev server CORS origin：`http://localhost:5173` 和 `http://127.0.0.1:5173`。
- 将 `scripts/dev-start.ps1` / `scripts/dev-stop.ps1` 对齐到当前双进程开发模式：一键启动 `LightRAGNet.Server` 和独立 `LightRAGNet.React`，并新增 Git Bash 可用的 `.sh` 包装脚本。

## Out of Scope

- 不删除、不重构、不改动 `src/LightRAGNet.Web`、Blazor routes、MudBlazor 依赖或 Blazor 测试。
- 不迁移 Graph Workbench、System Status、Cache Management 或 RAG Chat。
- 不把 React build 静态托管进 `LightRAGNet.Server`，也不引入主题切换 UI。
- PDF/DOCX 深度预览仍不在本切片内，当前只使用现有文档 API 可提供的内容。

## Verification Snapshot

- React targeted verification covered upload validation, document API client, list rendering, actions, preview, safe download, lifecycle refresh, parity checklist, and App-level SignalR wiring.
- React final verification: `npm run test --prefix src/LightRAGNet.React` passed `11` files / `74` tests; `npm run typecheck --prefix src/LightRAGNet.React` passed.
- React build verification: `npm run build --prefix src/LightRAGNet.React` passed after filtering the known `@microsoft/signalr/dist/esm/Utils.js` Rolldown `INVALID_ANNOTATION` warning in Vite config.
- Server targeted verification: `dotnet test tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter ReactDevCorsSourceTests` passed `1/1`.
- Full solution verification: `dotnet test LightRAGNet.slnx --no-restore -v minimal` passed with `427` core tests, `203` server tests, and `38` web tests after stabilizing the cache trend time fixture uncovered during closeout.
- Dev startup verification: `scripts/dev-start.ps1 -SkipNpmInstall -SkipClientBuild` started Server and React; `http://127.0.0.1:5173/documents` returned `200`; `http://localhost:5261/api/MarkdownDocuments` returned `200`; Git Bash wrappers `scripts/dev-stop.sh` and `scripts/dev-start.sh` were also exercised successfully.
- Diff hygiene checks confirmed no changes under `src/LightRAGNet.Web`, `tests/LightRAGNet.Web.Tests`, or `tests/LightRAGNet.Tests/Web`, and no React test files under `src/LightRAGNet.React/src`.

## Source Documents

- Spec: [React Standalone Documents Migration Design](../../specs/2026-05-24-react-standalone-documents-migration-design.md)
- Visual: None found for this topic.
- Plan: [React Standalone Documents Migration Implementation Plan](../../plans/2026-05-24-react-standalone-documents-migration-implementation-plan.md)

## Related Problems

- [Cache Trend Hour Boundary Test Flake](../../problems/2026-05/2026-05-24-cache-trend-hour-boundary-test-flake-problem.md)

## Notes

- Review gates caught and fixed production SignalR wiring, row-level action race hardening, safe download parity, and missing status-detail UI before archive.
- `src/LightRAGNet.Web` intentionally remains available for coexistence until the later Blazor removal phase.
