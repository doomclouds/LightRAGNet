# Python LightRAG 与 LightRAG.NET 功能对照

- 日期：2026-05-24
- 目的：为后续 LightRAG.NET 继续对齐 Python LightRAG、制定开发计划和拆分阶段目标提供功能地图。
- 范围：本仓库当前代码、`LightRAG/` 本地 Python 参考树、`docs/superpowers/specs`、`docs/superpowers/plans`、`docs/superpowers/archives`。
- 重要边界：本文描述的是“当前可见代码与已归档交付”。有 spec/plan 但无 archive、且当前 `src/` 未落地的内容，按“规划中/未完成”处理。

## 结论摘要

Python LightRAG 是完整产品级框架：同时覆盖 Core SDK、API Server、WebUI、Ollama 兼容接口、多后端存储、多模型 provider、文档 pipeline、图谱治理、查询/引用/缓存、部署向导、离线部署、评估与可观测性。

LightRAG.NET 当前已经把“核心 RAG 主链”推进到可用状态：文档分块、实体/关系抽取、Qdrant/Neo4j/JSON 存储、Local/Global/Hybrid/Mix/Naive/Bypass 查询、引用、raw retrieval data、LLM cache、rerank chunking、文档生命周期、删除、任务队列、SignalR 状态、PDF/DOCX 本地转换接入、Python 风格图谱工作台和 Web 可见的 System Status 都有实现。

最大差距不在单个算法点，而在产品面和生态面：Python 版的更广文档接入、目录扫描、多 provider、多存储、部署向导、认证安全、Ollama 兼容 API、评估/可观测性、export/maintenance tools 仍明显领先。PDF/DOCX 和 System Status 已经补上首个可用切片，接下来最值得做的方向是先补“缓存/维护工具 + 图谱导出/search + 安全部署边界”，然后再扩 provider/storage 生态。

## 状态标记

| 标记 | 含义 |
| --- | --- |
| 已对齐 | .NET 当前代码已经具备同类能力，并有测试或 archive 佐证 |
| 部分对齐 | .NET 有核心能力，但范围、入口、生态或边界明显小于 Python |
| 未对齐 | .NET 当前代码未发现等价能力 |
| 规划中 | 已有 spec/plan，但当前代码和 archive 尚不足以当作完成 |

## Python LightRAG 功能全景

### 1. Core SDK 与初始化

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| `LightRAG` Core 类 | `LightRAG/lightrag/lightrag.py` 提供主入口，包含 storage 初始化、插入、查询、删除、图谱治理、导出等方法。 | 已对齐核心入口：`src/LightRAGNet/LightRAG.cs` |
| 显式 storage 生命周期 | `initialize_storages()` / `finalize_storages()` 管理存储初始化与释放。 | 部分对齐：.NET 通过 DI 和 hosted app 生命周期管理，未暴露同名 Core 生命周期 API |
| 数据迁移 | Python 有 `check_and_migrate_data()`、entity/relation 迁移、chunk tracking 迁移。 | 部分对齐：有 EF migration、KV legacy key 迁移、Qdrant/Neo4j naming 对齐；缺少 Python 那种统一迁移入口 |
| workspace 隔离 | `workspace` 影响 KV/vector/graph/doc_status 命名、目录或标签。 | 已对齐核心语义：Qdrant collection/point、Neo4j label、KV doc_status 都考虑 workspace |
| 工作目录 | `working_dir` 保存缓存、KV、图文件、状态等。 | 已对齐：`LightRAGOptions.WorkingDir` + JSON KV + SQLite/Uploads |
| tokenizer 可替换 | Python 默认 tiktoken，也可传自定义 tokenizer。 | 部分对齐：.NET 使用 DeepSeek tokenizer / `ITokenizer`，可抽象替换但 provider 面较窄 |
| 大量初始化参数 | chunk、overlap、topK、token budget、source id limit、summary、rerank、cache、并发、entity types 等。 | 部分对齐：主要 RAG 参数已在 `LightRAGOptions`，但 LLM role kwargs、node2vec、embedding cache 等未对齐 |
| role-specific LLM | Python 可按 embedding、extract、summary、query 等场景配置模型函数和 kwargs。 | 未对齐：.NET 当前主要是单一 `ILLMService` |

### 2. 模型 Provider 与推理接口

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| OpenAI / OpenAI-compatible | `lightrag/llm/openai.py` 支持 OpenAI、兼容 API、Azure OpenAI。 | 部分对齐：DeepSeek OpenAI-compatible LLM |
| Ollama | LLM、embedding、Server Ollama 模拟接口。 | 未对齐：无 Ollama provider，也无 Ollama-compatible API |
| Gemini | LLM 与 embedding。 | 未对齐 |
| HuggingFace | 本地/远端 HF 模型与 embedding。 | 未对齐 |
| Anthropic Claude | Claude provider。 | 未对齐 |
| AWS Bedrock | Bedrock LLM/embedding。 | 未对齐 |
| Zhipu | 智谱 LLM/embedding。 | 未对齐 |
| VoyageAI | embedding provider。 | 未对齐 |
| Jina | embedding provider。 | 未对齐 |
| LlamaIndex | 可用 LlamaIndex LLM/embedding 接入。 | 未对齐 |
| LMDeploy / Lollms / NVIDIA OpenAI | 额外 provider。 | 未对齐 |
| Provider 参数封装 | `binding_options.py` 有 Ollama/Gemini/OpenAI 等绑定参数模型。 | 部分对齐：.NET 有 DeepSeek/Aliyun/Qdrant/Neo4j options |
| Token usage tracking | `TokenTracker` 可统计 LLM token 用量。 | 未对齐 |
| Langfuse tracing | OpenAI-compatible 调用可接 Langfuse 可观测性。 | 未对齐 |

