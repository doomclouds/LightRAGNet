# React UI Standardization and RAG Chat Workbench Archive

- Date: `2026-05-24`
- Topic slug: `react-ui-standardization-rag-chat-workbench`
- Status: `Archived`
- Scope: `UI`
- Tags: `react-island`, `rag-chat`, `design-system`, `dark-ops`, `references`, `diagnostics`

## Summary

本次交付把 LightRAGNet 的 React 页面收敛到以 Cache Management 为基准的 `dark-ops` 视觉标准，并将 `/` RAG Chat 从 Blazor/MudBlazor 迁移为 React workbench。RAG Chat 保留轻量双栏对话模型、消息级引用、诊断详情和检索数据入口，同时把 reference 展示从 LLM 文本约定转移到后端结构化 metadata 与安全预览路由。

## Delivered Scope

- 增加共享 React `dark-ops` theme token 与基础 UI surface，Cache Management、System Status、Knowledge Graph 和 RAG Chat 均按该风格对齐。
- RAG Chat React workbench 接入 Vite 多入口和 Blazor `/` host，提供左侧对话、右侧 query settings、流式回复、debug output、message details dialog、retrieval data 加载和请求快照。
- 后端为 RAG references 增加安全 preview metadata，并提供 document preview routes；前端只在 `previewUrl` 存在时渲染新标签页预览链接。
- 移除 RAG prompt 中要求 LLM 生成最终 references markdown section 的职责，引用由结构化 metadata 和 UI 承担。
- 补齐 React SSE/API 合同测试、Blazor host source tests、dark canvas regression test，以及 graph workbench Sigma 白底回退保护。

## Out of Scope

- 未实现主题切换 UI；`dark-ops` 是本阶段唯一实际 skin。
- 未迁移整个 Blazor shell，仍采用 Blazor host 挂载 React islands。
- 未实现 query run 持久化、多轮对比评测或重型诊断仪表盘。
- 未为 Blazor JSInterop import/dispose race 建立 bUnit 级真实生命周期测试；当前以 source guard 和代码门控覆盖明显回退。
- 未验证本机真实 PDF/DOCX 样本 preview 内容链路；本阶段验证了 preview route、artifact contract 和 React reference link 入口。

## Verification Snapshot

- `2026-05-24` Backend targeted tests: `dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "RagPromptReferenceContractTests|QueryCache|DocumentProcessingServiceTests|DescriptionMergerTests" --no-restore --verbosity minimal` passed, `94/94`.
- `2026-05-24` Server targeted tests: `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "DocumentReferencePreviewResolverTests|DocumentPreviewControllerTests|RagQueryControllerTests|RagQueryRequestMapperTests|CacheManagement" --no-restore --verbosity minimal` passed, `55/55`.
- `2026-05-24` Web host tests: `dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --verbosity minimal` passed, `36/36`.
- `2026-05-24` Frontend tests: `npm test` from `src/LightRAGNet.Web/ClientApp` passed, `12 files / 80 tests`.
- `2026-05-24` Frontend build: `npm run build` emitted `rag-chat`, `graph-workbench`, `cache-management`, and `system-status` assets successfully.
- `2026-05-24` Full solution: first run failed because a Playwright QA Web server process locked `LightRAGNet.Web` DLLs; after stopping the Web process, `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal` passed with `429` core tests, `36` web tests, and `220` server tests.
- `2026-05-24` Diff/conflict checks: `git diff --check` exited `0` with only LF/CRLF warnings; `rg -n "^(<<<<<<<|=======|>>>>>>>)" src tests docs` returned no matches.
- `2026-05-24` Visual QA: Playwright screenshots verified `/` desktop and mobile RAG Chat, `/cache-management`, `/system-status`, and `/graph-view`; graph canvas initially showed a white Sigma background, then commit `809ba97` fixed it and `output/playwright/graph-view-dark-fixed.png` confirmed dark canvas readability.

## Source Documents

- Spec: [2026-05-24-react-ui-standardization-rag-chat-workbench-design.md](../../specs/2026-05-24-react-ui-standardization-rag-chat-workbench-design.md)
- Visual: None found for this topic.
- Plan: [2026-05-24-react-ui-standardization-rag-chat-workbench-implementation-plan.md](../../plans/2026-05-24-react-ui-standardization-rag-chat-workbench-implementation-plan.md)

## Related Problems

- None promoted yet. Same-thread reusable signals are tracked in this archive's Notes and should be promoted only if they recur.

## Notes

- The visual QA finding about React Sigma's default white canvas was fixed during closeout and covered by `reactPageThemeUsage.test.ts`.
- The Blazor React island host now guards dynamic JS import against dispose races for RAG Chat; existing islands may still benefit from a shared host pattern in a later refactor.
