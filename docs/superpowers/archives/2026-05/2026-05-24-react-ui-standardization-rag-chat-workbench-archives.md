# React UI Standardization and RAG Chat Workbench Archive

- Date: `2026-05-24`
- Topic slug: `react-ui-standardization-rag-chat-workbench`
- Status: `Archived`
- Scope: `UI`
- Tags: `react-island`, `rag-chat`, `design-system`, `dark-ops`, `anthropic-light`, `references`, `diagnostics`

## Summary

本次交付把 LightRAGNet 的 React 页面收敛到以 Cache Management 为基准的 `dark-ops` 视觉标准，并将 `/` RAG Chat 从 Blazor/MudBlazor 迁移为 React workbench。RAG Chat 保留轻量双栏对话模型、消息级引用、诊断详情和检索数据入口，同时把 reference 展示从 LLM 文本约定转移到后端结构化 metadata 与安全预览路由。

`2026-05-28` 追加交付：在用户验收 `02-rag-chat-workbench.png` 视觉原型后，独立 React `/` RAG Chat 页面迁移为浅色 `anthropic-light` 工作台，包含顶部对话工具、卡片式消息、底部 composer、Lucide 图标化引用/详情入口和右侧 `Query Settings` 抽屉式设置面板，同时保留原有 streaming、references、rerank、debug output、details dialog 和 query request 合同。

## Delivered Scope

- 增加共享 React `dark-ops` theme token 与基础 UI surface，Cache Management、System Status、Knowledge Graph 和 RAG Chat 均按该风格对齐。
- RAG Chat React workbench 接入 Vite 多入口和 Blazor `/` host，提供左侧对话、右侧 query settings、流式回复、debug output、message details dialog、retrieval data 加载和请求快照。
- Follow-up on `2026-05-28`: `/` RAG Chat 按已批准 React/Lucide 原型重构为浅色工作台：自定义 heading/status chips、对话工具、空态提示、消息头像、icon-only composer action、三段式可视 mode presets、设置 reset，并继续通过隐藏完整 `Mode` select 保留 `Mix/Naive/Bypass/Local/Global/Hybrid` 合同。
- Follow-up correction on `2026-05-28`: user validation clarified that chat height must remain fixed so long conversations scroll inside the message area and the composer remains reachable. The fix restored a fixed chat frame, marked `.rag-chat__messages` as the internal scroll surface, moved the empty-state hint directly above the composer, and replaced the prototype-only three-option mode segment with the full `Mode` select.
- Follow-up correction on `2026-05-28`: user validation then found a remaining blank band below the composer in the real app. Browser metrics traced this to the RAG route `main` being allowed to grow beyond the shell viewport (`851px` on a 720px viewport) because the route did not bind its height to `calc(100vh - var(--topbar-height))`. The fix added route-specific `.app-main--rag-chat` height/overflow rules, kept `.app-main` `min-height: 0`, and let the workbench consume the main content area instead of using viewport magic numbers.
- Follow-up correction on `2026-05-28`: user validation also caught that the empty chat area had no visible outer frame. The fix restored `.rag-chat__chat` as a bordered, rounded, light panel with hidden overflow while keeping `.rag-chat__messages` as the internal scroll surface.
- Follow-up correction on `2026-05-28`: user validation found the right `Query Settings` panel too rigid compared with the approved prototype. The fix reworked it into a lighter prototype-aligned settings flow: header icon, per-control label and note copy, lightweight toggle rows, compact inline number controls, and a reset action at the end of the settings stack while retaining the real full `Mode` select contract.
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
- `2026-05-24` User validation follow-up fixed two React migration regressions: Knowledge Graph dark canvas was blank because Sigma canvas layers were painted opaque, and RAG Chat query details were too small/incomplete. Verification reran `npm test`, `npm run build`, `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal`, and Playwright screenshots `output/playwright/graph-dark-after-fix.png` / `output/playwright/rag-details-dialog-zindex-fixed.png`.
- `2026-05-28` React verification: `npm test` from `src/LightRAGNet.React` passed, `35` files / `271` tests.
- `2026-05-28` React build: `npm run build` from `src/LightRAGNet.React` passed; Vite kept the existing large chunk warning.
- `2026-05-28` Browser QA: Vite dev server at `http://127.0.0.1:5174/` rendered desktop and mobile RAG Chat without horizontal overflow; desktop screenshot confirmed composer visibility after height correction and mobile check reported `scrollWidth == clientWidth == 390`. SignalR/API console errors were expected because the backend server was not running.
- `2026-05-28` User validation correction: 1280x720 browser metrics showed fixed chat/layout height `496px`, message surface height `409px`, composer pinned at `y=566.64`, empty hint gap to composer reduced from `325px` to `16px`, and `Mode` rendered as `lrn-select` with no `.rag-chat__mode-segment`.
- `2026-05-28` Final blank-band correction: 1280x720 browser metrics after the route-height fix showed `main.bottom == viewport.bottom == 720`, layout/composer bottom `704`, bottom gap `16px`, and `documentExtra == 0`; `npm test` passed `35` files / `273` tests and `npm run build` passed with the existing large chunk warning.
- `2026-05-28` Chat-frame visual correction: browser metrics confirmed `.rag-chat__chat` border `1px solid rgb(229, 222, 210)`, border radius `10px`, light panel background, hidden outer overflow, bottom gap `16px`, and `documentExtra == 0`; final `npm test` passed `35` files / `273` tests and `npm run build` passed with the existing large chunk warning.
- `2026-05-28` Query Settings visual correction: React tests verified seven setting rows, the prototype-style helper notes for mode/response/toggles/numbers/keywords/debug output, and the reset action inside the settings stack. Playwright screenshot `output/playwright/rag-chat-settings-panel-light-row.png` confirmed the lighter settings-row layout in the real browser. Final `npm test` passed `35` files / `273` tests and `npm run build` passed with the existing large chunk warning.

