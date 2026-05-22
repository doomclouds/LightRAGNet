# ManagedCode MarkItDown PDF/Word Document Intake Design

- Date: `2026-05-22`
- Topic slug: `managedcode-markitdown-document-intake`
- Status: `Ready for implementation planning`
- Scope: `PDF/DOCX upload + original file persistence + Add to RAG triggered local Markdown conversion + existing RAG indexing pipeline`
- Tags: `document-intake`, `managedcode-markitdown`, `pdf`, `docx`, `offline-conversion`, `add-to-rag`

## Purpose

LightRAGNet 当前已经有文档列表、上传、手动 `Add to RAG`、RAG 队列状态、重试、取消和删除能力。本阶段要把上传格式从 Markdown/text 扩展到 PDF 和 Word `.docx`，但不能改变产品主语义：**上传只是把文件放进系统，真正进入 RAG 必须由用户点击“加入 RAG”触发。**

首版目标：

```text
Upload source file
  -> show original file in document list
  -> user clicks Add to RAG
  -> convert source file to converted.md
  -> enqueue existing RAG indexing task with converted Markdown
  -> update document status through existing task pipeline
```

文件列表里显示的文件名始终是用户上传的原始文件名，例如 `合同.pdf`、`产品说明.docx`。`converted.md` 是系统内部派生产物，不替代列表里的显示名称。

## Design Principles

- 首版只支持 `.pdf` 和 `.docx`。
- 上传阶段只保存原始文件和元数据，不转换，不切片，不入 RAG 队列。
- `Add to RAG` 阶段才开始转换和索引 pipeline。
- 转换后的 Markdown 长期保存为 `converted.md`，不是临时字符串。
- 索引只读取已保存的 Markdown artifact，不直接从 PDF/DOCX 切片。
- 默认转换必须本地离线运行，不依赖 Azure、OpenAI、Google、AWS、OCR 服务、URL 抓取或任何第三方在线服务。
- `ManagedCode.MarkItDown` 必须被封装在 LightRAGNet 自己的 converter 接口后面。

## Reference Semantics

默认转换库使用 `ManagedCode.MarkItDown`，它是原生 .NET/C# document-to-Markdown 库，PDF 底层使用 PdfPig，Office 文档使用 DocumentFormat.OpenXml。

不采用 `Microsoft.Extensions.DataIngestion.MarkItDownReader` 作为首版默认实现，因为它是 preview 包，并且默认依赖 `markitdown` executable。Microsoft Python MarkItDown 只作为产品语义参考，不作为运行时依赖。

`ManagedCode.MarkItDown` 支持 URL、AI enrichment、云 provider 等更大能力，但 LightRAGNet 首版不暴露这些能力，也不配置 Azure、Google、AWS、LLM、音视频转写或网络型 provider。默认转换路径只接收系统已经保存到本地 artifact store 的 PDF/DOCX 文件。

## Current State

当前系统已有：

- `POST /api/MarkdownDocuments`：legacy 单文件 Markdown 上传，上传后不自动加入 RAG。
- `POST /api/MarkdownDocuments/{id}/add-to-rag`：用户手动把文档加入 RAG 队列。
- `DocumentIntakeService.SubmitUploadedFilesAsync(...)`：pipeline-style 批量上传入口，当前偏 Markdown/text intake。
- 文档列表：未加入 RAG 的文档显示 `Add to RAG` 按钮。
- RAG 状态字段：`IsInRagSystem`、`RagStatus`、`RagCurrentStage`、`ActiveRagTaskId`、retry/cancel 相关字段。

当前缺口：

- PDF/DOCX 原始文件没有长期 source artifact。
- 没有保存转换后的 Markdown artifact。
- `Add to RAG` 不能按文档类型区分“直接索引 Markdown”和“先转换再索引 PDF/DOCX”。
- pipeline 还没有明确的 `Converting` 阶段。

## Scope

首版支持：

- `.pdf`
- `.docx`

首版不支持：

- `.doc`
- `.pptx`
- `.xlsx`
- `.csv`
- `.html` / `.htm`
- `.json`
- `.xml`
- 图片 OCR
- 扫描版 PDF OCR
- ZIP/目录批量扫描
- URL 抓取或网页转 Markdown

扫描件或图片型 PDF 如果无法提取出有效 Markdown，应在 `Add to RAG` 后的转换阶段失败，错误信息提示“无法提取可索引文本”，但不承诺 OCR。

## Storage Model

文件系统保存内容，SQLite 保存元数据。

文件系统示意：

```text
{LightRAG:WorkingDir}/documents/{documentId}/original.pdf
{LightRAG:WorkingDir}/documents/{documentId}/converted.md
```

