# Chunking Strategy Parity Design

- 日期：`2026-06-02`
- 主题标识：`chunking-strategy-parity`
- 状态：`Ready for review`
- 范围：`LightRAGNet core document chunking strategies`
- 标签：`chunking`、`python-parity`、`document-processing`、`semantic-vector`、`paragraph-semantic`

## 背景

LightRAGNet 目前的分块入口集中在 `DocumentProcessingService.ChunkDocument`。现有行为本质上是固定 token 窗口分块，加上可选的 `splitByCharacter` 分隔符路径。它已经覆盖了 Python LightRAG 早期默认的 `chunking_by_token_size` 语义，但还没有对齐 Python LightRAG 近期文件处理管线中明确支持的四种分块策略：

- `F` / Fix：固定 token 窗口分块。
- `R` / Recursive：递归字符分块。
- `V` / Vector：基于句子 embedding 距离突变的语义向量分块。
- `P` / Paragraph：基于标题、段落和表格结构的段落语义分块。

Python 参考来源：

- `https://github.com/HKUDS/LightRAG`
- `docs/FileProcessingPipeline.md`
- `docs/ParagraphSemanticChunking.md`
- `lightrag/chunker/token_size.py`
- `lightrag/chunker/recursive_character.py`
- `lightrag/chunker/semantic_vector.py`
- `lightrag/chunker/paragraph_semantic.py`
- `tests/chunker/*`

本轮目标不是只加一个枚举开关，而是把分块策略升级成可配置、可测试、可解释、可恢复的核心能力。

## 目标

1. 在 .NET 中实现 `F/R/V/P` 四种分块策略。
2. 保持默认 `F` 行为兼容现有索引、缓存和测试。
3. 引入异步分块入口，让 `V` 可以调用 `IEmbeddingService`。
4. 将“太大继续拆”和“太小尝试合并”都作为一等规则。
5. 为每个分块结果保留可测试的 source span / heading / strategy metadata。
6. 在文档入队或处理时冻结 chunking 配置快照，避免重试时使用漂移后的配置。
7. 明确策略切换只影响新索引文档；已有文档需要显式重索引。
8. 用单元测试和插入流程测试覆盖 Python parity 行为和边界。

## 非目标

- 不在本轮新增 React 策略切换控件。
- 不在本轮实现已有文档批量重索引入口。
- 不完整复刻 Python 的 MinerU、Docling 或 native `.blocks.jsonl` 多模态 sidecar 管线。
- 不把 V 策略静默变成向量检索；V 是 semantic breakpoint chunking，不是 vector search。
- 不让默认 `F` 行为发生无意变化。
- 不在默认测试中调用真实 embedding provider。

## 核心决策

新增一个策略化分块服务，替代 `DocumentProcessingService` 内部的单体分块逻辑：

```text
DocumentProcessingService
  -> LightRagChunkingService
      -> FixedTokenChunkingStrategy
      -> RecursiveCharacterChunkingStrategy
      -> SemanticVectorChunkingStrategy
      -> ParagraphSemanticChunkingStrategy
```

`DocumentProcessingService` 仍负责文档处理编排、embedding、实体抽取和 cache 逻辑。分块策略本身下沉到专门服务，避免继续在 `ChunkDocument` 里堆叠 `if/else`。

新增异步入口：

```csharp
Task<IReadOnlyList<Chunk>> ChunkDocumentAsync(
    string content,
    string docId,
    string filePath = "",
    ChunkingRequestOptions? options = null,
    CancellationToken cancellationToken = default);
```

保留现有同步 `ChunkDocument(...)` 入口作为兼容层，只走默认 `F` 逻辑。索引主流程切换到 `ChunkDocumentAsync(...)`。

## 数据模型

内部策略输出使用更丰富的模型，再映射回现有 `Chunk`：

```csharp
public sealed class ChunkingSegment
{
    public string Content { get; init; } = "";
    public int Tokens { get; init; }
    public int Order { get; init; }
    public string Strategy { get; init; } = "";
    public SourceSpan? SourceSpan { get; init; }
    public ChunkHeading? Heading { get; init; }
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>();
}

public sealed record SourceSpan(int Start, int End);

public sealed record ChunkHeading(
    int Level,
    string Heading,
    IReadOnlyList<string> ParentHeadings);
```

现有 `Chunk` 首期可以只消费 `Content`、`Tokens`、`ChunkOrderIndex`、`FullDocId` 和 `FilePath`。`SourceSpan`、`Heading` 和 `Strategy` 先用于测试、配置快照和后续 provenance 扩展。若实现阶段发现现有存储需要保留 heading，可在 `Chunk` 或 chunk metadata 中加窄字段，但不能破坏已有消费者。