### 3. Embedding 与 Rerank

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| Embedding function 抽象 | `EmbeddingFunc` 控制维度、max token、批量与并发。 | 部分对齐：`IEmbeddingService` + Aliyun embedding，配置面较窄 |
| embedding batch | 支持 embedding batch size 与 max async。 | 部分对齐：.NET provider 内部是否批量由当前 Aliyun 实现决定，整体并发配置不完整 |
| 非对称 embedding | 支持 query/document prefix 或 provider task 参数，并明确需要重建索引。 | 未对齐 |
| embedding cache | `embedding_cache_config` 可按相似度复用问题/答案或 embedding 相关缓存。 | 未对齐或未显式实现 |
| Rerank provider | Python `rerank.py` 支持多种 reranker driver。 | 部分对齐：.NET 有 `IRerankService` + Aliyun Rerank |
| rerank 默认启用 | Python 默认 query 可启用 rerank，mix 模式受益。 | 已对齐：`QueryParam.EnableRerank` 默认 true |
| 长 chunk rerank | Python 会切分长 chunk，子片段 max-score 聚合回原始 chunk。 | 已对齐：`RerankDocumentChunker` + `RerankCoordinator` |
| min rerank score | Python 有 `min_rerank_score` 过滤。 | 部分对齐：需继续核实 .NET 当前 provider/filter 是否完整暴露 |

### 4. 存储后端

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| KV Storage | Json、Redis、PostgreSQL、MongoDB、OpenSearch。 | 部分对齐：JSON KV |
| Vector Storage | NanoVectorDB、Milvus、PostgreSQL/pgvector、Faiss、Qdrant、MongoDB Atlas Vector、OpenSearch。 | 部分对齐：Qdrant |
| Graph Storage | NetworkX、Neo4j、PostgreSQL AGE、MongoDB、Memgraph、OpenSearch。 | 部分对齐：Neo4j |
| DocStatus Storage | Json、Redis、PostgreSQL、MongoDB、OpenSearch。 | 部分对齐：KV-backed doc_status |
| Storage env 校验 | Python 会验证 storage/provider 必需环境变量。 | 部分对齐：.NET provider 构造时校验少量 key，缺统一配置审计 |
| collection/table suffix | Python 根据 embedding model/dimension 生成隔离后缀。 | 部分对齐：Qdrant collection 包含 dimension，未完整包含 model name |
| batch graph ops | Python graph contract 有 batch get/upsert/degree/edges。 | 部分对齐：.NET Neo4j 有部分批量/查询能力，但接口面更小 |
| full drop | Python storage contract 有 `drop()` 清库能力。 | 部分对齐：.NET 有 clear-all 和外部清理器，但测试默认隔离真实存储 |
| shared update flags | Python in-memory/file storage 通过 shared flags 通知跨进程数据更新。 | 未对齐：.NET 当前是单进程服务/SignalR/JSON 文件持久化模型 |