## Source Documents

- Spec: [2026-05-24-react-ui-standardization-rag-chat-workbench-design.md](../../specs/2026-05-24-react-ui-standardization-rag-chat-workbench-design.md)
- Visual: [RAG Chat approved React prototype](../../visuals/anthropic-light-workbench/07-rag-chat-workbench-react-prototype.html)
- Visual: [RAG Chat prototype desktop screenshot](../../visuals/anthropic-light-workbench/rag-chat-prototype-desktop.png)
- Visual: [RAG Chat prototype mobile screenshot](../../visuals/anthropic-light-workbench/rag-chat-prototype-mobile.png)
- Plan: [2026-05-24-react-ui-standardization-rag-chat-workbench-implementation-plan.md](../../plans/2026-05-24-react-ui-standardization-rag-chat-workbench-implementation-plan.md)

## Related Problems

- [Graph Workbench Sigma Canvas Background Problem](../../problems/2026-05/2026-05-24-graph-workbench-sigma-canvas-background-problem.md)

## Notes

- The visual QA finding about React Sigma's default white canvas was fixed during closeout and covered by `reactPageThemeUsage.test.ts`.
- Follow-up dark-mode debugging showed that making every Sigma canvas dark is wrong for multi-layer renderers: the container should own the dark background and canvas layers must remain transparent.
- React RAG Chat query details now render through a body-level portal, sit above Blazor fixed chrome, auto-load full retrieval data, and restore tabbed views for entities, relationships, chunks, references, metadata, diagnostics, request, and raw JSON.
- The Blazor React island host now guards dynamic JS import against dispose races for RAG Chat; existing islands may still benefit from a shared host pattern in a later refactor.
- Final review corrected RAG Chat reference rendering so unresolved/external references remain visible as plain source labels, while only references with `previewUrl` render new-tab links.
- The `2026-05-28` light workbench pass deliberately changed only presentation and local interaction chrome; production query semantics still flow through `buildRagQueryRequest`, `queryRagStream`, and the existing details-dialog retrieval endpoint.
- Preserve the fixed chat-frame rule during future visual passes: page scroll should not be the primary way to reach the composer after many messages; only the message surface should scroll.
- Avoid viewport magic numbers for RAG Chat height. The route should bind to the app shell's main surface (`app-main--rag-chat`) so outer page scroll does not reappear as bottom whitespace.