## 配置

新增策略配置，保留现有 `ChunkTokenSize`、`ChunkOverlapTokenSize` 作为全局 fallback：

```csharp
public enum LightRagChunkingStrategy
{
    FixedToken,
    RecursiveCharacter,
    SemanticVector,
    ParagraphSemantic
}

public sealed class LightRagChunkingOptions
{
    public LightRagChunkingStrategy Strategy { get; set; } =
        LightRagChunkingStrategy.FixedToken;

    public FixedTokenChunkingOptions FixedToken { get; set; } = new();
    public RecursiveCharacterChunkingOptions RecursiveCharacter { get; set; } = new();
    public SemanticVectorChunkingOptions SemanticVector { get; set; } = new();
    public ParagraphSemanticChunkingOptions ParagraphSemantic { get; set; } = new();
}
```

策略默认：

- `F` 默认继承 `ChunkTokenSize = 1200`，`ChunkOverlapTokenSize = 100`。
- `R` 默认继承全局 token size 和 overlap。
- `V` 默认继承全局 token size，但不使用 overlap。
- `P` 默认 `ChunkTokenSize = 2000`，不继承全局 `1200`，因为段落语义合并需要更大空间。

配置快照规则：

- 文档进入 RAG 处理前冻结本次策略和参数。
- 重试同一个文档时使用冻结快照，而不是读取最新全局配置。
- 如果文档尚未开始处理，取消后重新提交可以使用新配置。
- 快照只保留当前策略实际消费的参数，不保存无关策略完整配置。

## 策略总览

### F：固定 Token 分块

`F` 保持现有默认行为：

1. 对内容做 trim。
2. 若配置了 `splitByCharacter`，先按指定分隔符切。
3. 若 `splitByCharacterOnly = true`，任一段超过 token 上限就失败。
4. 若 `splitByCharacterOnly = false`，超过 token 上限的段继续固定窗口切。
5. 若未配置分隔符，按 token window + overlap 滑动。
6. 继续保留现有尾部小片段合并行为，避免最后产生小于 overlap 的尾块。

`F` 不引入新的小块合并策略，避免默认行为和历史 chunk id 产生非预期变化。

### R：递归字符分块

`R` 是“先在强语义边界切，切不开再逐级降级”的策略。

默认分隔符 cascade：

```text
\n\n
\n
。
！
？
；
，
 
""
```

处理流程：

1. 先尝试用当前最强分隔符切分文本。
2. 对每个切出的 piece 计算 token。
3. 若 piece 小于 token 上限，进入 merge buffer。
4. 若 piece 超过 token 上限，使用下一层分隔符递归处理该 piece。
5. 若已经到空分隔符仍超限，进入字符或 token 级硬切。
6. merge buffer 将相邻小 piece 合并到接近 token 上限，并按配置保留 overlap。

关键点：

- R 不是“一句一个 chunk”，而是 split 后再 merge。
- 超长句子不是异常；它会继续降级到逗号、空格、字符或 token 窗口。
- 所有大小判断使用 tokenizer，不使用字符串长度代替 token 数。
- overlap 必须小于 chunk token size。

### V：语义向量分块

`V` 是 semantic breakpoint chunking。它不是向量检索，不查询 Qdrant，也不匹配已有 chunk。

流程：

1. 用 sentence regex 切句子，默认同时支持英文 `.?!` 和中文 `。？！`。
2. 用 `bufferSize` 构造相邻句子窗口。
3. 调用 `IEmbeddingService.GenerateEmbeddingsAsync(...)` 给窗口生成 embedding。
4. 计算相邻窗口之间的 cosine distance。
5. 根据阈值策略找距离突变点。
6. 按突变点合并句子为语义 chunk。
7. 对过大的语义 chunk 调用 `R` 继续拆分，且 overlap 设为 0。
8. 对过小语义 chunk 尝试与语义距离更近的一侧合并。

阈值策略：

```text
Percentile
StandardDeviation
Interquartile
Gradient
```

规则：

- `NumberOfChunks` 有值时，优先按目标 chunk 数选择断点。
- 没有明显断点时，可能只产出一个语义 chunk，再由 R 兜底处理超长。
- 断点过多导致小块时，先进行语义相邻合并。
- 无 embedding 服务配置时，记录 warning 并 fallback 到 R。
- embedding 调用失败时，不静默 fallback，文档处理应失败并记录清晰 stage。

### P：段落语义分块

Python P 依赖 `.blocks.jsonl` sidecar。LightRAGNet 首期采用 Markdown/converted text 版 block builder：