### 5. 文档接入与索引 Pipeline

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| 单文本插入 | `insert()` / `ainsert()`。 | 已对齐：`InsertAsync()` |
| 批量插入 | `insert(["TEXT1", ...])`，支持 `max_parallel_insert`。 | 部分对齐：Server text intake 支持批量，Core `InsertAsync` 是单文档入口 |
| 自定义 doc id | Python insert 支持 `ids`。 | 已对齐：`InsertAsync(content, docId)` |
| file path / citation | Python insert 支持 `file_paths`，query 可返回 references。 | 已对齐：.NET chunk/entity/relation 保存 file_path 并返回 references |
| pipeline enqueue | `apipeline_enqueue_documents()` / `apipeline_process_enqueue_documents()` 支持后台增量处理。 | 部分对齐：.NET 有 RAG task queue、DocumentIntakeService、SignalR 状态；不完全同 Python global pipeline status |
| track_id | Python upload/text/scan 用 `track_id` 追踪一批文档。 | 已对齐：.NET intake track id |
| doc_status | Python 状态：pending、processing、preprocessed、processed、failed。 | 部分对齐：Core lifecycle 有 pending/processing/processed/failed/deleting/deleted；Server intake 有 queued/processing/completed/failed/cancelled |
| chunk snapshot | Python doc_status 保存 chunks_count/chunks_list 用于删除和恢复。 | 已对齐 |
| duplicate gate | Python 通过 doc id/status 避免重复处理。 | 已对齐：doc lifecycle + file hash/name 层面 |
| 错误文件入队 | Python 支持 `apipeline_enqueue_error_documents()` 记录抽取失败文件。 | 部分对齐：.NET queue/enqueue 失败会标记 Failed，但错误文件模型较窄 |
| 目录扫描 | API `/documents/scan` 扫描 input_dir。 | 未对齐 |
| 文件上传保存到 input_dir | API `/documents/upload` 保存文件并后台处理。 | 部分对齐：.NET `/api/MarkdownDocuments/upload` 支持批量 `.md/.markdown/.pdf/.docx` 保存；PDF/DOCX 需要用户 `Add to RAG` 后进入 conversion queue |
| 文本 API 插入 | `/documents/text`、`/documents/texts`。 | 已对齐：`/api/MarkdownDocuments/text` |
| 支持文件类型 | Python DocumentManager 支持 `.txt/.md/.mdx/.pdf/.docx/.pptx/.xlsx/.rtf/.odt/.tex/.epub/.html/.csv/.json/.xml/.yaml/.log/.conf/.ini/.properties/.sql/.bat/.sh/.c/.h/.cpp/.hpp/.py/.java/.js/.ts/.swift/.go/.rb/.php/.css/.scss/.less`。 | 部分对齐：当前 pipeline upload 支持 `.md/.markdown/.pdf/.docx`；legacy 单文件 upload 仍只支持 `.md/.markdown`；`.txt` 走 text API 而非文件 upload |
| PDF 抽取 | Python 支持 pypdf，配置 docling 时优先 docling，可处理密码配置。 | 部分对齐：.NET 通过本地 `ManagedCode.MarkItDown` 转换 PDF，保存 original artifact 和 `converted.md`，但未覆盖密码 PDF、OCR/docling 等高级路径 |
| DOCX/PPTX/XLSX 抽取 | Python 用 python-docx/python-pptx/openpyxl 或 docling fallback。 | 部分对齐：.NET 已支持 DOCX 转 Markdown；PPTX/XLSX 仍未对齐 |
| Markdown/text 直接处理 | Python 读取 UTF-8 文本类文件。 | 已对齐：.NET 支持 markdown/text intake 和编码检测 |
| 多模态 RAG-Anything | Python 文档声明可接 RAG-Anything，支持 PDF、Office、图像、表格、公式。 | 未对齐 |
| scan busy/cancel | Python pipeline 有 busy 状态、cancel pipeline。 | 部分对齐：.NET 支持按文档/track cancel，但未完全等价全局 pipeline |
| reprocess failed | Python `/documents/reprocess_failed`。 | 已对齐近似：.NET 支持失败文档 retry |
| status counts / pagination | Python doc status counts、分页、按 status/track 查询。 | 已对齐近似：.NET 文档列表分页、状态过滤、track status |

### 6. 文档分块、实体关系抽取与图谱构建

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| token chunking | `chunking_by_token_size()` 按 token size 和 overlap 切块。 | 已对齐：`DocumentProcessingService` |
| chunk metadata | tokens、content、full_doc_id、chunk_order_index、file_path、llm_cache_list。 | 已对齐：text_chunks + vector metadata |
| entity extraction prompt | Python prompt 支持 entity types、语言、gleaning。 | 部分对齐：.NET 有 extraction prompt/parser，entity type/options；多轮 gleaning 需继续补齐 |
| max entity/relation per chunk | Python 有默认限制与 prompt/解析边界。 | 部分对齐：`.NET LightRAGOptions` 有 `MaxEntitiesPerChunk`、`MaxRelationshipsPerChunk` |
| LLM extraction cache | Python `default:extract`。 | 已对齐：索引阶段 extraction cache |
| summary cache | Python `default:summary`。 | 已对齐：summary cache |
| description merge | Python 合并实体/关系描述，必要时 LLM summary。 | 已对齐：`DescriptionMerger` |
| source_id 聚合 | Python 用 `<SEP>` 聚合多来源 chunk ids。 | 已对齐 |
| file_path 聚合与 placeholder | Python 保留多 file_path，超限占位。 | 部分对齐：.NET 已聚合 file_path，具体 placeholder/limit 行为已实现但仍可继续跟 Python 精确化 |
| source id limit | Python 支持 FIFO/KEEP 等限制方法。 | 已对齐：`SourceIdsLimiter` |
| KG rebuild from chunks | Python 可在删除文档后从剩余 chunk 重建相关图谱。 | 部分对齐：.NET 删除可 prune/rebuild retained entity/relation；完整 rebuild pipeline 范围小于 Python |
| custom chunks | Python `insert_custom_chunks()`。 | 未对齐 |
| custom KG | Python `insert_custom_kg()`。 | 未对齐：.NET 有 graph curation API，但不是同等 bulk custom KG import |

