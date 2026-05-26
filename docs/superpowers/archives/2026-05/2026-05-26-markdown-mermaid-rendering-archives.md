# Markdown Mermaid Rendering

- Date: `2026-05-26`
- Topic slug: `markdown-mermaid-rendering`
- Status: `Archived`
- Scope: `UI`
- Tags: `react`, `markdown`, `mermaid`, `document-preview`, `rag-chat`

## Summary

本次交付把 React 前端的 Markdown 渲染入口统一为共享组件，并为 `mermaid` fenced code block 增加真实图表渲染能力。目标是让文档预览、预览抽屉和 RAG Chat 回复都能展示 Mermaid 图，而不是把图表源码当普通代码块露出来。

## Delivered Scope

- 新增共享 `MarkdownRenderer`，集中承载 `react-markdown`、`remark-gfm` 和 Mermaid block 处理。
- 支持标准 ```` ```mermaid ```` fenced code block，使用 Mermaid 手动渲染 SVG，并配置 `startOnLoad: false` 与 `securityLevel: 'strict'`。
- 渲染失败时显示明确错误和原始 Mermaid 源码，避免预览页或聊天消息白屏。
- 将 RAG Chat assistant 消息、独立 Document Preview 页面和 Documents preview drawer 全部切换到共享渲染器。

## Out of Scope

- 未加入 Markdown 编辑器、实时图表编辑或图表语法提示。
- 未按 Mermaid 图表类型做更细的 bundle 拆分优化。
- 未改变后端文档预览 API 或 Markdown 存储格式。

## Verification Snapshot

- `npm test --prefix src/LightRAGNet.React` -> `32` files / `236` tests passed.
- `npm run build --prefix src/LightRAGNet.React` passed; Vite reported existing-style large chunk warnings, including async Mermaid parser chunks.
- Browser QA with local mock API rendered `/document-preview/42` Mermaid Markdown as an SVG diagram.
- Regression QA covered a real `sequenceDiagram` where message text contains `;`; the renderer escapes sequence diagram semicolons before calling Mermaid while preserving the original source in fallback output.

## Source Documents

- Spec: None; direct user-approved implementation request on `2026-05-26`.
- Visual: Browser QA screenshot generated at `artifacts/mermaid-preview-qa.png`.
- Plan: None; implementation followed the approved in-chat approach of shared renderer plus Mermaid block fallback.

## Related Problems

- None.

## Notes

- Browser QA exposed a PowerShell-specific mock-data trap: literal Markdown backticks in `node -e` were interpreted by PowerShell. The final QA mock generated the fence with `String.fromCharCode(96).repeat(3)` to verify the real Markdown path.
- Mermaid sequence diagrams can fail on bare semicolons inside message text. The renderer normalizes only `sequenceDiagram` input by replacing `;` with `#59;` before `mermaid.render`, which keeps the displayed label equivalent and avoids changing normal Markdown/code fallback text.