对于 DOCX：

```text
{LightRAG:WorkingDir}/documents/{documentId}/original.docx
{LightRAG:WorkingDir}/documents/{documentId}/converted.md
```

SQLite 新增或等价表达这些元数据：

```text
OriginalFileName
OriginalFilePath
OriginalContentType
OriginalContentHash
ConvertedMarkdownPath
ConvertedMarkdownHash
ConversionStatus
ConversionErrorMessage
ConversionStartedAt
ConversionCompletedAt
ConversionTool
ConversionToolVersion
```

`MarkdownDocument.FileName` 保持原始上传文件名，供列表显示。`MarkdownDocument.Content` 对 PDF/DOCX 可在转换成功后冗余保存 converted Markdown，以兼容现有详情接口和队列接口；权威 Markdown artifact 仍是 `ConvertedMarkdownPath`。

## State Model

新增转换状态：

```text
NotStarted
NotRequired
Queued
Processing
Completed
Failed
```

上传 PDF/DOCX 后：

```text
FileName = 原始文件名，例如 合同.pdf
OriginalFilePath = documents/{documentId}/original.pdf
ConversionStatus = NotStarted
RagStatus = null
RagCurrentStage = null
ActiveRagTaskId = null
IsInRagSystem = false
```

这时文档列表显示“加入 RAG”按钮。按钮不应被 `Queued` 状态禁用，因为还没有进入 RAG pipeline。

用户点击 `Add to RAG` 后：

```text
ConversionStatus = Queued
RagStatus = Queued
RagCurrentStage = Accepted
ActiveRagTaskId = null
```

转换 worker claim 后：

```text
ConversionStatus = Processing
RagStatus = Processing
RagCurrentStage = Converting
```

转换成功并成功入现有 RAG 队列后：

```text
ConversionStatus = Completed
RagStatus = Queued
RagCurrentStage = Indexing
ActiveRagTaskId = <rag task id>
ConvertedMarkdownPath = documents/{documentId}/converted.md
```

之后由现有 `RagTaskStatusChangedHandler` 根据 RAG task 更新 `Processing`、`Completed`、`Failed`、`Cancelled` 等索引状态。

如果转换成功但入 RAG 队列失败：

```text
ConversionStatus = Completed
RagStatus = Failed
RagCurrentStage = Indexing
ActiveRagTaskId = null
ConvertedMarkdownPath = documents/{documentId}/converted.md
RagErrorMessage = Document could not be queued for indexing.
```

这不是转换失败，不能把 `ConversionStatus` 回滚成 `Failed`。

## Pipeline Semantics

上传请求流程：

```text
POST /api/MarkdownDocuments/upload
  -> validate extension and size
  -> persist original file
  -> create MarkdownDocument row
  -> ConversionStatus = NotStarted
  -> RagStatus = null
  -> return 202 + track_id + document DTOs
```

上传请求不做：

- 不运行 `IDocumentMarkdownConverter`
- 不写 `converted.md`
- 不调用 `IRagTaskQueueService.EnqueueTaskAsync`
- 不把文档标记为 `Queued`

加入 RAG 流程：

```text
POST /api/MarkdownDocuments/{id}/add-to-rag
  -> if document is already in RAG or active, reject
  -> if document has OriginalFilePath and extension is .pdf/.docx:
       ConversionStatus = Queued
       RagStatus = Queued
       RagCurrentStage = Accepted
       return updated DTO
  -> else:
       use existing Markdown/text enqueue behavior
```

后台转换流程：

```text
Queued
  -> Converting
       - load original file
       - run IDocumentMarkdownConverter
       - validate non-empty Markdown
       - write converted.md
       - write converted hash and conversion metadata
  -> Indexing
       - read converted.md
       - enqueue existing LightRAG indexing task
  -> existing RAG task pipeline
```

## Post-Conversion Handoff

每个文件在后台转换成 Markdown 后，下一步必须立即交给现有 RAG indexing 队列，而不是停留在 converted artifact 状态。

成功 handoff 顺序：

```text
1. 保存 converted.md
2. 校验 converted.md 可读取且非空
3. 将 converted Markdown 作为 content 调用 IRagTaskQueueService.EnqueueTaskAsync(...)
4. enqueue 成功:
     ConversionStatus = Completed
     RagStatus = Queued
     RagCurrentStage = Indexing
     ActiveRagTaskId = <task id>
5. 后续索引阶段由现有 RagTaskStatusChangedHandler 接管
```

错误边界：

```text
转换失败:
  ConversionStatus = Failed
  RagStatus = Failed
  RagCurrentStage = Converting

转换成功但 RAG enqueue 失败:
  ConversionStatus = Completed
  RagStatus = Failed
  RagCurrentStage = Indexing
  RagErrorMessage = Document could not be queued for indexing.
```

