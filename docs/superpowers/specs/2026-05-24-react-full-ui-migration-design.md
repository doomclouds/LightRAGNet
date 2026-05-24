# React Full UI Migration Design

- Date: 2026-05-24
- Topic slug: `react-full-ui-migration`
- Scope: `Migrate all Blazor-hosted React pages into the standalone React frontend and establish the new React UI shell`
- Tags: `react`, `frontend-migration`, `blazor-coexistence`, `dark-ops`, `documents`, `rag-chat`, `graph`, `system-status`, `cache-management`

## Context

LightRAGNet 当前有两套前端形态：

- `src/LightRAGNet.Web`：Blazor Server 主壳，负责顶部栏、左侧菜单、底部 SignalR 状态条、Clear All Data 入口，并通过 Razor 页面加载多个 React island。
- `src/LightRAGNet.Web/ClientApp`：Blazor 项目下的 React/Vite islands，已包含 RAG Chat、Knowledge Graph、System Status、Cache Management，以及共享 `dark-ops` 主题。
- `src/LightRAGNet.React`：新的独立 React/Vite 前端，目前已经有 Documents 和 Upload，但视觉仍是浅色临时版，壳层、表格、菜单和页面切换规范尚未建立。

本阶段目标不是继续保留多个 React island，而是把 `src/LightRAGNet.React` 升级为新的完整 UI 前端。Blazor 项目中的 React 单页能力需要迁入独立 React，同时保留现有已认可的交互和页面语义。

## Approved Visual Direction

视觉稿已落地：

- [React full UI migration concepts](../visuals/2026-05-24-react-full-ui-migration-concepts.html)

用户已认可整体方向：

- 新 React 前端使用深色 `dark-ops` 主题。
- 壳层参考 Blazor 当前结构：顶部栏、左侧菜单、底部 SignalR 状态条、Clear All Data 入口。
- 页面视觉参考 Blazor 项目下已完成的 React 单页，而不是重新做一套浅色 UI。
- Documents 页面按当前视觉稿重设计。
- RAG Chat 参数必须完整保留，不能漏项。
- Knowledge Graph 尽量原样迁移，相关内容、按钮和交互不要动。

## Goals

1. 将 `src/LightRAGNet.Web/ClientApp` 中已完成的 React 页面迁移到 `src/LightRAGNet.React`。
2. 建立独立 React 前端的应用框架：Shell、导航、页面容器、顶部操作区、底部 SignalR 状态条。
3. 将 `dark-ops` 主题迁入独立 React，并作为唯一默认主题。
4. 重设计 Documents 和 Upload，使它们匹配 Blazor 下 React 单页的深色风格、信息密度和表格规范。
5. 保留 RAG Chat 当前能力和参数，不降低配置面。
6. Knowledge Graph 原样迁移，避免在本阶段重做按钮、浮层、面板和图谱交互。
7. 迁移 System Status、Cache Management 和 Document Preview，使它们成为独立 React routes。
8. 保留 `LightRAGNet.Server` 作为 API、SignalR 和 preview 后端。

## Migration Principle

Blazor 项目下已经是 React 实现的页面，默认按“直接迁移”处理：

- 优先移动/复用现有 React 组件、API client、types、store、tests 和 CSS。
- 保留现有功能、按钮、控件、文案、交互路径和数据流。
- 只做为了进入独立 React frontend 必需的适配：route、import alias、shared theme import、Shell content fit、build config、测试路径和 API base 传递。
- 只有当现有页面和已确认的新 Shell / `dark-ops` UI 规范出入很大时，才做局部重设计。
- 局部重设计必须有明确原因，例如：当前 Documents 浅色临时样式不符合新深色框架、表格信息密度不足、页面结构不是完整 React frontend 的可复用形态。

这条原则尤其适用于 RAG Chat、Knowledge Graph、System Status 和 Cache Management：它们已经是 Blazor-hosted React islands，迁移时应尽量保留当前实现，而不是借迁移重写。

## Non-Goals

- 不在本阶段删除 `src/LightRAGNet.Web`。
- 不重写 Knowledge Graph 的图谱交互、按钮、浮层布局、设置面板、图例或属性面板。
- 不重新设计 RAG Chat 为三栏 cockpit。
- 不移除 RAG Chat 当前 query settings 参数。
- 不引入主题切换 UI。
- 不改变后端 API 语义，除非独立 React 路由挂载暴露出必要缺口。
- 不把 Blazor / MudBlazor 组件直接带入 `src/LightRAGNet.React`。

## Target Routes

独立 React 前端承载以下 routes：

```text
/                         RAG Chat
/documents                Document list
/documents/upload         Upload document
/document-preview         Safe document preview empty state
/document-preview/:id     Safe document preview
/graph-view               Knowledge Graph
/system-status            System Status
/cache-management         Cache Management
```

`/documents` 不再是唯一首页。RAG Chat 应成为独立 React 前端的默认入口，保持当前 Blazor nav 中 `/` 对应 RAG Chat 的语义。

## Application Shell

新 React shell 由以下区域组成：

