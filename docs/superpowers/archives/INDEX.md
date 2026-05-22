# Superpowers Archives Index

## 2026-05

- [2026-05-22-graph-workbench-python-parity-archives.md](./2026-05/2026-05-22-graph-workbench-python-parity-archives.md): 将 Knowledge Graph tab 重构为 Python LightRAG 风格的整屏 Sigma 图谱工作台，补齐浮层控件、布局菜单、搜索聚焦、图例和属性面板。
- [2026-05-21-document-intake-pipeline-parity-archives.md](./2026-05/2026-05-21-document-intake-pipeline-parity-archives.md): 将 Markdown/text 文档接入升级为带 `track_id`、后台队列、状态追踪、retry/cancel 和 Web 基础操作的 intake pipeline。
- [2026-05-21-graph-curation-react-workbench-archives.md](./2026-05/2026-05-21-graph-curation-react-workbench-archives.md): 引入 React/Vite 图谱工作台和图谱治理 API，对齐 Python LightRAG 图谱编辑、属性面板和实体合并语义，并保留参考来源声明。
- [2026-05-21-kg-context-builder-parity-archives.md](./2026-05/2026-05-21-kg-context-builder-parity-archives.md): 将 KG query context 收口到结构化 builder，统一 JSON section、`reference_id` 引用锚点和按最终输出形态计算的 token budget。
- [2026-05-21-query-data-debug-panel-archives.md](./2026-05/2026-05-21-query-data-debug-panel-archives.md): 为每条 RAG Chat assistant 回复增加按需检索数据检查入口，用消息级请求快照查看 raw retrieval data 和 metadata。
- [2026-05-21-rerank-chunking-parity-archives.md](./2026-05/2026-05-21-rerank-chunking-parity-archives.md): 将 Naive 和 KG Mix 的长 chunk rerank 对齐为 token 子片段打分、max-score 原始 chunk 聚合和 document-level topN。
- [2026-05-20-chat-query-ui-adaptation-archives.md](./2026-05/2026-05-20-chat-query-ui-adaptation-archives.md): 将 Chat 升级为可选 query mode、Streaming/Cacheable、References 和 diagnostics 的查询工作台，并补齐共享请求/SSE metadata 合同。
- [2026-05-20-indexing-llm-cache-parity-archives.md](./2026-05/2026-05-20-indexing-llm-cache-parity-archives.md): 将索引阶段 LLM cache 对齐为 `default:extract` / `default:summary` 合同，写入 chunk cache 引用并区分 extract 删除与 summary 保留语义。
- [2026-05-20-retrieval-context-vector-chunk-parity-archives.md](./2026-05/2026-05-20-retrieval-context-vector-chunk-parity-archives.md): 让 KG entity/relation related chunks 的默认 `VECTOR` 配置真正按 chunk vector cosine similarity 选择，并在向量不可用时稳定降级到 `WEIGHT`。
- [2026-05-19-concurrency-race-governance-archives.md](./2026-05/2026-05-19-concurrency-race-governance-archives.md): 建立文件原子写入、事件串行分发、操作取消、按 key 锁和状态泵启动保护等并发边界，收敛近期竞态问题。
- [2026-05-19-query-llm-cache-parity-archives.md](./2026-05/2026-05-19-query-llm-cache-parity-archives.md): 为查询阶段接入 KG keyword cache、KG/Naive/Bypass non-streaming answer cache，并用 workspace revision 防止文档变更后命中旧答案。
- [2026-05-19-query-mode-context-parity-archives.md](./2026-05/2026-05-19-query-mode-context-parity-archives.md): 接入 `QueryMode.Bypass` 直连 LLM 和 `QueryMode.Naive` vector-only 查询路由，并保留 KG keyword policy 边界。
- [2026-05-18-document-deletion-parity-archives.md](./2026-05/2026-05-18-document-deletion-parity-archives.md): 补齐 indexed document deletion 的核心存储清理、API/任务状态、Blazor 删除体验、clear-all 安全边界和可选真实存储验证。
- [2026-05-18-document-lifecycle-alignment-archives.md](./2026-05/2026-05-18-document-lifecycle-alignment-archives.md): 引入可测试的文档生命周期、`doc_status` workspace 持久化、失败重试和删除计划合同，为后续 Python LightRAG 对齐奠定数据一致性基础。
- [2026-05-17-testability-foundation-archives.md](./2026-05/2026-05-17-testability-foundation-archives.md): 建立 `src/` 与 `tests/` 分层，并为文档分块、检索上下文、图谱合并和任务队列补上首层可维护测试覆盖。