```text
Markdown / converted text
  -> DocumentBlockBuilder
  -> DocumentBlock[]
  -> ParagraphSemanticChunkingStrategy
```

`DocumentBlock` 初始字段：

```csharp
public sealed class DocumentBlock
{
    public string Content { get; init; } = "";
    public int Level { get; init; }
    public string Heading { get; init; } = "";
    public IReadOnlyList<string> ParentHeadings { get; init; } = [];
    public DocumentBlockKind Kind { get; init; }
    public SourceSpan? SourceSpan { get; init; }
}
```

P 的处理阶段：

1. Stage A：按 Markdown heading 构建初始 block。
2. Stage B：对超长 table 做结构化切分，优先按行切。
3. Stage C：对超长 heading block 做 anchor-driven split。
4. Stage D：对小 block 做 bottom-up、level-aware 合并。

P 的边界：

- 不把上一个顶级标题尾部合并到下一个顶级标题开头。
- 同一 heading 内的长正文可 fallback 到 R，保留配置 overlap。
- 缺少结构化 block 时 fallback 到 R。
- heading block 被拆成多个 fragment 时，heading 增加 `[part n]` 后缀。
- Markdown table 优先按行切；单行 table 超长时再 fallback R。
- code fence 优先保持完整；超长 code block 按行切，仍超长时 fallback R。

## 降级链

完整降级链：

```text
P
  -> 缺少 block / 普通长正文 / 表格或代码兜底
  -> R
      -> 分隔符递归
      -> 字符或 token 硬切

V
  -> 语义 chunk 超过 token 上限
  -> R with overlap = 0
      -> 分隔符递归
      -> 字符或 token 硬切

F
  -> 固定 token 窗口硬切
```

所有策略最终必须满足：

- 不输出空 chunk。
- 不输出超过硬 token 上限的 chunk，除非现有兼容路径明确要求保留失败行为。
- 不死循环。
- 输出顺序与原文一致。

## 小块合并

小块合并是核心需求，不是实现细节。

统一原则：

- 不合并空白内容。
- 合并后必须重新 tokenize 整体内容，而不是简单累加 token。
- 合并时要把中间 separator 的 token 成本算进去。
- 合并不能改变原文顺序。
- 合并不能突破策略语义边界。

策略规则：

- `F`：不额外合并，保持兼容。
- `R`：split 后通过 merge buffer 合并小 piece，天然避免碎片。
- `V`：过小语义块优先与 cosine distance 更小的一侧合并；若左右都无法合并，才保留。
- `P`：使用 bottom-up、level-aware 合并。同级优先，父子级次之，禁止跨顶级标题乱并。

建议配置：

```csharp
public bool MergeSmallChunks { get; set; } = true;
public int MinChunkTokenSize { get; set; } = 0;
```

`MinChunkTokenSize = 0` 表示使用策略默认阈值。首期可先不暴露复杂 UI，只在配置中保留。

## Source Span 与 Provenance

Source span 用于测试和后续 provenance，不要求首期立刻暴露到 API。

规则：

- span 必须指向原文 `[start, end)`。
- `content[span.Start..span.End]` 应等于 chunk content，允许 trim 后 span 对应 trim 后内容。
- 重复文本不能用 naive `IndexOf`；查找必须带游标或锚点，保证 span 单调前进。
- token window span 计算要避免 O(N²) 全前缀 decode。
- P 的 heading/block 来源应保留在 metadata，方便后续文档预览定位和删除清理扩展。

## 错误处理

- 未知策略：配置错误，直接失败。
- 同时指定多个策略：配置错误，直接失败。
- `chunk_token_size <= 0`：配置错误。
- `overlap >= chunk_token_size`：clamp 到 `chunk_token_size - 1`；若 `chunk_token_size <= 1`，overlap 为 0。
- `F splitByCharacterOnly` 遇到超限段：保持现有失败语义。
- `V` 缺少 embedding 服务：warning 后 fallback 到 R。
- `V` embedding 调用失败：文档处理失败，不静默降级。
- `P` 缺少结构化 block：warning 后 fallback 到 R。
- `P` table/code 无法结构化切分：fallback 到 R。

## 策略切换与重索引

chunk id 由 chunk content hash 产生。更换策略会改变 chunk content，也自然改变 chunk id。

规则：

- 策略切换只影响新索引文档。
- 已有文档不会自动重新分块。
- 用户需要显式重索引已有文档，重索引入口不在本轮实现。
- 文档状态或 metadata 应能记录本次使用的 strategy 和关键参数，便于解释“为什么这批 chunk 是这样切的”。

## 集成点

### Core