- Top bar：品牌、当前 API base 提示、菜单收起按钮、Clear All Data 操作。
- Left navigation：RAG Chat、Documents、Upload、Knowledge Graph、System Status、Cache Management、Document Preview 入口。
- Main content：按页面类型承载 table workbench、chat workbench、graph canvas、diagnostics dashboard。
- Bottom status bar：SignalR connection status，语义对齐现有 Blazor 底部状态条。

Shell 不应使用 MudBlazor。React 内部使用共享组件和 CSS token 实现相同信息架构。

## Shared UI Standard

迁入 `src/LightRAGNet.React/src/shared`：

- theme tokens：来自 `src/LightRAGNet.Web/ClientApp/src/styles/theme.css`
- Shell layout
- page header
- page meta chips
- panel
- toolbar
- tabs
- buttons
- icon buttons
- inputs/selects/textareas
- switches/checkboxes
- data table
- status chip/pill
- progress bar
- loading/empty/error states
- dialog or drawer surfaces
- elevation tokens：panel、popover、drawer、modal 分层阴影
- overlay/scrim tokens：drawer 和 modal 打开时使用半透明遮罩

样式原则：

- 使用 `dark-ops` 作为唯一默认主题。
- 页面背景、面板、边框、文字、按钮和状态色使用语义 token。
- 表格密度参考 Cache Management。
- 页面标题下的 chips 只能展示真实当前状态，不做无意义功能宣传标签。
- 操作按钮必须对应真实命令，不增加“看起来不错但没有当前业务语义”的按钮。
- UI 不能做成完全扁平：普通 panel 使用轻阴影，hover/active surface 稍微抬高，drawer/modal 使用更高阴影和遮罩，明确高低层级。
- 遮罩不应纯黑盖死内容，建议 `rgba(2, 6, 12, 0.58)` 级别，并保留背景轮廓，让用户知道当前上下文仍在后面。

## Documents

Documents 是第一个重设计落地页。

必须保留当前 React Documents 功能：

- 分页服务端列表。
- status filter。
- loading / empty / network error states。
- file name、file size、upload time、RAG status、progress、error summary、added time。
- View、Download、Add to RAG、Retry、Cancel、Delete。
- SignalR `TaskStatusUpdated` 和 `DataCleared` 刷新。
- 删除确认、行级 pending、删除后当前页修正。

视觉调整：

- 使用 Shell 主内容区。
- 使用 `page header + tabs + summary cards + dense table` 模型。
- status filter 可以保留 select，同时 tabs 提供主要状态切换入口。
- table 使用 Cache Management 的表格密度和状态 pill 风格。
- 点击行级 `View` / 小眼睛时，默认在 Documents 当前页面打开右侧 preview drawer，而不是跳走或在表格下方塞临时块。
- preview drawer 使用遮罩、阴影和固定层级覆盖在 Documents 页面上；关闭后回到原列表、筛选和分页状态。
- drawer 内提供 `Open full preview`，进入 `/document-preview/:id` 完整页面；Download 仍使用后端安全下载链接。
- 窄屏时 drawer 退化为 full-screen sheet，避免挤压表格。

## Upload

Upload 作为 Documents 工作流的准备页，而不是营销式上传页。

必须保留：

- `.md`、`.markdown`、`.pdf`、`.docx`
- 最多 10 个文件
- 单文件最大 10 MB
- 本地拒绝 unsupported extension、oversized file、duplicate selected file name
- 单次 multipart 上传，field name 为 `files`
- 上传成功后提示后续从 Documents 执行 Add to RAG
- 不自动 Add to RAG

视觉调整：

- 使用深色 dropzone。
- 右侧或下方展示 selected file list。
- validation messages 使用统一 banner / row status。

## RAG Chat

RAG Chat 从 Blazor-hosted React island 迁入独立 React route `/`。

布局保持轻量双栏：

- 左侧：conversation、assistant markdown、streaming state、errors、references、message-level details、composer。
- 右侧：Query Settings。

必须完整保留当前 Query Settings 参数：

- `Mode`: `Mix`, `Naive`, `Bypass`, `Local`, `Global`, `Hybrid`
- `Response`: `Multiple Paragraphs`, `Single Paragraph`, `Bullet Points`, `Concise`
- `Streaming`
- `References`
- `Rerank`
- `TopK`
- `ChunkTopK`
- `High keywords`
- `Low keywords`
- `Debug output`: `Answer`, `ContextOnly`, `PromptOnly`

展示规则：

- 页面标题下方只展示真实当前状态，例如当前 mode 和 stream state。
- 不添加无语义的 `References`、`Message diagnostics` 标题 chip。
- References 只在后端 metadata 返回 references 时显示在 assistant message 中。
- `View query details` 保持 message-scoped，不把 diagnostics 常驻铺满主页面。

## Knowledge Graph

Knowledge Graph 使用原样迁移策略。

必须保持：

- existing query controls
- search box
- layout controls
- zoom/focus/fullscreen controls
- settings panel
- legend
- properties panel
- edit/merge/delete dialogs
- Sigma graph behavior
- transparent canvas-layer fix
- current graph curation API semantics