因此 conversion processor 的实现必须把“转换阶段异常”和“索引排队阶段异常”拆开处理。

失败流程：

```text
Converting -> Failed
Indexing   -> Failed
```

状态必须能区分转换失败和索引失败：

```text
转换失败:
  RagStatus = Failed
  RagCurrentStage = Converting
  ConversionStatus = Failed
  ConversionErrorMessage = ...
  RagErrorMessage = ...

索引失败:
  RagStatus = Failed
  RagCurrentStage = <RAG task stage>
  ConversionStatus = Completed
  RagErrorMessage = ...
```

## Retry Semantics

重试分层处理：

```text
如果 ConversionStatus = Failed
  -> 重试时重新进入 ConversionStatus = Queued
  -> 重新转换

如果 ConversionStatus = Completed 且 converted.md 存在
  -> 重试索引时复用 converted.md
  -> 不重新跑 ManagedCode.MarkItDown

如果 ConversionStatus = Completed 但 converted.md 缺失或 hash 不匹配
  -> Markdown artifact 不可信
  -> 重新进入 ConversionStatus = Queued
  -> 重新转换
```

未来可以增加显式“重新转换”操作，但首版不做按钮。

## Converter Boundary

新增 LightRAGNet-owned interface：

```csharp
public interface IDocumentMarkdownConverter
{
    Task<DocumentMarkdownConversionResult> ConvertAsync(
        FileInfo sourceFile,
        string originalFileName,
        string? contentType,
        CancellationToken cancellationToken);
}
```

结果模型：

```csharp
public sealed record DocumentMarkdownConversionResult(
    string Markdown,
    string? DetectedMediaType = null,
    IReadOnlyList<string>? Warnings = null);
```

默认实现：

```text
ManagedCodeDocumentMarkdownConverter
  -> wraps ManagedCode.MarkItDown MarkItDownClient
  -> only accepts .pdf and .docx source files
  -> disables or avoids URL, AI enrichment, cloud intelligence providers, and network-backed converters
```

如果 `ManagedCode.MarkItDown` 在当前环境中不稳定，可以保留接口并切换为更窄的本地实现，例如 `DocSharp.Docx` 处理 DOCX、`PdfPig` 处理 PDF；不要修改 upload/add-to-rag/pipeline 主体。

## API Contract

PDF/DOCX 批量上传入口：

```text
POST /api/MarkdownDocuments/upload
```

输入：

- multipart form-data
- field name: `files`
- allowed extensions: `.pdf`, `.docx`
- max file size: 10 MB per file

成功：

```text
202 Accepted
DocumentSubmissionResponse
  TrackId
  Documents[]
```

返回的每个文档应包含：

- 原始文件名
- `IsInRagSystem = false`
- `RagStatus = null`
- `ConversionStatus = NotStarted`
- track id

`Add to RAG` 入口：

```text
POST /api/MarkdownDocuments/{id}/add-to-rag
```

PDF/DOCX 文档成功：

```text
200 OK
MarkdownDocumentDto
  RagStatus = Queued
  RagCurrentStage = Accepted
  ConversionStatus = Queued
```

Markdown/text 文档成功：

```text
200 OK
MarkdownDocumentDto
  RagStatus = Pending or Queued
  ActiveRagTaskId = <rag task id>
```

失败：

- 文件为空、超限、扩展名不支持、原始文件保存失败时，上传返回 `400`。
- 转换失败发生在 `Add to RAG` 之后的后台阶段，文档状态更新为 `Failed`。
- 已加入 RAG 或已有 active pipeline 的文档再次 `Add to RAG` 返回 `400` 或 `409`。

## Web Boundary

`MarkdownUpload.razor` 首版调整为 PDF/Word 文档上传页：

- `Accept` 改为 `.pdf,.docx`。
- 客户端校验只允许 `.pdf` 和 `.docx`。
- 成功上传后跳转文档列表。
- 文档列表显示原始文件名，例如 `合同.pdf`、`说明书.docx`。
- 上传成功但未加入 RAG 的文档继续显示 `Add to RAG` 按钮。
- 点击 `Add to RAG` 后才显示 `Queued` / `Converting` / `Indexing` 等 pipeline 状态。

首版不做：

- 转换前预览
- converted.md 预览
- Markdown diff
- 显式重新转换按钮
- per-file warning 面板

## Error Handling

上传阶段错误：

- `Unsupported file type. Only .pdf and .docx are supported.`
- `File cannot be empty.`
- `File size cannot exceed 10MB.`
- `Original file could not be saved.`