### 7. 查询模式与检索上下文

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| Local | 低层关键词驱动，聚焦实体和局部关系。 | 已对齐 |
| Global | 高层关键词驱动，聚焦关系/全局模式。 | 已对齐 |
| Hybrid | local + global。 | 已对齐，当前与 Mix 行为保持一致 |
| Mix | KG + vector chunks 综合检索。 | 已对齐 |
| Naive | 只查 chunk vector，不查 KG。 | 已对齐 |
| Bypass | 不检索，直接 LLM。 | 已对齐 |
| empty query fail | Python 空查询返回 fail response。 | 已对齐近似 |
| keyword extraction | Python 从 query 生成 high/low keywords，短查询低层 fallback。 | 已对齐核心语义 |
| manual keywords | QueryParam 可传 hl/ll keywords。 | 已对齐 |
| conversation history | Python history 只给生成 LLM，不参与 retrieval。 | 已对齐 |
| user_prompt | Python user_prompt 不参与 retrieval，只影响生成。 | 已对齐或部分对齐：.NET QueryParam 有 UserPrompt |
| response_type | 影响回答形态。 | 已对齐 |
| only_need_context | 只返回检索上下文。 | 已对齐或部分对齐：Core QueryParam 有字段，需持续核验所有 mode |
| only_need_prompt | 只返回 prompt。 | 已对齐或部分对齐：Core QueryParam 有字段，需持续核验所有 mode |
| stream | Python 支持 stream/non-stream。 | 已对齐：Server SSE + Web streaming |
| include_references | Python query 可返回 references。 | 已对齐 |
| query_data | Python `/query/data` 与 `aquery_data()` 返回 entities、relationships、chunks、references、metadata。 | 已对齐：`/api/RagQuery/data` + Chat 检索数据面板 |
| raw metadata | query_mode、keywords、processing_info。 | 已对齐 |
| token budget | max_entity_tokens、max_relation_tokens、max_total_tokens。 | 已对齐：`TokenBudgetPlanner` / `KgQueryContextBuilder` |
| related chunk selection | Python entity/relation related chunks 支持 WEIGHT/VECTOR。 | 已对齐：VECTOR 默认，缺向量时降级 WEIGHT |
| reference list | Python raw_data 中 reference_id/file_path。 | 已对齐 |
| chunk content references | Python API 可选择 include_chunk_content。 | 部分对齐：.NET retrieval data 可看 chunks，但回答 references 展开主要是路径/id |
| prompt 模板完全一致 | Python 有完整 prompt.py 模板体系。 | 部分对齐：核心 prompt 参考 Python，但未声明逐字完全一致 |

### 8. LLM Cache 与维护工具

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| 全局 LLM cache 开关 | `enable_llm_cache`。 | 已对齐：`EnableLlmCache` |
| entity extract cache 开关 | `enable_llm_cache_for_entity_extract`。 | 已对齐 |
| query answer cache | KG/Naive/Bypass non-streaming 可缓存。 | 已对齐 |
| keyword cache | KG keyword cache。 | 已对齐 |
| workspace revision | 文档/图谱变化后避免命中旧查询答案。 | 已对齐 |
| chunk cache references | text chunk 写入 `llm_cache_list` 用于删除。 | 已对齐 |
| clear all cache | Python `aclear_cache()` 清 `llm_response_cache`。 | 部分对齐：.NET 有缓存服务，但缺公开完整 cache 管理 API/UI |
| selective query cache cleanup tool | Python `clean_llm_query_cache` 工具清 query/keywords cache。 | 未对齐 |
| migrate llm cache tool | Python `migrate_llm_cache`。 | 未对齐 |
| download/check init tools | Python tools 下有 download cache、check initialization、hash password 等。 | 未对齐 |

### 9. 删除、治理与数据一致性

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| delete by document id | `adelete_by_doc_id()` 删除 chunks、独占实体/关系、重建保留实体/关系、更新向量、清 status。 | 已对齐核心行为 |
| delete file | API 删除文档可选删除 input_dir 文件。 | 部分对齐：.NET 删除上传文件有安全边界 |
| delete LLM cache | 文档删除可选删除 extraction cache。 | 已对齐 |
| delete entity | `delete_by_entity()` 删除节点、关系、向量。 | 已对齐：Graph API/Core service |
| delete relation | `delete_by_relation()` 删除边、关系向量。 | 已对齐 |
| clear all documents | Python `/documents` DELETE 清文档与存储。 | 已对齐近似：.NET clear-all + external storage cleaner，但事务性有限 |
| deletion retry state | Python 有删除失败/重试状态。 | 已对齐：DeletionFailed retry |
| graph consistency | 删除/编辑后同步 graph/vector/KV tracking。 | 已对齐核心路径 |
| busy guard | Python pipeline busy 时限制 destructive operations。 | 部分对齐：.NET 对 active task 有互斥，非全局 pipeline busy |

### 10. 图谱查询与治理

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| graph labels | `get_graph_labels()` / `/graph/label/list`。 | 已对齐：`/api/graph/labels`，偏 popular labels |
| popular labels | `/graph/label/popular`。 | 部分对齐：当前 labels 返回 popular labels |
| search labels | `/graph/label/search`。 | 未对齐或未公开 |
| query graph | `/graphs` 支持 label、max_depth、max_nodes。 | 已对齐：`/api/graph/query` |
| max nodes truncated | Python KnowledgeGraph 有 truncation 语义。 | 部分对齐：.NET 有 maxNodes limit，truncated 语义需继续核验 |
| entity exists | `/graph/entity/exists`。 | 已对齐 |
| create entity | `/graph/entity/create`。 | 已对齐 |
| edit entity | 支持属性更新、rename、allow_merge。 | 已对齐 |
| create relation | `/graph/relation/create`。 | 已对齐 |
| edit relation | `/graph/relation/edit`。 | 已对齐 |
| merge entities | `/graph/entities/merge`，迁移关系、合并重复关系、防 self-loop。 | 已对齐 |
| graph export | `export_data()` 支持 csv/excel/md/txt，可包含 vector。 | 未对齐 |
| visualizer 工具 | Python 有 standalone graph visualizer。 | 部分对齐：.NET 有 React graph workbench，但无 standalone export visualizer |