- 新增 `Services/DocumentProcessing/Chunking/` 目录。
- 新增策略接口和 resolver。
- 将现有固定 token 逻辑迁入 `FixedTokenChunkingStrategy`。
- `DocumentProcessingService` 注入 `LightRagChunkingService`。
- 索引流程切换到 `ChunkDocumentAsync`。

### Options

- 扩展 `LightRAGOptions`，新增 `Chunking` 配置。
- 保留 `ChunkTokenSize` / `ChunkOverlapTokenSize` 作为兼容 fallback。
- 如果新旧配置同时存在，新策略专用配置优先。

### Server

- 文档 intake / Add to RAG 流程冻结 chunking 配置快照。
- 文档状态 metadata 记录 `chunking_strategy` 和关键参数。
- 第一阶段不新增 UI 和批量重索引 API。

### Tests

- 核心策略测试放在 `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/`。
- 插入流程测试覆盖默认 F 兼容、R/V/P dispatch 和配置快照。
- 所有 V 测试使用 fake embedding，不能调用真实 provider。

## 测试矩阵

### F

- 现有 `ChunkDocument` 行为保持。
- trim 后 token 计算保持。
- sliding window + overlap 保持。
- 尾部小片段合并保持。
- `splitByCharacter` 和 `splitByCharacterOnly` 保持。

### R

- 空文本返回空。
- 短文本返回单 chunk。
- 段落分隔符优先。
- 自定义 separators 生效。
- 超长段落递归到下一层分隔符。
- 超长句子继续降级到逗号、空格、字符或 token 硬切。
- overlap 被 clamp 后不死循环。
- split 后小块被 merge。
- source span 对重复文本不回跳。

### V

- fake embedding 可稳定分块。
- 无 embedding 服务时 fallback R 并 warning。
- embedding 失败时文档处理失败。
- 单句输入返回单 chunk。
- 两句输入稳定处理。
- 没有明显断点时返回大语义块，再由 R 处理超限。
- 断点过多时小块合并。
- `NumberOfChunks` 优先于阈值。
- 超长语义块 fallback R，overlap 为 0。
- CJK 句子可被 regex 分割。

### P

- Markdown heading 构建 block。
- 缺少 block 时 fallback R。
- 同一 heading 下短段落合并。
- 不跨顶级 heading 合并。
- 超长单段落 fallback R。
- 第一个短段落不能导致空切片。
- anchor split 后 heading 增加 `[part n]`。
- Markdown table 按行切。
- 单行超长 table fallback R。
- code fence 优先保持，超长再按行或 R 切。
- separator 重新 tokenize 后仍不超限。

### 配置与快照

- 默认策略为 `F`。
- 策略专用配置优先于全局 fallback。
- P 默认 token size 为 2000。
- 文档入队后配置快照不受后续全局配置变化影响。
- 未知策略报错。
- 多策略组合报错。

## 验证要求

设计实现后最低验证：

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~DocumentProcessing"
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~Document"
dotnet test .\LightRAGNet.slnx
```

如果实现触及 React 或 Server API，再补充：

```powershell
Push-Location src\LightRAGNet.React; npm test; npm run build; Pop-Location
```

## 验收标准

- `F/R/V/P` 四种策略可以通过配置选择。
- 默认 `F` 行为兼容现有测试。
- `R` 实现递归分隔符拆分、merge buffer 和硬兜底。
- `V` 实现 embedding distance breakpoint、小块合并和超长 fallback R。
- `P` 实现 Markdown block 版 heading/paragraph/table/code-aware 分块。
- 分块配置快照被记录并用于重试。
- 所有策略不产生空 chunk，不死循环，不输出超限 chunk。
- 关键边界测试通过。
- 默认测试不依赖真实 Qdrant、Neo4j 或外部 embedding provider。

## 后续工作

完成核心策略后，后续可分阶段做：

1. React 上传或文档详情页增加策略选择。
2. 增加已有文档显式重索引入口。
3. 将 P 策略从 Markdown block 扩展到更完整的 structured sidecar。
4. 在文档预览中基于 source span 高亮 chunk 来源。
5. 将 chunking strategy 纳入 RAGAS / retrieval evaluation 对比。

## Spec Self-Review

- Placeholder scan：没有保留未完成标记或空白章节。
- Scope check：本轮聚焦核心分块策略，不包含 UI 和批量重索引。
- Ambiguity check：`V` 被明确为 semantic breakpoint，不是 vector search；`P` 首期明确是 Markdown block 版，而不是完整 Python sidecar 复刻。
- Compatibility check：默认 `F` 行为保持，策略切换只影响新索引文档。
- Safety check：真实 embedding provider 不进入默认测试，embedding 失败不静默降级。