本阶段只允许做：

- route migration
- shared API/import path adaptation
- Shell content fit
- build output adaptation
- CSS token import / theme fit
- viewport sizing under new React shell

不允许做：

- 重新排列按钮
- 删除或新增图谱控件
- 改变图谱操作语义
- 把图谱页面改成普通表格页

## System Status

System Status 迁入独立 React route `/system-status`。

保留当前能力：

- `GET /api/system/health`
- overall status
- evidence
- remediation
- fix-first priorities
- feature impact
- raw JSON copy/export behavior

视觉保持当前 dark diagnostics page，挂入新 Shell 后只做边距、最大宽度和状态条适配。

## Cache Management

Cache Management 迁入独立 React route `/cache-management`。

它是 `dark-ops` 的基准页，迁移时尽量保持现有布局：

- summary cards
- workspace input
- time window segmented control
- refresh
- copy JSON
- cache family table
- insights
- trend
- clear plan
- entry drilldown
- measurement contract

仅做必要的路径、共享组件、Shell fit 和构建迁移。

## Document Preview

Document Preview 作为独立安全 route：

```text
/document-preview
/document-preview/:id
```

用途：

- Documents `View`
- RAG Chat reference link
- 左侧导航进入 `/document-preview` 时显示安全 empty state，不伪造任何文件内容。

入口行为：

- RAG Chat assistant message 中的 reference link 默认打开新的 React preview page：`/document-preview/:id`，建议 `target="_blank"`，避免用户丢失当前对话、流式回答和 query details 上下文。
- Documents 列表中的 `View` / 小眼睛默认打开同页 preview drawer，适合快速扫文件内容；drawer 内可以二次打开完整 preview page。
- 两个入口都调用同一套 `GET /api/document-preview/{id}/content` 数据契约，不能各做一套渲染规则。

规则：

- frontend 不根据 `filePath` 猜链接。
- 后端返回 preview metadata 后，frontend 只能基于后端给出的安全 `previewUrl` / `openKind` 进入 React preview route，不能基于 `filePath`、文件名或本地路径拼接。
- 如果 `previewUrl` 不是可识别的 DocumentPreview URL，则按后端返回的安全 URL 作为外部链接打开，不做本地猜测。
- Markdown/text 直接渲染内容。
- PDF 使用 original artifact route。
- DOCX 优先显示 converted Markdown，并提供 original action。
- unresolved/external references 显示为普通文本，不伪造链接。

## Migration Strategy

迁移顺序建议：

1. 建立 React Shell 和 shared `dark-ops` UI layer。
2. 迁移/重构 Documents 和 Upload，使它们成为新 UI 规范样板。
3. 迁移 RAG Chat，确保参数完整和 message details 行为不回退。
4. 原样迁移 Knowledge Graph，重点验证 canvas、浮层和 resize。
5. 迁移 System Status。
6. 迁移 Cache Management。
7. 迁移 Document Preview route，并统一 Documents/RAG references 入口。
8. 根据验证结果决定 Blazor host routes 是否进入下一阶段删除计划。

## Testing Strategy

React tests:

- Shell navigation route rendering。
- SignalR status bar state rendering。
- Documents table/tabs/filter/actions。
- Upload validation and submit flow。
- RAG Chat request building covers all settings。
- RAG Chat title chips only show mode and stream state。
- RAG Chat references render only from metadata.
- Graph route preserves expected control labels/classes.
- System Status consumes server health payload without local aggregation drift。
- Cache Management retains summary/table/clear-plan behavior。
- Document Preview renders safe content states。

.NET / host tests:

- Existing Blazor host tests remain until Blazor removal phase。
- Server preview and API tests stay passing。
- `dotnet test LightRAGNet.slnx` must not mutate real Qdrant/Neo4j data。

Visual verification:

- desktop and narrow viewports for all new React routes。
- Graph canvas nonblank, dark, and controls usable。
- RAG Chat details dialog above Shell chrome。
- Documents table no overlapping controls or clipped action text。
- Documents preview drawer has visible scrim, elevation, close action, and preserves list context。
- RAG Chat reference preview opens a full React preview route without losing chat context。
- Upload validation states readable。
- System Status and Cache Management keep dark contrast。

## Acceptance Criteria

- `src/LightRAGNet.React` owns all user-facing React UI routes listed in this design。
- Shared `dark-ops` theme and Shell are in the standalone React project。
- Documents and Upload match the approved visual direction。
- RAG Chat preserves all current settings and message details behavior。
- Knowledge Graph is migrated with existing controls and interaction semantics unchanged。
- System Status and Cache Management migrate without losing current functionality。
- Document Preview is available as a safe React route。
- React typecheck, tests, and build pass。
- Relevant .NET tests pass。
- Visual verification covers every migrated route。

## Open Questions

- Whether Blazor routes should redirect to the standalone React frontend in the next phase, or remain manually accessible until removal.
- Whether Clear All Data needs an additional permission/confirmation copy beyond the migrated Blazor confirmation behavior.
- Whether Document Preview should be implemented as a page first, then optionally exposed as a drawer from Documents later.