### 11. API Server

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| FastAPI Server | `lightrag-server`，包含 docs、health、auth、query、documents、graph、ollama。 | 部分对齐：ASP.NET Core API + Scalar/OpenAPI，接口路径不同 |
| health | `/health` 返回配置、WebUI、pipeline、版本等。 | 部分对齐：.NET 有 `GET /api/system/health`，覆盖 Server API、SQLite、WorkingDir、Qdrant、Neo4j、LLM/Embedding/Rerank config、RAG task queue、Conversion queue；路径和 Python payload 不同，版本/WebUI/auth 等仍缺 |
| auth status/login | `/auth-status`、`/login`、JWT/API key。 | 未对齐 |
| API key/whitelist | Python 支持认证白名单等 server 配置。 | 未对齐 |
| OpenAPI/Swagger | 自带 `/docs`、`/openapi.json`。 | 已对齐：OpenAPI + Scalar development UI |
| API prefix/root path | Python 支持 `LIGHTRAG_API_PREFIX` 与 reverse proxy。 | 未对齐或未系统化 |
| Nginx/streaming guidance | Python docs 提供 upload/stream proxy 配置。 | 未对齐为文档/配置模板 |
| document routes | scan/upload/text/texts/status/delete/cache/reprocess/cancel/paginated/count。 | 部分对齐：text/upload/list/track/retry/cancel/delete/clear-all/count、PDF/DOCX conversion 已有；缺 scan、status_counts、cache 管理等 |
| query routes | `/query`、`/query/stream`、`/query/data`。 | 已对齐近似：`/api/RagQuery/query` SSE、`/api/RagQuery/data` |
| graph routes | label/query/entity/relation/merge。 | 部分到已对齐：主治理路径已对齐，label search/popular 分离不足 |
| Ollama-compatible API | `/api/generate`、`/api/chat`、`/api/tags`、`/api/ps` 等模拟 Ollama。 | 未对齐 |
| static WebUI mount | Python Server 托管 WebUI，若无 WebUI 可跳 docs。 | 不适用：.NET Server 与 React 前端分离运行 |
| CORS / static upload | 支持 WebUI/API 交互与上传。 | 已对齐 |

### 12. WebUI

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| 文档上传/扫描/状态 | Python WebUI 支持文档接入和 pipeline 状态。 | 部分对齐：React 文档列表、`.md/.markdown/.pdf/.docx` 上传、Add to RAG、conversion 状态、retry/cancel/delete、SignalR；目录扫描仍缺 |
| 查询聊天 | Python WebUI 支持 RAG 查询。 | 已对齐：RAG Chat 查询工作台 |
| query mode 控制 | mode、stream、response type、topK 等。 | 已对齐 |
| references 展示 | Python 支持引用。 | 已对齐 |
| raw data/diagnostics | Python API 有 query_data，WebUI有相应数据能力。 | 已对齐：Chat 消息级检索数据面板 |
| Graph viewer | Python React 图谱工作台：Sigma、布局、搜索、属性、编辑。 | 已对齐主要体验：React/Vite graph workbench |
| System status | Python health 主要是 API/Server 状态，WebUI 可感知服务状态。 | .NET 增强：React System Status 展示 `/api/system/health` 的 evidence、remediation、fix-first 和 feature impact |
| Settings/Labels/Layout/Zoom/Fullscreen/Legend | Python 图谱工作台控件。 | 已对齐主要控件 |
| hover/focus/selection/neighborhood | Python 图谱交互。 | 已对齐 |
| i18n | Python WebUI 有 i18n。 | 未对齐 |
| search history | Python WebUI 有 search history manager。 | 未对齐 |
| pipeline busy auto refresh | Python WebUI 有更完整 pipeline busy 语义。 | 部分对齐：SignalR + table reload，但非完全同款 |
| React 全站 | Python WebUI 是 React SPA。 | 已对齐：.NET 前端已迁移到独立 React/Vite SPA |

### 13. 部署、配置与运维

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| PyPI core/server | `lightrag-hku` 与 `[api]` extra。 | 未对齐：当前是源码/solution 运行 |
| uv/pip/source install | Python 文档完整。 | 部分对齐：.NET README 有 restore/build/run |
| Dockerfile / docker-compose | Python 提供 lite/full compose、Docker 镜像、Cosign 验签说明。 | 部分对齐：本仓库有本地 Qdrant/Neo4j compose，无完整应用容器化 |
| K8s deploy | Python 有 Helm/k8s deploy 脚本。 | 未对齐 |
| offline deployment | Python 有离线部署文档和 requirements offline。 | 未对齐 |
| interactive setup | `make env-base/env-storage/env-server/env-security-check`。 | 未对齐 |
| local vLLM embedding/rerank compose | Python setup 可生成本地 vLLM 服务。 | 未对齐 |
| multi-site deployment | Python 支持多站点反代、runtime prefix injection。 | 未对齐 |
| config audit/security check | Python setup 有 env security check。 | 部分对齐：System Status 已检查 LLM/Embedding/Rerank/Qdrant/Neo4j/WorkingDir 等运行配置并做 evidence 脱敏；仍缺启动前安全审计、auth 配置审计和部署向导 |
| systemd service example | `lightrag.service.example`。 | 未对齐 |
| Makefile workflow | Python 有 dev/test/env 等 make 目标。 | 部分对齐：.NET 有脚本 `dev-start.ps1` / `dev-stop.ps1` |