后台转换阶段错误：

- `Document conversion failed.`
- `Document conversion produced empty Markdown.`
- `Document converter is not available.`

API 返回和 UI 展示不应泄露：

- 本机绝对临时路径
- 环境变量
- executable 参数
- API key
- connection string
- provider request payload

安全与依赖约束：

- 默认转换路径不得访问外部网络。
- 默认转换路径不得要求用户配置 API key。
- 默认转换路径不得调用 Azure、OpenAI、Google、AWS、Document Intelligence、LLM Vision、Whisper API 或其他第三方服务。
- URL、YouTube、Bing、RSS、AI enrichment、audio transcription 等 `ManagedCode.MarkItDown` 扩展能力不进入首版 API 和 UI。

## Testing Strategy

Server/API tests:

- 上传 `.pdf` 后保存原始文件、创建 document row、返回 `202`，但不入 RAG 队列。
- 上传 `.docx` 后保存原始文件、创建 document row、返回 `202`，但不入 RAG 队列。
- 上传后的 DTO 保留原始文件名，并返回 `RagStatus = null`、`ConversionStatus = NotStarted`。
- 上传 `.txt`、`.md`、`.pptx`、`.doc`、`.exe` 返回 `400`。
- 点击 `Add to RAG` 后，PDF/DOCX 文档进入 `ConversionStatus = Queued`、`RagStatus = Queued`。
- 点击 `Add to RAG` 后，Markdown/text 文档继续走现有直接索引队列。

Pipeline/service tests:

- conversion worker 只处理 `ConversionStatus = Queued` 的文档。
- converter 成功后写入 `converted.md` 和 conversion metadata。
- converter 成功后才 enqueue existing RAG task。
- converter 成功但 enqueue existing RAG task 失败时，`ConversionStatus` 保持 `Completed`，`RagCurrentStage` 为 `Indexing`，错误信息指向索引排队失败。
- converter 返回空 Markdown 时标记 `Failed`，不进入 `Indexing`。
- converter 抛异常时标记 `Failed`，错误信息不泄露内部路径。
- 索引失败重试时，如果 `converted.md` 已存在且 conversion completed，复用 Markdown，不重新转换。
- 转换失败重试时重新转换。

Storage tests:

- 原始文件路径落在受控 documents root 内。
- converted Markdown 路径落在同一个 document-owned directory 内。
- 删除未加入 RAG 的文档会清理原始文件。
- 删除已转换文档会清理原始文件和 converted.md。

Web source tests:

- upload page `Accept` 只包含 `.pdf,.docx`。
- 文案不再描述为 Markdown-only。
- 客户端校验拒绝非 PDF/DOCX。
- 未加入 RAG 的 PDF/DOCX 文档仍可显示 Add to RAG 按钮。

## Acceptance Criteria

- 用户可以上传 PDF 和 `.docx`。
- 上传后列表显示原始文件名和扩展名。
- 上传后文档不会自动转换，也不会自动加入 RAG。
- 上传后文档仍显示 `Add to RAG` 按钮。
- 点击 `Add to RAG` 后，系统才开始 PDF/DOCX -> Markdown 转换。
- 转换后的 `converted.md` 长期保存。
- 转换成功后，系统把 converted Markdown 交给现有 RAG indexing task。
- 转换失败和索引失败在状态/阶段/错误中可区分。
- 索引失败重试默认复用已转换 Markdown。
- 转换失败重试会重新转换。
- 默认转换路径在没有第三方服务凭据、没有 Python CLI、没有 `markitdown` executable 的环境中可运行。

## Out of Scope

- 不支持 `.doc`。
- 不支持 PPT/Excel/CSV/HTML/JSON/XML。
- 不支持 OCR 或扫描 PDF 文字识别。
- 不支持 URL 抓取或网页转 Markdown。
- 不支持原始文件下载权限设计。
- 不支持 converted.md 预览。
- 不支持显式重新转换按钮。
- 不改变 query pipeline、rerank、context builder 或 prompt 模板。

## Implementation Planning Decisions

- 文档 root 首版使用 `LightRAG:WorkingDir/documents`，避免新增配置面。
- `MarkdownDocument.FileName` 始终保存并展示原始文件名。
- `MarkdownDocument.Content` 对 PDF/DOCX 在转换成功后可冗余保存 converted Markdown；权威 Markdown artifact 仍是 `ConvertedMarkdownPath`。
- 默认 converter 使用 `ManagedCode.MarkItDown`，不依赖 Python CLI 或 `markitdown` executable。
- 如果 converted.md 被人工删除，重试时自动重新转换；如果重新转换失败，则标记为转换失败。