### 14. 评估、复现与研究工具

| Python 功能点 | 说明 | .NET 当前状态 |
| --- | --- | --- |
| 论文复现脚本 | `reproduce/` 包含 Step_0..3、batch_eval。 | 未对齐 |
| RAGAS evaluation | `lightrag/evaluation` 有 RAGAS 评估框架。 | 未对齐 |
| offline retrieval check | 离线 retrieval oracle 检查。 | 未对齐 |
| sample documents/datasets | Python evaluation 样例数据。 | 未对齐 |
| examples 丰富 | OpenAI、Ollama、Gemini、Azure、Bedrock、HF、Mongo、OpenSearch、Milvus、Neo4j、RAG-Anything 等。 | 部分对齐：.NET 只有 `LightRAGNet.Example` |
| graph visual examples | HTML、Neo4j、OpenSearch visual examples。 | 部分对齐：有 React graph workbench |

## LightRAG.NET 当前功能清单

### 1. 解决方案结构

| .NET 功能点 | 当前实现 |
| --- | --- |
| 分层项目 | `Core`、`Share`、`LightRAGNet` core、`LLM`、`Embedding`、`Rerank`、`Storage`、`Hosting`、`Server`、`React`、`Example` |
| .NET 版本 | .NET 10 solution：`LightRAGNet.slnx` |
| DI 组合 | `LightRAGNet.Hosting` 统一注册 provider、storage、core services、task queue |
| 测试结构 | `LightRAGNet.Tests`、`LightRAGNet.Server.Tests`、`LightRAGNet.React/tests` |
| 测试安全 | Server/API 测试隔离真实 Qdrant/Neo4j，clear-all 不碰本机开发数据 |
| 中央包管理 | `Directory.Packages.props` |

### 2. Core RAG 能力

| .NET 功能点 | 当前实现 | 对齐级别 |
| --- | --- | --- |
| 文档插入 | `LightRAG.InsertAsync(content, docId, filePath)` | 已对齐 |
| 文档分块 | token chunking、overlap、chunk metadata | 已对齐 |
| chunk 向量化 | embedding 后写 Qdrant `chunks` | 已对齐 |
| 实体/关系抽取 | LLM extraction + parser | 部分对齐 |
| KG merge | entity/relation builder、description merge、summary、source/file path 聚合 | 已对齐 |
| text chunks KV | `text_chunks` 保存 chunk 内容、tokens、file_path、llm_cache_list | 已对齐 |
| full docs/entities/relations KV | JSON KV 存储 tracking | 已对齐 |
| doc_status | workspace-scoped document lifecycle record | 已对齐 |
| task state event | `TaskStateChanged` + buffer + subscriber isolation | 已对齐 .NET 自身需要 |

### 3. 查询能力

| .NET 功能点 | 当前实现 | 对齐级别 |
| --- | --- | --- |
| Query modes | Local、Global、Hybrid、Naive、Mix、Bypass | 已对齐 |
| KG context | `RetrievalContextService` + Local/Global/Mix strategies | 已对齐 |
| Naive query | chunk vector-only retrieval | 已对齐 |
| Bypass query | direct LLM generation | 已对齐 |
| query cache | answer cache、keyword cache、workspace revision | 已对齐 |
| query_data | structured entities/relationships/chunks/references/metadata | 已对齐 |
| references | reference_id/file_path | 已对齐 |
| token budget | entity/relation/total budget builder | 已对齐 |
| rerank | Aliyun rerank + long chunk max aggregation | 部分到已对齐 |
| SSE streaming | Server `text/event-stream` + Web streaming display | 已对齐 |

### 4. 文档管理与任务队列

| .NET 功能点 | 当前实现 | 对齐级别 |
| --- | --- | --- |
| Markdown/text intake | `/api/MarkdownDocuments/text`、`/upload` pipeline-style batch | 部分对齐 |
| legacy Markdown upload | 单文件 `.md/.markdown` 上传，手动 Add to RAG | 项目既有能力 |
| track status | `/tracks/{trackId}` | 已对齐 |
| retry | `/{id}/retry` | 已对齐近似 |
| cancel document | `/{id}/cancel` | 已对齐近似 |
| cancel track | `/tracks/{trackId}/cancel` | 已对齐近似 |
| document list | 分页、状态过滤、active task 状态刷新 | 已对齐近似 |
| SignalR | task status 实时通知 | .NET 增强 |
| task persistence | JSON task state store + atomic write | .NET 增强 |
| clear-all | SQLite row、uploads、KV、外部存储清理边界 | 部分对齐 |
| PDF/DOCX | `ManagedCode.MarkItDown` 本地转换、original artifact、`converted.md`、conversion queue、retry/cancel/delete/clear-all | 部分对齐 |

### 5. 删除与图谱治理

| .NET 功能点 | 当前实现 | 对齐级别 |
| --- | --- | --- |
| delete indexed document | 删除 chunks、vectors、full docs、tracking、prune/rebuild graph、可选 cache | 已对齐核心行为 |
| delete entity | API + GraphCurationService + vector consistency | 已对齐 |
| delete relation | API + GraphCurationService + vector consistency | 已对齐 |
| create/edit entity | 支持属性更新、rename、allow merge | 已对齐 |
| create/edit relation | 支持关系属性更新 | 已对齐 |
| merge entities | 迁移关系、合并数据、operation summary | 已对齐 |
| graph labels | `/api/graph/labels` | 部分对齐 |
| graph query | label/maxDepth/maxNodes | 已对齐 |
| graph config | `GraphView:MaxNodesLimit` + `/api/graph/config` | .NET 增强 |

### 6. React 产品能力

| .NET 功能点 | 当前实现 | 对齐级别 |
| --- | --- | --- |
| RAG Chat | React chat workspace | 已对齐 |
| query toolbar | mode、response、stream/cacheable、references、rerank、TopK、ChunkTopK、keywords、debug output | 已对齐 |
| message references | assistant message 可展开 references | 已对齐 |
| diagnostics | high/low keywords、metadata diagnostics | 已对齐 |
| retrieval data dialog | 每条 assistant 回复可查看 raw retrieval data | 已对齐 |
| Markdown document list | 上传、查看、下载、Add to RAG、retry/cancel/delete、状态/进度，支持 PDF/DOCX 转换状态 | 部分对齐 |
| React graph workbench | Sigma graph canvas、布局、搜索、属性面板、图谱治理控件 | 已对齐主要体验 |
| React system status | `/system-status` 展示 `GET /api/system/health` 的 checks、evidence、fix-first、feature impact 和 JSON 复制 | .NET 增强 |
| React SPA shell | 独立 React/Vite shell 承载主要产品页面 | 已对齐 |

### 7. Server 运维状态

| .NET 功能点 | 当前实现 | 对齐级别 |
| --- | --- | --- |
| System health API | `GET /api/system/health` | 部分对齐 Python health |
| health checks | Server API、SQLite、WorkingDir、Qdrant、Neo4j、LLM config、Embedding config、Rerank config、RAG task queue、Conversion queue | .NET 增强 |
| evidence/remediation | 结构化 evidence、字符串 remediation、fix-first、feature impact、敏感字段脱敏 | .NET 增强 |
| health UI | `/system-status` React 页面 | .NET 增强 |
| 非目标 | 不真实调用模型 provider，不修改配置，不执行 clear-all/clear-cache 等破坏性操作 | 边界已明确 |

### 8. Provider 与存储

| .NET 功能点 | 当前实现 | 对齐级别 |
| --- | --- | --- |
| LLM | DeepSeek OpenAI-compatible | 部分对齐 |
| Embedding | Aliyun embedding | 部分对齐 |
| Rerank | Aliyun rerank | 部分对齐 |
| Vector store | Qdrant | 部分对齐 |
| Graph store | Neo4j | 部分对齐 |
| KV store | JSON file KV | 部分对齐 |
| Metadata DB | SQLite/EF Core for Server document rows | .NET 自身设计 |

## 对照矩阵

| 领域 | Python LightRAG | LightRAG.NET | 差距判断 |
| --- | --- | --- | --- |
| Core RAG 主链 | 完整 | 基本完整 | .NET 已具备核心可用性 |
| Query modes | 完整 | 完整 | 已对齐 |
| Query cache | 完整，并有维护工具 | 核心对齐，工具不足 | 补运维 API/UI |
| Rerank | provider 多，chunking 完整 | Aliyun + chunking | provider 生态缺口 |
| Document pipeline | 目录扫描、上传、多格式、track、reprocess、cancel | Markdown/text、track、retry、cancel | 多格式与 scan 是大缺口 |
| Document conversion | PDF/DOCX、docling/Office 生态 | PDF/DOCX 本地 MarkItDown | 已补首个切片，仍缺 PPTX/XLSX/OCR/scan |
| Document lifecycle/deletion | 完整 | 核心对齐 | 边界/事务性仍可加强 |
| Graph curation | 完整 | 主路径对齐 | label search/export 等缺口 |
| WebUI | React SPA，文档/查询/图谱 | React/Vite SPA | i18n/search history 未对齐 |
| API Server | FastAPI 全入口、auth、health、Ollama-compatible | ASP.NET Core 主业务 API + `/api/system/health` | auth/Ollama/prefix/cache 管理缺口明显 |
| Provider | 多 LLM/Embedding/Rerank | DeepSeek/Aliyun | 生态差距大 |
| Storage | Json/Redis/Postgres/Mongo/OpenSearch/Milvus/Faiss/Qdrant/Neo4j/Memgraph 等 | Json/Qdrant/Neo4j | 生态差距大 |
| Deployment | PyPI、Docker、K8s、offline、interactive setup、多站点 | 本地开发脚本 + compose for Qdrant/Neo4j | 产品化部署差距大 |
| Evaluation | RAGAS、offline oracle、paper reproduce | 无等价 | 明显缺口 |
| Observability | TokenTracker、Langfuse | 常规日志 | 明显缺口 |

## 下一阶段建议

### P0：缓存与维护工具

PDF/DOCX 和 System Status 已经落地后，最值得补的是 cache management。当前 .NET 已经有 query/keyword/extract/summary cache、workspace revision 和删除时 cache 清理能力，但缺 Web/API 入口，用户无法看见缓存占用、命中边界和清理结果。这个缺口会直接影响调试、复测和运维。

建议验收：

- 后端返回 query cache、keyword cache、extract cache、summary cache、workspace revision 的可解释统计。
- 支持按类别清理：全部 cache、query/keyword cache、按 doc id 清 extraction cache。
- Web 入口只展示真实后端数据，不发明命中率或节省 token 等无源指标。
- 清理操作必须有确认、结果回显和测试隔离，不能误删本机开发 Qdrant/Neo4j 数据。

### P1：Graph 和 WebUI 收口

Graph workbench 已经很像 Python 版，下一步应该补产品缺失而不是继续调皮肤：

- label search API 与前端接入。
- expand/prune 后端联动。
- graph export：CSV/Excel/Markdown/TXT 先做 CSV/Markdown 即可。
- search history、i18n 可后置，除非目标是对外发布。

### P2：Server 安全与部署边界

System Status 已经解决“当前系统到底哪里坏”的入口，但 Python 版 Server 的产品化边界还包括可保护、可部署、可反代。建议补：

- 认证：API key 或最小 bearer auth，至少保护 destructive endpoints。
- 配置审计：启动时检查关键配置缺失、embedding dimension 变更风险、Qdrant/Neo4j 连接风险。
- reverse proxy 文档：SSE、上传大小、CORS、API/Web 分离端口。
- 应用容器化：不仅是 Qdrant/Neo4j compose，还要有 Server/Web 的可运行部署故事。

### P3：文档接入第二阶段

PDF/DOCX 已经是可靠首版，但 Python 文档接入面仍宽很多。第二阶段建议按真实使用价值继续扩：

- `.txt` 文件上传入口，避免只能走 text API。
- `/documents/scan` 或本地目录扫描。
- PPTX/XLSX 作为 Office 第二切片。
- OCR/扫描版 PDF 后置，除非实际样本已经逼近这个需求。

### P4：Provider / Storage 生态

这个方向价值高，但容易发散。建议按真实部署需求选择：

- Provider：优先 OpenAI-compatible 泛化，而不是逐个 provider 硬编码。
- Embedding：先补 asymmetric embedding 配置与重建提醒。
- Storage：如果要扩，优先 PostgreSQL 一体化或 OpenSearch 一体化，因为 Python 版已有完整参考，部署故事也更完整。

### P5：评估与可观测性

当文档接入和 Server 产品化稳定后，再补：

- RAGAS 或简化版 offline retrieval evaluation。
- Token usage tracking。
- Langfuse/OpenTelemetry 一类 tracing。
- 回归基准数据集，用于评估 query mode、rerank、cache 变更收益。

## 推荐短期任务拆分

1. 补 cache management API + Web 入口：先做只读统计，再做有确认的清理动作。
2. 补 graph label search + export：让图谱工作台从“可看可改”走向“可治理可带走”。
3. 补 API key / bearer auth 和 destructive endpoint 保护。
4. 补 `.txt` 文件上传和 `/documents/scan` 设计，作为文档接入第二阶段。
5. 做一份最小 evaluation fixture：固定 5-10 个文档、问题、期望引用，给后续改算法兜底。

## 主要取证来源

- Python Core：`LightRAG/lightrag/lightrag.py`、`LightRAG/lightrag/operate.py`、`LightRAG/lightrag/base.py`
- Python API：`LightRAG/lightrag/api/lightrag_server.py`、`LightRAG/lightrag/api/routers/document_routes.py`、`query_routes.py`、`graph_routes.py`、`ollama_api.py`
- Python provider/storage：`LightRAG/lightrag/llm/`、`LightRAG/lightrag/kg/`
- Python docs：`LightRAG/README-zh.md`、`LightRAG/docs/ProgramingWithCore.md`、`LightRAG/docs/AdvancedFeatures.md`、`LightRAG/docs/LightRAG-API-Server-zh.md`
- .NET Core：`src/LightRAGNet/LightRAG.cs`、`src/LightRAGNet/LightRAGOptions.cs`、`src/LightRAGNet/Services/`
- .NET Server/React：`src/LightRAGNet.Server/Controllers/`、`src/LightRAGNet.Server/Services/`、`src/LightRAGNet.React/src/`
- .NET storage/provider：`src/LightRAGNet.Storage/`、`src/LightRAGNet.LLM/`、`src/LightRAGNet.Embedding/`、`src/LightRAGNet.Rerank/`
- 已交付历史：`docs/superpowers/archives/INDEX.md`
- PDF/DOCX 已交付：`docs/superpowers/archives/2026-05/2026-05-22-managedcode-markitdown-document-intake-archives.md`
- System Status 已交付：`docs/superpowers/archives/2026-05/2026-05-24-server-operational-readiness-archives.md`
