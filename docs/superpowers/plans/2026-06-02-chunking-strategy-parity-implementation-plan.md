# Chunking Strategy Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement Python LightRAG-style `F/R/V/P` chunking strategies in LightRAGNet while keeping the current fixed-token default compatible.

**Architecture:** Add a focused chunking subsystem under `Services/DocumentProcessing/Chunking`, keep `DocumentProcessingService` as the document-processing facade, and route indexing through an async `ChunkDocumentAsync` entrypoint. `F` preserves current behavior, `R` provides recursive structural splitting, `V` uses embedding-distance semantic breakpoints, and `P` uses a Markdown block model with R fallback.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, NSubstitute, existing `ITokenizer` and `IEmbeddingService`.

---

## File Structure

- Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/LightRagChunkingStrategy.cs`
  - Strategy enum and stable wire values.
- Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/LightRagChunkingOptions.cs`
  - Strategy-specific options and validation helpers.
- Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/ChunkingSegment.cs`
  - Internal rich chunking output with source span and heading.
- Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/IChunkingStrategy.cs`
  - Shared async strategy interface.
- Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/ChunkingUtilities.cs`
  - Token-size validation, overlap clamping, cosine distance, percentile helpers, span helpers.
- Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/FixedTokenChunkingStrategy.cs`
  - Extracts current `ChunkDocument` fixed-token behavior.
- Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/RecursiveCharacterChunkingStrategy.cs`
  - Implements recursive separator cascade and merge buffer.
- Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/SemanticVectorChunkingStrategy.cs`
  - Implements V semantic breakpoint chunking using `IEmbeddingService`.
- Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/MarkdownDocumentBlockBuilder.cs`
  - Builds heading/table/code-aware blocks from Markdown or converted text.
- Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/ParagraphSemanticChunkingStrategy.cs`
  - Implements P paragraph semantic chunking on Markdown blocks.
- Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/LightRagChunkingService.cs`
  - Resolves active strategy, maps `ChunkingSegment` to existing `Chunk`, and creates config metadata.
- Modify `src/LightRAGNet/LightRAGOptions.cs`
  - Adds `Chunking` options while preserving legacy `ChunkTokenSize` and `ChunkOverlapTokenSize`.
- Modify `src/LightRAGNet/Services/DocumentProcessing/DocumentProcessingService.cs`
  - Injects `LightRagChunkingService`, keeps sync compatibility method, adds async chunking facade.
- Modify `src/LightRAGNet/LightRAG.cs`
  - Uses `ChunkDocumentAsync`, records chunking metadata in full docs and lifecycle metadata.
- Modify `src/LightRAGNet/Services/DocumentLifecycle/DocumentLifecycleService.cs`
  - Adds optional chunking metadata recording.
- Modify `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
  - Registers chunking service and strategies.
- Create tests under `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/`
  - Focused tests for options, F, R, V, P, source spans, and metadata snapshots.
- Modify `tests/LightRAGNet.Tests/DocumentProcessing/DocumentProcessingServiceTests.cs`
  - Updates helper construction and adds async facade coverage.
- Modify integration tests that construct `DocumentProcessingService`
  - Use a local helper factory or pass `LightRagChunkingService`.

## Task 1: Add Chunking Contracts and Options

**Files:**
- Create: `src/LightRAGNet/Services/DocumentProcessing/Chunking/LightRagChunkingStrategy.cs`
- Create: `src/LightRAGNet/Services/DocumentProcessing/Chunking/LightRagChunkingOptions.cs`
- Create: `src/LightRAGNet/Services/DocumentProcessing/Chunking/ChunkingSegment.cs`
- Create: `src/LightRAGNet/Services/DocumentProcessing/Chunking/IChunkingStrategy.cs`
- Create: `src/LightRAGNet/Services/DocumentProcessing/Chunking/ChunkingUtilities.cs`
- Modify: `src/LightRAGNet/LightRAGOptions.cs`
- Test: `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/LightRagChunkingOptionsTests.cs`

- [ ] **Step 1: Write failing option tests**

Create `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/LightRagChunkingOptionsTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing.Chunking;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class LightRagChunkingOptionsTests
{
    [Fact]
    public void Normalize_WhenUnset_DefaultsToFixedToken()
    {
        var options = new LightRAGOptions();

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.Strategy.Should().Be(LightRagChunkingStrategy.FixedToken);
        snapshot.ChunkTokenSize.Should().Be(1200);
        snapshot.FixedToken.ChunkOverlapTokenSize.Should().Be(100);
    }

    [Fact]
    public void Normalize_ParagraphSemantic_DefaultsToTwoThousandTokens()
    {
        var options = new LightRAGOptions
        {
            ChunkTokenSize = 1200,
            Chunking = new LightRagChunkingOptions
            {
                Strategy = LightRagChunkingStrategy.ParagraphSemantic
            }
        };

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.Strategy.Should().Be(LightRagChunkingStrategy.ParagraphSemantic);
        snapshot.ParagraphSemantic.ChunkTokenSize.Should().Be(2000);
    }

    [Fact]
    public void Normalize_WhenOverlapExceedsChunkSize_ClampsOverlap()
    {
        var options = new LightRAGOptions
        {
            ChunkTokenSize = 5,
            ChunkOverlapTokenSize = 12
        };

        var snapshot = options.CreateChunkingSnapshot();

        snapshot.FixedToken.ChunkOverlapTokenSize.Should().Be(4);
    }

    [Fact]
    public void Normalize_WhenChunkSizeIsZero_Throws()
    {
        var options = new LightRAGOptions { ChunkTokenSize = 0 };

        var act = () => options.CreateChunkingSnapshot();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ChunkTokenSize*greater than zero*");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~LightRagChunkingOptionsTests" --no-restore --verbosity minimal
```

Expected: build fails because `LightRagChunkingStrategy`, `LightRagChunkingOptions`, and `CreateChunkingSnapshot` do not exist.

- [ ] **Step 3: Add enum, segment, interface, and utility types**

Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/LightRagChunkingStrategy.cs`:

```csharp
namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public enum LightRagChunkingStrategy
{
    FixedToken,
    RecursiveCharacter,
    SemanticVector,
    ParagraphSemantic
}

public static class LightRagChunkingStrategyExtensions
{
    public static string ToWireValue(this LightRagChunkingStrategy strategy)
    {
        return strategy switch
        {
            LightRagChunkingStrategy.FixedToken => "F",
            LightRagChunkingStrategy.RecursiveCharacter => "R",
            LightRagChunkingStrategy.SemanticVector => "V",
            LightRagChunkingStrategy.ParagraphSemantic => "P",
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unsupported chunking strategy.")
        };
    }
}
```

Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/ChunkingSegment.cs`:

```csharp
namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class ChunkingSegment
{
    public string Content { get; init; } = string.Empty;
    public int Tokens { get; init; }
    public int Order { get; init; }
    public LightRagChunkingStrategy Strategy { get; init; }
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

Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/IChunkingStrategy.cs`:

```csharp
using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed record ChunkingRequest(
    string Content,
    string DocId,
    string FilePath,
    LightRagChunkingSnapshot Options);

public interface IChunkingStrategy
{
    LightRagChunkingStrategy Strategy { get; }

    Task<IReadOnlyList<ChunkingSegment>> ChunkAsync(
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken);
}
```

Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/ChunkingUtilities.cs`:

```csharp
using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

internal static class ChunkingUtilities
{
    public static int RequirePositiveChunkSize(int value, string name)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"{name} must be greater than zero.");
        }

        return value;
    }

    public static int ClampOverlap(int chunkSize, int overlap)
    {
        if (chunkSize <= 1)
        {
            return 0;
        }

        return Math.Clamp(overlap, 0, chunkSize - 1);
    }

    public static int CountTokens(ITokenizer tokenizer, string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : tokenizer.Encode(text).Count;
    }

    public static SourceSpan? TrimmedSpan(string content, int start, int end)
    {
        start = Math.Max(0, Math.Min(start, content.Length));
        end = Math.Max(start, Math.Min(end, content.Length));
        while (start < end && char.IsWhiteSpace(content[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(content[end - 1]))
        {
            end--;
        }

        return start >= end ? null : new SourceSpan(start, end);
    }

    public static double CosineDistance(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            throw new InvalidOperationException("Embedding vectors must have the same non-zero dimension.");
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm == 0 || rightNorm == 0)
        {
            return 1.0;
        }

        return 1.0 - dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        var position = (sorted.Length - 1) * percentile / 100.0;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        var weight = position - lower;
        return sorted[lower] * (1 - weight) + sorted[upper] * weight;
    }
}
```

- [ ] **Step 4: Add options and snapshot model**

Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/LightRagChunkingOptions.cs`:

```csharp
namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class LightRagChunkingOptions
{
    public LightRagChunkingStrategy Strategy { get; set; } = LightRagChunkingStrategy.FixedToken;
    public FixedTokenChunkingOptions FixedToken { get; set; } = new();
    public RecursiveCharacterChunkingOptions RecursiveCharacter { get; set; } = new();
    public SemanticVectorChunkingOptions SemanticVector { get; set; } = new();
    public ParagraphSemanticChunkingOptions ParagraphSemantic { get; set; } = new();
}

public sealed class FixedTokenChunkingOptions
{
    public int? ChunkTokenSize { get; set; }
    public int? ChunkOverlapTokenSize { get; set; }
    public string? SplitByCharacter { get; set; }
    public bool SplitByCharacterOnly { get; set; }
}

public sealed class RecursiveCharacterChunkingOptions
{
    public int? ChunkTokenSize { get; set; }
    public int? ChunkOverlapTokenSize { get; set; }
    public List<string> Separators { get; set; } =
    [
        "\n\n", "\n", "。", "！", "？", "；", "，", " ", ""
    ];
}

public enum SemanticVectorBreakpointThresholdType
{
    Percentile,
    StandardDeviation,
    Interquartile,
    Gradient
}

public sealed class SemanticVectorChunkingOptions
{
    public int? ChunkTokenSize { get; set; }
    public SemanticVectorBreakpointThresholdType BreakpointThresholdType { get; set; } =
        SemanticVectorBreakpointThresholdType.Percentile;
    public double? BreakpointThresholdAmount { get; set; }
    public int BufferSize { get; set; } = 1;
    public int? NumberOfChunks { get; set; }
    public int? MinChunkSize { get; set; }
    public int MinChunkTokenSize { get; set; } = 0;
    public string SentenceSplitRegex { get; set; } = @"(?<=[。？！.!?])\s+";
    public bool FallBackToRecursiveWhenEmbeddingUnavailable { get; set; } = true;
}

public sealed class ParagraphSemanticChunkingOptions
{
    public int? ChunkTokenSize { get; set; }
    public int? ChunkOverlapTokenSize { get; set; }
    public int MinChunkTokenSize { get; set; } = 0;
}

public sealed record LightRagChunkingSnapshot(
    LightRagChunkingStrategy Strategy,
    int ChunkTokenSize,
    FixedTokenChunkingSnapshot FixedToken,
    RecursiveCharacterChunkingSnapshot RecursiveCharacter,
    SemanticVectorChunkingSnapshot SemanticVector,
    ParagraphSemanticChunkingSnapshot ParagraphSemantic);

public sealed record FixedTokenChunkingSnapshot(
    int ChunkTokenSize,
    int ChunkOverlapTokenSize,
    string? SplitByCharacter,
    bool SplitByCharacterOnly);

public sealed record RecursiveCharacterChunkingSnapshot(
    int ChunkTokenSize,
    int ChunkOverlapTokenSize,
    IReadOnlyList<string> Separators);

public sealed record SemanticVectorChunkingSnapshot(
    int ChunkTokenSize,
    SemanticVectorBreakpointThresholdType BreakpointThresholdType,
    double? BreakpointThresholdAmount,
    int BufferSize,
    int? NumberOfChunks,
    int? MinChunkSize,
    int MinChunkTokenSize,
    string SentenceSplitRegex,
    bool FallBackToRecursiveWhenEmbeddingUnavailable);

public sealed record ParagraphSemanticChunkingSnapshot(
    int ChunkTokenSize,
    int ChunkOverlapTokenSize,
    int MinChunkTokenSize);
```

Modify `src/LightRAGNet/LightRAGOptions.cs`:

```csharp
using LightRAGNet.Services.DocumentProcessing.Chunking;

namespace LightRAGNet;

public partial class LightRAGOptions
{
    public LightRagChunkingOptions Chunking { get; set; } = new();

    public LightRagChunkingSnapshot CreateChunkingSnapshot()
    {
        var globalSize = ChunkingUtilities.RequirePositiveChunkSize(ChunkTokenSize, nameof(ChunkTokenSize));
        var globalOverlap = ChunkingUtilities.ClampOverlap(globalSize, ChunkOverlapTokenSize);

        var fixedSize = Chunking.FixedToken.ChunkTokenSize ?? globalSize;
        fixedSize = ChunkingUtilities.RequirePositiveChunkSize(fixedSize, "Chunking:FixedToken:ChunkTokenSize");
        var fixedOverlap = ChunkingUtilities.ClampOverlap(
            fixedSize,
            Chunking.FixedToken.ChunkOverlapTokenSize ?? globalOverlap);

        var recursiveSize = Chunking.RecursiveCharacter.ChunkTokenSize ?? globalSize;
        recursiveSize = ChunkingUtilities.RequirePositiveChunkSize(
            recursiveSize,
            "Chunking:RecursiveCharacter:ChunkTokenSize");
        var recursiveOverlap = ChunkingUtilities.ClampOverlap(
            recursiveSize,
            Chunking.RecursiveCharacter.ChunkOverlapTokenSize ?? globalOverlap);

        var vectorSize = Chunking.SemanticVector.ChunkTokenSize ?? globalSize;
        vectorSize = ChunkingUtilities.RequirePositiveChunkSize(
            vectorSize,
            "Chunking:SemanticVector:ChunkTokenSize");

        var paragraphSize = Chunking.ParagraphSemantic.ChunkTokenSize ?? 2000;
        paragraphSize = ChunkingUtilities.RequirePositiveChunkSize(
            paragraphSize,
            "Chunking:ParagraphSemantic:ChunkTokenSize");
        var paragraphOverlap = ChunkingUtilities.ClampOverlap(
            paragraphSize,
            Chunking.ParagraphSemantic.ChunkOverlapTokenSize ?? globalOverlap);

        return new LightRagChunkingSnapshot(
            Chunking.Strategy,
            globalSize,
            new FixedTokenChunkingSnapshot(
                fixedSize,
                fixedOverlap,
                Chunking.FixedToken.SplitByCharacter,
                Chunking.FixedToken.SplitByCharacterOnly),
            new RecursiveCharacterChunkingSnapshot(
                recursiveSize,
                recursiveOverlap,
                [.. Chunking.RecursiveCharacter.Separators]),
            new SemanticVectorChunkingSnapshot(
                vectorSize,
                Chunking.SemanticVector.BreakpointThresholdType,
                Chunking.SemanticVector.BreakpointThresholdAmount,
                Math.Max(1, Chunking.SemanticVector.BufferSize),
                Chunking.SemanticVector.NumberOfChunks,
                Chunking.SemanticVector.MinChunkSize,
                Math.Max(0, Chunking.SemanticVector.MinChunkTokenSize),
                Chunking.SemanticVector.SentenceSplitRegex,
                Chunking.SemanticVector.FallBackToRecursiveWhenEmbeddingUnavailable),
            new ParagraphSemanticChunkingSnapshot(
                paragraphSize,
                paragraphOverlap,
                Math.Max(0, Chunking.ParagraphSemantic.MinChunkTokenSize)));
    }
}
```

Also change the existing declaration to partial:

```csharp
public partial class LightRAGOptions
```

- [ ] **Step 5: Run option tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~LightRagChunkingOptionsTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 6: Commit contracts**

```powershell
git add src\LightRAGNet\LightRAGOptions.cs src\LightRAGNet\Services\DocumentProcessing\Chunking tests\LightRAGNet.Tests\DocumentProcessing\Chunking\LightRagChunkingOptionsTests.cs
git commit -m "feat: add chunking strategy contracts"
```

## Task 2: Extract Fixed Token Strategy and Preserve Compatibility

**Files:**
- Create: `src/LightRAGNet/Services/DocumentProcessing/Chunking/FixedTokenChunkingStrategy.cs`
- Create: `src/LightRAGNet/Services/DocumentProcessing/Chunking/LightRagChunkingService.cs`
- Modify: `src/LightRAGNet/Services/DocumentProcessing/DocumentProcessingService.cs`
- Modify: `tests/LightRAGNet.Tests/DocumentProcessing/DocumentProcessingServiceTests.cs`
- Test: `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/FixedTokenChunkingStrategyTests.cs`

- [ ] **Step 1: Write fixed-token strategy tests**

Create `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/FixedTokenChunkingStrategyTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class FixedTokenChunkingStrategyTests
{
    [Fact]
    public async Task ChunkAsync_UsesSlidingTokenWindowWithOverlap()
    {
        var strategy = new FixedTokenChunkingStrategy();
        var request = CreateRequest("one two three four five six seven eight", chunkSize: 4, overlap: 1);

        var chunks = await strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        chunks.Select(chunk => chunk.Content).Should().Equal(
            "t1 t2 t3 t4",
            "t4 t5 t6 t7",
            "t7 t8");
        chunks.Select(chunk => chunk.Tokens).Should().Equal(4, 4, 2);
        chunks.Select(chunk => chunk.Order).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task ChunkAsync_WhenSplitByCharacterOnlyAndSegmentExceedsLimit_Throws()
    {
        var strategy = new FixedTokenChunkingStrategy();
        var request = CreateRequest(
            "alpha beta gamma|delta",
            chunkSize: 2,
            overlap: 1,
            splitByCharacter: "|",
            splitByCharacterOnly: true);

        var act = () => strategy.ChunkAsync(request, new FakeTokenizer(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static ChunkingRequest CreateRequest(
        string content,
        int chunkSize,
        int overlap,
        string? splitByCharacter = null,
        bool splitByCharacterOnly = false)
    {
        var snapshot = new LightRagChunkingSnapshot(
            LightRagChunkingStrategy.FixedToken,
            chunkSize,
            new FixedTokenChunkingSnapshot(chunkSize, overlap, splitByCharacter, splitByCharacterOnly),
            new RecursiveCharacterChunkingSnapshot(chunkSize, overlap, ["\n\n", "\n", " ", ""]),
            new SemanticVectorChunkingSnapshot(
                chunkSize,
                SemanticVectorBreakpointThresholdType.Percentile,
                null,
                1,
                null,
                null,
                0,
                @"(?<=[。？！.!?])\s+",
                true),
            new ParagraphSemanticChunkingSnapshot(2000, overlap, 0));

        return new ChunkingRequest(content, "doc-1", "file.md", snapshot);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~FixedTokenChunkingStrategyTests" --no-restore --verbosity minimal
```

Expected: build fails because `FixedTokenChunkingStrategy` does not exist.

- [ ] **Step 3: Implement fixed-token strategy**

Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/FixedTokenChunkingStrategy.cs`:

```csharp
using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class FixedTokenChunkingStrategy : IChunkingStrategy
{
    public LightRagChunkingStrategy Strategy => LightRagChunkingStrategy.FixedToken;

    public Task<IReadOnlyList<ChunkingSegment>> ChunkAsync(
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var options = request.Options.FixedToken;
        var content = request.Content.Trim();
        var chunks = string.IsNullOrEmpty(options.SplitByCharacter)
            ? ChunkByWindow(content, tokenizer, options)
            : ChunkBySeparator(content, tokenizer, options);

        return Task.FromResult<IReadOnlyList<ChunkingSegment>>(chunks);
    }

    private static List<ChunkingSegment> ChunkBySeparator(
        string content,
        ITokenizer tokenizer,
        FixedTokenChunkingSnapshot options)
    {
        var rawChunks = content.Split(options.SplitByCharacter!, StringSplitOptions.None);
        var newChunks = new List<(int Tokens, string Content)>();

        foreach (var rawChunk in rawChunks)
        {
            var chunkTokens = tokenizer.Encode(rawChunk);
            if (chunkTokens.Count <= options.ChunkTokenSize)
            {
                newChunks.Add((chunkTokens.Count, rawChunk));
                continue;
            }

            if (options.SplitByCharacterOnly)
            {
                throw new InvalidOperationException(
                    $"Chunk exceeds token limit: {chunkTokens.Count} > {options.ChunkTokenSize}");
            }

            var step = Math.Max(1, options.ChunkTokenSize - options.ChunkOverlapTokenSize);
            for (var start = 0; start < chunkTokens.Count; start += step)
            {
                var end = Math.Min(start + options.ChunkTokenSize, chunkTokens.Count);
                var subTokens = chunkTokens.Skip(start).Take(end - start).ToList();
                newChunks.Add((subTokens.Count, tokenizer.Decode(subTokens)));
            }
        }

        return newChunks
            .Select((chunk, index) => CreateSegment(chunk.Content, chunk.Tokens, index))
            .ToList();
    }

    private static List<ChunkingSegment> ChunkByWindow(
        string content,
        ITokenizer tokenizer,
        FixedTokenChunkingSnapshot options)
    {
        var tokens = tokenizer.Encode(content);
        var chunks = new List<ChunkingSegment>();
        var step = Math.Max(1, options.ChunkTokenSize - options.ChunkOverlapTokenSize);

        for (var start = 0; start < tokens.Count; start += step)
        {
            var end = Math.Min(start + options.ChunkTokenSize, tokens.Count);
            var remainingTokens = tokens.Count - start;
            if (remainingTokens <= options.ChunkOverlapTokenSize && chunks.Count > 0)
            {
                var previous = chunks[^1];
                var previousTokens = tokenizer.Encode(previous.Content);
                var remaining = tokens.Skip(start).Take(remainingTokens).ToList();
                var mergedTokens = previousTokens.Concat(remaining).ToList();
                chunks[^1] = CreateSegment(
                    tokenizer.Decode(mergedTokens),
                    mergedTokens.Count,
                    previous.Order);
                break;
            }

            var chunkTokens = tokens.Skip(start).Take(end - start).ToList();
            if (chunkTokens.Count == 0)
            {
                break;
            }

            chunks.Add(CreateSegment(
                tokenizer.Decode(chunkTokens),
                chunkTokens.Count,
                chunks.Count));
        }

        return chunks;
    }

    private static ChunkingSegment CreateSegment(string content, int tokens, int order)
    {
        return new ChunkingSegment
        {
            Content = content.Trim(),
            Tokens = tokens,
            Order = order,
            Strategy = LightRagChunkingStrategy.FixedToken
        };
    }
}
```

- [ ] **Step 4: Implement chunking service and DocumentProcessingService facade**

Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/LightRagChunkingService.cs`:

```csharp
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Utils;
using LightRAGNet.Services.DocumentProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class LightRagChunkingService(
    IEnumerable<IChunkingStrategy> strategies,
    ITokenizer tokenizer,
    IOptions<LightRAGOptions> options,
    ILogger<LightRagChunkingService> logger)
{
    private readonly Dictionary<LightRagChunkingStrategy, IChunkingStrategy> _strategies =
        strategies.ToDictionary(strategy => strategy.Strategy);

    public async Task<IReadOnlyList<Chunk>> ChunkDocumentAsync(
        string content,
        string docId,
        string filePath,
        LightRagChunkingSnapshot? snapshot = null,
        CancellationToken cancellationToken = default)
    {
        snapshot ??= options.Value.CreateChunkingSnapshot();
        if (!_strategies.TryGetValue(snapshot.Strategy, out var strategy))
        {
            throw new InvalidOperationException($"Chunking strategy '{snapshot.Strategy}' is not registered.");
        }

        var request = new ChunkingRequest(content, docId, filePath, snapshot);
        var segments = await strategy.ChunkAsync(request, tokenizer, cancellationToken);
        logger.LogDebug(
            "Chunked document {DocId} with strategy {Strategy}: {ChunkCount} chunks",
            docId,
            snapshot.Strategy,
            segments.Count);

        return segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment.Content))
            .Select((segment, index) => new Chunk
            {
                Id = HashUtils.ComputeMd5Hash(segment.Content, "chunk-"),
                Content = segment.Content.Trim(),
                Tokens = segment.Tokens,
                ChunkOrderIndex = index,
                FullDocId = docId,
                FilePath = filePath
            })
            .ToList();
    }
}
```

Modify `DocumentProcessingService` constructor to accept `LightRagChunkingService? chunkingService = null`, store it, and update chunking methods:

```csharp
using LightRAGNet.Services.DocumentProcessing.Chunking;

private readonly LightRagChunkingService? _chunkingService = chunkingService;

public async Task<IReadOnlyList<Chunk>> ChunkDocumentAsync(
    string content,
    string docId,
    string filePath = "",
    LightRagChunkingSnapshot? snapshot = null,
    CancellationToken cancellationToken = default)
{
    if (_chunkingService is null)
    {
        return ChunkDocument(content, docId, filePath);
    }

    return await _chunkingService.ChunkDocumentAsync(
        content,
        docId,
        filePath,
        snapshot,
        cancellationToken);
}
```

Keep existing `ChunkDocument(...)` body unchanged for this task.

- [ ] **Step 5: Update test helper construction**

In `DocumentProcessingServiceTests.CreateService`, create a chunking service:

```csharp
var chunkingOptions = Options.Create(lightRagOptions);
var chunkingService = new LightRagChunkingService(
    [new FixedTokenChunkingStrategy()],
    new FakeTokenizer(),
    chunkingOptions,
    NullLogger<LightRagChunkingService>.Instance);

return new DocumentProcessingService(
    llmService ?? Substitute.For<ILLMService>(),
    embeddingService ?? Substitute.For<IEmbeddingService>(),
    new FakeTokenizer(),
    new LightRagLlmCacheService(
        llmCacheStore,
        Options.Create(lightRagOptions),
        keyBuilder,
        NullLogger<LightRagLlmCacheService>.Instance),
    Options.Create(lightRagOptions),
    NullLogger<DocumentProcessingService>.Instance,
    chunkingService);
```

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~FixedTokenChunkingStrategyTests|FullyQualifiedName~DocumentProcessingServiceTests.ChunkDocument" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 7: Commit fixed token extraction**

```powershell
git add src\LightRAGNet\Services\DocumentProcessing tests\LightRAGNet.Tests\DocumentProcessing
git commit -m "refactor: extract fixed token chunking strategy"
```

## Task 3: Implement Recursive Character Strategy

**Files:**
- Create: `src/LightRAGNet/Services/DocumentProcessing/Chunking/RecursiveCharacterChunkingStrategy.cs`
- Modify: `src/LightRAGNet/Services/DocumentProcessing/Chunking/LightRagChunkingService.cs`
- Test: `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/RecursiveCharacterChunkingStrategyTests.cs`

- [ ] **Step 1: Write recursive strategy tests**

Create `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/RecursiveCharacterChunkingStrategyTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class RecursiveCharacterChunkingStrategyTests
{
    [Fact]
    public async Task ChunkAsync_EmptyInput_ReturnsEmptyList()
    {
        var chunks = await CreateStrategy().ChunkAsync(
            CreateRequest(""),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task ChunkAsync_UsesParagraphSeparatorBeforeWeakerSeparators()
    {
        var body = "alpha beta\n\ngamma delta\n\neta theta";

        var chunks = await CreateStrategy().ChunkAsync(
            CreateRequest(body, chunkSize: 3, overlap: 0),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().HaveCount(3);
        chunks.Select(chunk => chunk.Content).Should().Equal(
            "alpha beta",
            "gamma delta",
            "eta theta");
    }

    [Fact]
    public async Task ChunkAsync_LongSentenceFallsThroughToSpaceAndTokenWindows()
    {
        var body = "alpha beta gamma delta epsilon zeta eta theta";

        var chunks = await CreateStrategy().ChunkAsync(
            CreateRequest(body, chunkSize: 3, overlap: 1),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.Tokens <= 3);
    }

    [Fact]
    public async Task ChunkAsync_MergesSmallPieces()
    {
        var body = "alpha\nbeta\ngamma";

        var chunks = await CreateStrategy().ChunkAsync(
            CreateRequest(body, chunkSize: 10, overlap: 0),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Contain("alpha");
        chunks[0].Content.Should().Contain("gamma");
    }

    [Fact]
    public async Task ChunkAsync_RepeatedTextSourceSpansMoveForward()
    {
        var body = "alpha beta\n\nalpha beta\n\nalpha beta";

        var chunks = await CreateStrategy().ChunkAsync(
            CreateRequest(body, chunkSize: 3, overlap: 0),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Select(chunk => chunk.SourceSpan!.Start)
            .Should().BeInAscendingOrder();
        foreach (var chunk in chunks)
        {
            var span = chunk.SourceSpan!;
            body[span.Start..span.End].Should().Be(chunk.Content);
        }
    }

    private static RecursiveCharacterChunkingStrategy CreateStrategy()
    {
        return new RecursiveCharacterChunkingStrategy(new FixedTokenChunkingStrategy());
    }

    private static ChunkingRequest CreateRequest(string body, int chunkSize = 10, int overlap = 0)
    {
        var snapshot = new LightRagChunkingSnapshot(
            LightRagChunkingStrategy.RecursiveCharacter,
            chunkSize,
            new FixedTokenChunkingSnapshot(chunkSize, overlap, null, false),
            new RecursiveCharacterChunkingSnapshot(chunkSize, overlap, ["\n\n", "\n", " ", ""]),
            new SemanticVectorChunkingSnapshot(
                chunkSize,
                SemanticVectorBreakpointThresholdType.Percentile,
                null,
                1,
                null,
                null,
                0,
                @"(?<=[。？！.!?])\s+",
                true),
            new ParagraphSemanticChunkingSnapshot(2000, overlap, 0));

        return new ChunkingRequest(body, "doc-1", "file.md", snapshot);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RecursiveCharacterChunkingStrategyTests" --no-restore --verbosity minimal
```

Expected: build fails because `RecursiveCharacterChunkingStrategy` does not exist.

- [ ] **Step 3: Implement recursive strategy**

Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/RecursiveCharacterChunkingStrategy.cs`:

```csharp
using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class RecursiveCharacterChunkingStrategy(
    FixedTokenChunkingStrategy fixedTokenFallback) : IChunkingStrategy
{
    public LightRagChunkingStrategy Strategy => LightRagChunkingStrategy.RecursiveCharacter;

    public Task<IReadOnlyList<ChunkingSegment>> ChunkAsync(
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var content = request.Content.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult<IReadOnlyList<ChunkingSegment>>([]);
        }

        var options = request.Options.RecursiveCharacter;
        var pieces = SplitRecursive(
            content,
            sourceOffset: request.Content.IndexOf(content, StringComparison.Ordinal),
            options.Separators,
            separatorIndex: 0,
            tokenizer,
            options.ChunkTokenSize);
        var merged = MergePieces(
            pieces,
            tokenizer,
            options.ChunkTokenSize,
            options.ChunkOverlapTokenSize);

        return Task.FromResult<IReadOnlyList<ChunkingSegment>>(
            merged.Select((piece, index) => new ChunkingSegment
            {
                Content = piece.Content.Trim(),
                Tokens = ChunkingUtilities.CountTokens(tokenizer, piece.Content.Trim()),
                Order = index,
                Strategy = Strategy,
                SourceSpan = ChunkingUtilities.TrimmedSpan(request.Content, piece.Start, piece.End)
            }).ToList());
    }

    private IReadOnlyList<SpanPiece> SplitRecursive(
        string text,
        int sourceOffset,
        IReadOnlyList<string> separators,
        int separatorIndex,
        ITokenizer tokenizer,
        int chunkSize)
    {
        if (ChunkingUtilities.CountTokens(tokenizer, text) <= chunkSize)
        {
            return [new SpanPiece(text, sourceOffset, sourceOffset + text.Length)];
        }

        if (separatorIndex >= separators.Count)
        {
            return HardSplit(text, sourceOffset, tokenizer, chunkSize);
        }

        var separator = separators[separatorIndex];
        if (separator.Length == 0)
        {
            return HardSplit(text, sourceOffset, tokenizer, chunkSize);
        }

        var rawPieces = SplitKeepingSpans(text, sourceOffset, separator);
        if (rawPieces.Count <= 1)
        {
            return SplitRecursive(text, sourceOffset, separators, separatorIndex + 1, tokenizer, chunkSize);
        }

        var output = new List<SpanPiece>();
        foreach (var piece in rawPieces)
        {
            if (ChunkingUtilities.CountTokens(tokenizer, piece.Content) <= chunkSize)
            {
                output.Add(piece);
            }
            else
            {
                output.AddRange(SplitRecursive(
                    piece.Content,
                    piece.Start,
                    separators,
                    separatorIndex + 1,
                    tokenizer,
                    chunkSize));
            }
        }

        return output;
    }

    private static List<SpanPiece> SplitKeepingSpans(string text, int sourceOffset, string separator)
    {
        var pieces = new List<SpanPiece>();
        var cursor = 0;
        while (cursor <= text.Length)
        {
            var next = text.IndexOf(separator, cursor, StringComparison.Ordinal);
            if (next < 0)
            {
                if (cursor < text.Length)
                {
                    pieces.Add(new SpanPiece(text[cursor..], sourceOffset + cursor, sourceOffset + text.Length));
                }
                break;
            }

            if (next > cursor)
            {
                pieces.Add(new SpanPiece(text[cursor..next], sourceOffset + cursor, sourceOffset + next));
            }

            cursor = next + separator.Length;
        }

        return pieces;
    }

    private IReadOnlyList<SpanPiece> HardSplit(
        string text,
        int sourceOffset,
        ITokenizer tokenizer,
        int chunkSize)
    {
        var snapshot = new LightRagChunkingSnapshot(
            LightRagChunkingStrategy.FixedToken,
            chunkSize,
            new FixedTokenChunkingSnapshot(chunkSize, 0, null, false),
            new RecursiveCharacterChunkingSnapshot(chunkSize, 0, []),
            new SemanticVectorChunkingSnapshot(
                chunkSize,
                SemanticVectorBreakpointThresholdType.Percentile,
                null,
                1,
                null,
                null,
                0,
                @"(?<=[。？！.!?])\s+",
                true),
            new ParagraphSemanticChunkingSnapshot(2000, 0, 0));
        var request = new ChunkingRequest(text, "fallback", string.Empty, snapshot);
        var chunks = fixedTokenFallback.ChunkAsync(request, tokenizer, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var output = new List<SpanPiece>();
        var cursor = 0;
        foreach (var chunk in chunks)
        {
            var index = text.IndexOf(chunk.Content, cursor, StringComparison.Ordinal);
            if (index < 0)
            {
                index = cursor;
            }

            output.Add(new SpanPiece(
                chunk.Content,
                sourceOffset + index,
                sourceOffset + index + chunk.Content.Length));
            cursor = index + chunk.Content.Length;
        }

        return output;
    }

    private static List<SpanPiece> MergePieces(
        IReadOnlyList<SpanPiece> pieces,
        ITokenizer tokenizer,
        int chunkSize,
        int overlap)
    {
        var results = new List<SpanPiece>();
        var current = new List<SpanPiece>();

        foreach (var piece in pieces)
        {
            var candidate = Join(current, piece);
            if (current.Count > 0 && ChunkingUtilities.CountTokens(tokenizer, candidate.Content) > chunkSize)
            {
                results.Add(Join(current));
                current = KeepOverlapTail(current, tokenizer, overlap);
            }

            current.Add(piece);
        }

        if (current.Count > 0)
        {
            results.Add(Join(current));
        }

        return results;
    }

    private static SpanPiece Join(IReadOnlyList<SpanPiece> pieces, SpanPiece? next = null)
    {
        var all = next is null ? pieces : [.. pieces, next.Value];
        var content = string.Join("\n\n", all.Select(piece => piece.Content.Trim()).Where(value => value.Length > 0));
        return new SpanPiece(content, all[0].Start, all[^1].End);
    }

    private static List<SpanPiece> KeepOverlapTail(IReadOnlyList<SpanPiece> pieces, ITokenizer tokenizer, int overlap)
    {
        if (overlap <= 0)
        {
            return [];
        }

        var kept = new List<SpanPiece>();
        var total = 0;
        for (var i = pieces.Count - 1; i >= 0; i--)
        {
            var tokens = ChunkingUtilities.CountTokens(tokenizer, pieces[i].Content);
            if (kept.Count > 0 && total + tokens > overlap)
            {
                break;
            }

            kept.Insert(0, pieces[i]);
            total += tokens;
        }

        return kept;
    }

    private readonly record struct SpanPiece(string Content, int Start, int End);
}
```

- [ ] **Step 4: Run recursive tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RecursiveCharacterChunkingStrategyTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Commit recursive strategy**

```powershell
git add src\LightRAGNet\Services\DocumentProcessing\Chunking\RecursiveCharacterChunkingStrategy.cs tests\LightRAGNet.Tests\DocumentProcessing\Chunking\RecursiveCharacterChunkingStrategyTests.cs
git commit -m "feat: add recursive character chunking"
```

## Task 4: Implement Semantic Vector Strategy

**Files:**
- Create: `src/LightRAGNet/Services/DocumentProcessing/Chunking/SemanticVectorChunkingStrategy.cs`
- Test: `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/SemanticVectorChunkingStrategyTests.cs`

- [ ] **Step 1: Write semantic vector tests**

Create `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/SemanticVectorChunkingStrategyTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using LightRAGNet.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class SemanticVectorChunkingStrategyTests
{
    [Fact]
    public async Task ChunkAsync_WhenEmbeddingUnavailable_FallsBackToRecursive()
    {
        var recursive = new RecursiveCharacterChunkingStrategy(new FixedTokenChunkingStrategy());
        var strategy = new SemanticVectorChunkingStrategy(null, recursive, NullLogger<SemanticVectorChunkingStrategy>.Instance);

        var chunks = await strategy.ChunkAsync(
            CreateRequest("alpha beta gamma delta", chunkSize: 2),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().NotBeEmpty();
        chunks.Should().OnlyContain(chunk => chunk.Strategy == LightRagChunkingStrategy.RecursiveCharacter);
    }

    [Fact]
    public async Task ChunkAsync_WhenSemanticGroupExceedsLimit_ResplitsWithRecursive()
    {
        var embedding = Substitute.For<IEmbeddingService>();
        embedding.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var texts = call.ArgAt<IEnumerable<string>>(0).ToList();
                return texts.Select((_, index) => new[] { (float)index, 1f }).ToArray();
            });
        var recursive = new RecursiveCharacterChunkingStrategy(new FixedTokenChunkingStrategy());
        var strategy = new SemanticVectorChunkingStrategy(embedding, recursive, NullLogger<SemanticVectorChunkingStrategy>.Instance);

        var chunks = await strategy.ChunkAsync(
            CreateRequest("alpha beta gamma delta epsilon zeta.", chunkSize: 2),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.Tokens <= 2);
    }

    [Fact]
    public async Task ChunkAsync_NumberOfChunksControlsBreakpointCount()
    {
        var embedding = Substitute.For<IEmbeddingService>();
        embedding.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                [1f, 0f],
                [0.9f, 0.1f],
                [0f, 1f],
                [0.1f, 0.9f]
            ]);
        var recursive = new RecursiveCharacterChunkingStrategy(new FixedTokenChunkingStrategy());
        var strategy = new SemanticVectorChunkingStrategy(embedding, recursive, NullLogger<SemanticVectorChunkingStrategy>.Instance);

        var chunks = await strategy.ChunkAsync(
            CreateRequest("A one. A two. B one. B two.", chunkSize: 100, numberOfChunks: 2),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().HaveCount(2);
    }

    private static ChunkingRequest CreateRequest(string content, int chunkSize, int? numberOfChunks = null)
    {
        var snapshot = new LightRagChunkingSnapshot(
            LightRagChunkingStrategy.SemanticVector,
            chunkSize,
            new FixedTokenChunkingSnapshot(chunkSize, 0, null, false),
            new RecursiveCharacterChunkingSnapshot(chunkSize, 0, ["\n\n", "\n", " ", ""]),
            new SemanticVectorChunkingSnapshot(
                chunkSize,
                SemanticVectorBreakpointThresholdType.Percentile,
                80,
                1,
                numberOfChunks,
                null,
                0,
                @"(?<=[。？！.!?])\s+",
                true),
            new ParagraphSemanticChunkingSnapshot(2000, 0, 0));

        return new ChunkingRequest(content, "doc-v", "vector.md", snapshot);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~SemanticVectorChunkingStrategyTests" --no-restore --verbosity minimal
```

Expected: build fails because `SemanticVectorChunkingStrategy` does not exist.

- [ ] **Step 3: Implement semantic vector strategy**

Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/SemanticVectorChunkingStrategy.cs`:

```csharp
using System.Text.RegularExpressions;
using LightRAGNet.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class SemanticVectorChunkingStrategy(
    IEmbeddingService? embeddingService,
    RecursiveCharacterChunkingStrategy recursiveFallback,
    ILogger<SemanticVectorChunkingStrategy> logger) : IChunkingStrategy
{
    public LightRagChunkingStrategy Strategy => LightRagChunkingStrategy.SemanticVector;

    public async Task<IReadOnlyList<ChunkingSegment>> ChunkAsync(
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var options = request.Options.SemanticVector;
        if (embeddingService is null)
        {
            if (!options.FallBackToRecursiveWhenEmbeddingUnavailable)
            {
                throw new InvalidOperationException("Semantic vector chunking requires an embedding service.");
            }

            logger.LogWarning("Semantic vector chunking is falling back to recursive chunking because embedding service is unavailable.");
            return await recursiveFallback.ChunkAsync(
                request with
                {
                    Options = request.Options with { Strategy = LightRagChunkingStrategy.RecursiveCharacter }
                },
                tokenizer,
                cancellationToken);
        }

        var sentences = SplitSentences(request.Content, options.SentenceSplitRegex);
        if (sentences.Count == 0)
        {
            return [];
        }

        if (sentences.Count == 1)
        {
            return await EmitOrResplitAsync([sentences[0]], request, tokenizer, cancellationToken);
        }

        var windows = BuildWindows(sentences, options.BufferSize);
        var embeddings = await embeddingService.GenerateEmbeddingsAsync(
            windows.Select(window => window.Content),
            cancellationToken);
        var distances = new List<double>();
        for (var i = 0; i < embeddings.Length - 1; i++)
        {
            distances.Add(ChunkingUtilities.CosineDistance(embeddings[i], embeddings[i + 1]));
        }

        var breakpoints = SelectBreakpoints(distances, options);
        var groups = GroupSentences(sentences, breakpoints);
        var mergedGroups = MergeSmallGroups(groups, tokenizer, options);
        return await EmitOrResplitAsync(mergedGroups, request, tokenizer, cancellationToken);
    }

    private static List<SentenceSpan> SplitSentences(string content, string regex)
    {
        var normalized = content.Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        var parts = Regex.Split(normalized, regex)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
        if (parts.Count == 0)
        {
            return [new SentenceSpan(normalized, 0, normalized.Length)];
        }

        var output = new List<SentenceSpan>();
        var cursor = 0;
        foreach (var part in parts)
        {
            var start = normalized.IndexOf(part, cursor, StringComparison.Ordinal);
            if (start < 0)
            {
                start = cursor;
            }

            var end = start + part.Length;
            output.Add(new SentenceSpan(part.Trim(), start, end));
            cursor = end;
        }

        return output;
    }

    private static List<SentenceSpan> BuildWindows(IReadOnlyList<SentenceSpan> sentences, int bufferSize)
    {
        var radius = Math.Max(0, bufferSize);
        return sentences.Select((sentence, index) =>
        {
            var start = Math.Max(0, index - radius);
            var end = Math.Min(sentences.Count - 1, index + radius);
            var content = string.Join(" ", sentences.Skip(start).Take(end - start + 1).Select(item => item.Content));
            return new SentenceSpan(content, sentences[start].Start, sentences[end].End);
        }).ToList();
    }

    private static HashSet<int> SelectBreakpoints(
        IReadOnlyList<double> distances,
        SemanticVectorChunkingSnapshot options)
    {
        if (distances.Count == 0)
        {
            return [];
        }

        if (options.NumberOfChunks is > 1)
        {
            return distances
                .Select((distance, index) => (distance, index))
                .OrderByDescending(item => item.distance)
                .Take(options.NumberOfChunks.Value - 1)
                .Select(item => item.index)
                .ToHashSet();
        }

        var threshold = CalculateThreshold(distances, options);
        return distances
            .Select((distance, index) => (distance, index))
            .Where(item => item.distance > threshold)
            .Select(item => item.index)
            .ToHashSet();
    }

    private static double CalculateThreshold(
        IReadOnlyList<double> distances,
        SemanticVectorChunkingSnapshot options)
    {
        return options.BreakpointThresholdType switch
        {
            SemanticVectorBreakpointThresholdType.Percentile =>
                ChunkingUtilities.Percentile(distances, options.BreakpointThresholdAmount ?? 95),
            SemanticVectorBreakpointThresholdType.StandardDeviation =>
                distances.Average() + (options.BreakpointThresholdAmount ?? 3) * StandardDeviation(distances),
            SemanticVectorBreakpointThresholdType.Interquartile =>
                ChunkingUtilities.Percentile(distances, 75) +
                (options.BreakpointThresholdAmount ?? 1.5) *
                (ChunkingUtilities.Percentile(distances, 75) - ChunkingUtilities.Percentile(distances, 25)),
            SemanticVectorBreakpointThresholdType.Gradient =>
                ChunkingUtilities.Percentile(Gradients(distances), options.BreakpointThresholdAmount ?? 95),
            _ => ChunkingUtilities.Percentile(distances, 95)
        };
    }

    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        var average = values.Average();
        return Math.Sqrt(values.Sum(value => Math.Pow(value - average, 2)) / values.Count);
    }

    private static List<double> Gradients(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return [.. values];
        }

        return values.Zip(values.Skip(1), (left, right) => Math.Abs(right - left)).ToList();
    }

    private static List<List<SentenceSpan>> GroupSentences(
        IReadOnlyList<SentenceSpan> sentences,
        HashSet<int> breakpoints)
    {
        var groups = new List<List<SentenceSpan>>();
        var current = new List<SentenceSpan>();
        for (var i = 0; i < sentences.Count; i++)
        {
            current.Add(sentences[i]);
            if (breakpoints.Contains(i))
            {
                groups.Add(current);
                current = [];
            }
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        return groups;
    }

    private static List<List<SentenceSpan>> MergeSmallGroups(
        List<List<SentenceSpan>> groups,
        ITokenizer tokenizer,
        SemanticVectorChunkingSnapshot options)
    {
        var minTokens = options.MinChunkTokenSize;
        if (minTokens <= 0 || groups.Count <= 1)
        {
            return groups;
        }

        var output = new List<List<SentenceSpan>>();
        foreach (var group in groups)
        {
            var text = string.Join(" ", group.Select(sentence => sentence.Content));
            if (ChunkingUtilities.CountTokens(tokenizer, text) >= minTokens || output.Count == 0)
            {
                output.Add(group);
                continue;
            }

            var previous = output[^1];
            var merged = previous.Concat(group).ToList();
            var mergedText = string.Join(" ", merged.Select(sentence => sentence.Content));
            if (ChunkingUtilities.CountTokens(tokenizer, mergedText) <= options.ChunkTokenSize)
            {
                output[^1] = merged;
            }
            else
            {
                output.Add(group);
            }
        }

        return output;
    }

    private async Task<IReadOnlyList<ChunkingSegment>> EmitOrResplitAsync(
        IReadOnlyList<List<SentenceSpan>> groups,
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var output = new List<ChunkingSegment>();
        foreach (var group in groups)
        {
            var content = string.Join(" ", group.Select(sentence => sentence.Content)).Trim();
            if (content.Length == 0)
            {
                continue;
            }

            var tokens = ChunkingUtilities.CountTokens(tokenizer, content);
            if (tokens <= request.Options.SemanticVector.ChunkTokenSize)
            {
                output.Add(new ChunkingSegment
                {
                    Content = content,
                    Tokens = tokens,
                    Order = output.Count,
                    Strategy = Strategy,
                    SourceSpan = new SourceSpan(group[0].Start, group[^1].End)
                });
                continue;
            }

            var recursivePieces = await recursiveFallback.ChunkAsync(
                request with
                {
                    Content = content,
                    Options = request.Options with { Strategy = LightRagChunkingStrategy.RecursiveCharacter }
                },
                tokenizer,
                cancellationToken);
            output.AddRange(recursivePieces.Select(piece => new ChunkingSegment
            {
                Content = piece.Content,
                Tokens = piece.Tokens,
                Order = output.Count,
                Strategy = piece.Strategy,
                SourceSpan = piece.SourceSpan
            }));
        }

        return output.Select((segment, index) => new ChunkingSegment
        {
            Content = segment.Content,
            Tokens = segment.Tokens,
            Order = index,
            Strategy = segment.Strategy,
            SourceSpan = segment.SourceSpan,
            Heading = segment.Heading,
            Metadata = segment.Metadata
        }).ToList();
    }

    private sealed record SentenceSpan(string Content, int Start, int End);
}
```

- [ ] **Step 4: Run semantic vector tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~SemanticVectorChunkingStrategyTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Commit semantic vector strategy**

```powershell
git add src\LightRAGNet\Services\DocumentProcessing\Chunking\SemanticVectorChunkingStrategy.cs tests\LightRAGNet.Tests\DocumentProcessing\Chunking\SemanticVectorChunkingStrategyTests.cs
git commit -m "feat: add semantic vector chunking"
```

## Task 5: Add Markdown Block Builder for Paragraph Semantic Chunking

**Files:**
- Create: `src/LightRAGNet/Services/DocumentProcessing/Chunking/MarkdownDocumentBlockBuilder.cs`
- Test: `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/MarkdownDocumentBlockBuilderTests.cs`

- [ ] **Step 1: Write block builder tests**

Create `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/MarkdownDocumentBlockBuilderTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing.Chunking;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class MarkdownDocumentBlockBuilderTests
{
    [Fact]
    public void Build_CreatesHeadingBlocksWithParentHierarchy()
    {
        const string markdown = """
                                # Root
                                intro

                                ## Child
                                body
                                """;

        var blocks = MarkdownDocumentBlockBuilder.Build(markdown);

        blocks.Should().HaveCount(2);
        blocks[0].Heading.Should().Be("Root");
        blocks[0].Level.Should().Be(1);
        blocks[0].Content.Should().Contain("intro");
        blocks[1].Heading.Should().Be("Child");
        blocks[1].ParentHeadings.Should().Equal("Root");
    }

    [Fact]
    public void Build_KeepsMarkdownTableAsTableBlock()
    {
        const string markdown = """
                                # Data
                                | A | B |
                                | - | - |
                                | 1 | 2 |
                                """;

        var blocks = MarkdownDocumentBlockBuilder.Build(markdown);

        blocks.Should().ContainSingle(block => block.Kind == DocumentBlockKind.Table);
    }

    [Fact]
    public void Build_HandlesContentWithoutHeading()
    {
        var blocks = MarkdownDocumentBlockBuilder.Build("alpha\n\nbeta");

        blocks.Should().ContainSingle();
        blocks[0].Heading.Should().BeEmpty();
        blocks[0].Content.Should().Contain("alpha");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~MarkdownDocumentBlockBuilderTests" --no-restore --verbosity minimal
```

Expected: build fails because `MarkdownDocumentBlockBuilder` does not exist.

- [ ] **Step 3: Implement block builder**

Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/MarkdownDocumentBlockBuilder.cs`:

```csharp
using System.Text.RegularExpressions;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public enum DocumentBlockKind
{
    Text,
    Table,
    Code
}

public sealed class DocumentBlock
{
    public string Content { get; init; } = string.Empty;
    public int Level { get; init; }
    public string Heading { get; init; } = string.Empty;
    public IReadOnlyList<string> ParentHeadings { get; init; } = [];
    public DocumentBlockKind Kind { get; init; }
    public SourceSpan? SourceSpan { get; init; }
}

public static class MarkdownDocumentBlockBuilder
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);

    public static IReadOnlyList<DocumentBlock> Build(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var lines = content.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<DocumentBlock>();
        var headingStack = new Stack<(int Level, string Heading)>();
        var currentHeading = string.Empty;
        var currentLevel = 0;
        var currentLines = new List<string>();
        var currentStart = 0;
        var offset = 0;
        var inCode = false;

        void Flush()
        {
            var body = string.Join("\n", currentLines).Trim();
            if (body.Length == 0)
            {
                currentLines.Clear();
                return;
            }

            blocks.Add(new DocumentBlock
            {
                Content = body,
                Level = currentLevel,
                Heading = currentHeading,
                ParentHeadings = headingStack
                    .Reverse()
                    .Where(item => item.Level < currentLevel)
                    .Select(item => item.Heading)
                    .ToList(),
                Kind = DetectKind(body),
                SourceSpan = ChunkingUtilities.TrimmedSpan(content, currentStart, currentStart + body.Length)
            });
            currentLines.Clear();
        }

        foreach (var line in lines)
        {
            var lineStart = offset;
            offset += line.Length + 1;

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                if (currentLines.Count == 0)
                {
                    currentStart = lineStart;
                }
                currentLines.Add(line);
                continue;
            }

            var headingMatch = !inCode ? HeadingRegex.Match(line) : Match.Empty;
            if (headingMatch.Success)
            {
                Flush();
                currentLevel = headingMatch.Groups[1].Value.Length;
                currentHeading = headingMatch.Groups[2].Value.Trim();
                while (headingStack.Count > 0 && headingStack.Peek().Level >= currentLevel)
                {
                    headingStack.Pop();
                }

                headingStack.Push((currentLevel, currentHeading));
                currentStart = offset;
                continue;
            }

            if (currentLines.Count == 0)
            {
                currentStart = lineStart;
            }

            currentLines.Add(line);
        }

        Flush();

        if (blocks.Count == 0)
        {
            return
            [
                new DocumentBlock
                {
                    Content = content.Trim(),
                    Kind = DocumentBlockKind.Text,
                    SourceSpan = ChunkingUtilities.TrimmedSpan(content, 0, content.Length)
                }
            ];
        }

        return blocks;
    }

    private static DocumentBlockKind DetectKind(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return DocumentBlockKind.Code;
        }

        var lines = trimmed.Split('\n');
        if (lines.Length >= 2 &&
            lines[0].TrimStart().StartsWith("|", StringComparison.Ordinal) &&
            lines[1].Contains("---", StringComparison.Ordinal))
        {
            return DocumentBlockKind.Table;
        }

        return DocumentBlockKind.Text;
    }
}
```

- [ ] **Step 4: Run block builder tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~MarkdownDocumentBlockBuilderTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Commit block builder**

```powershell
git add src\LightRAGNet\Services\DocumentProcessing\Chunking\MarkdownDocumentBlockBuilder.cs tests\LightRAGNet.Tests\DocumentProcessing\Chunking\MarkdownDocumentBlockBuilderTests.cs
git commit -m "feat: add markdown block builder"
```

## Task 6: Implement Paragraph Semantic Strategy

**Files:**
- Create: `src/LightRAGNet/Services/DocumentProcessing/Chunking/ParagraphSemanticChunkingStrategy.cs`
- Test: `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/ParagraphSemanticChunkingStrategyTests.cs`

- [ ] **Step 1: Write paragraph semantic tests**

Create `tests/LightRAGNet.Tests/DocumentProcessing/Chunking/ParagraphSemanticChunkingStrategyTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Services.DocumentProcessing.Chunking;
using LightRAGNet.Tests.TestDoubles;

namespace LightRAGNet.Tests.DocumentProcessing.Chunking;

public sealed class ParagraphSemanticChunkingStrategyTests
{
    [Fact]
    public async Task ChunkAsync_DoesNotMergeAcrossTopLevelHeadings()
    {
        const string markdown = """
                                # A
                                alpha beta

                                # B
                                gamma delta
                                """;
        var strategy = new ParagraphSemanticChunkingStrategy(
            new RecursiveCharacterChunkingStrategy(new FixedTokenChunkingStrategy()));

        var chunks = await strategy.ChunkAsync(
            CreateRequest(markdown, chunkSize: 20),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().HaveCount(2);
        chunks[0].Heading!.Heading.Should().Be("A");
        chunks[1].Heading!.Heading.Should().Be("B");
    }

    [Fact]
    public async Task ChunkAsync_LongSingleBlockFallsBackToRecursive()
    {
        var markdown = "# Long\n" + string.Join(" ", Enumerable.Range(0, 20).Select(i => $"word{i}"));
        var strategy = new ParagraphSemanticChunkingStrategy(
            new RecursiveCharacterChunkingStrategy(new FixedTokenChunkingStrategy()));

        var chunks = await strategy.ChunkAsync(
            CreateRequest(markdown, chunkSize: 5),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.Tokens <= 5);
        chunks.Should().OnlyContain(chunk => chunk.Heading!.Heading.StartsWith("Long", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChunkAsync_TableSplitsByRowsBeforeRecursiveFallback()
    {
        const string markdown = """
                                # Data
                                | A | B |
                                | - | - |
                                | alpha | beta |
                                | gamma | delta |
                                | eta | theta |
                                """;
        var strategy = new ParagraphSemanticChunkingStrategy(
            new RecursiveCharacterChunkingStrategy(new FixedTokenChunkingStrategy()));

        var chunks = await strategy.ChunkAsync(
            CreateRequest(markdown, chunkSize: 8),
            new FakeTokenizer(),
            CancellationToken.None);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.Tokens <= 8);
        chunks.Should().OnlyContain(chunk => chunk.Content.Contains("|", StringComparison.Ordinal));
    }

    private static ChunkingRequest CreateRequest(string content, int chunkSize)
    {
        var snapshot = new LightRagChunkingSnapshot(
            LightRagChunkingStrategy.ParagraphSemantic,
            chunkSize,
            new FixedTokenChunkingSnapshot(chunkSize, 0, null, false),
            new RecursiveCharacterChunkingSnapshot(chunkSize, 0, ["\n\n", "\n", " ", ""]),
            new SemanticVectorChunkingSnapshot(
                chunkSize,
                SemanticVectorBreakpointThresholdType.Percentile,
                null,
                1,
                null,
                null,
                0,
                @"(?<=[。？！.!?])\s+",
                true),
            new ParagraphSemanticChunkingSnapshot(chunkSize, 0, 0));

        return new ChunkingRequest(content, "doc-p", "paragraph.md", snapshot);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~ParagraphSemanticChunkingStrategyTests" --no-restore --verbosity minimal
```

Expected: build fails because `ParagraphSemanticChunkingStrategy` does not exist.

- [ ] **Step 3: Implement paragraph semantic strategy**

Create `src/LightRAGNet/Services/DocumentProcessing/Chunking/ParagraphSemanticChunkingStrategy.cs`:

```csharp
using LightRAGNet.Core.Interfaces;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class ParagraphSemanticChunkingStrategy(
    RecursiveCharacterChunkingStrategy recursiveFallback) : IChunkingStrategy
{
    public LightRagChunkingStrategy Strategy => LightRagChunkingStrategy.ParagraphSemantic;

    public async Task<IReadOnlyList<ChunkingSegment>> ChunkAsync(
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var blocks = MarkdownDocumentBlockBuilder.Build(request.Content);
        if (blocks.Count == 0)
        {
            return await recursiveFallback.ChunkAsync(
                request with { Options = request.Options with { Strategy = LightRagChunkingStrategy.RecursiveCharacter } },
                tokenizer,
                cancellationToken);
        }

        var output = new List<ChunkingSegment>();
        foreach (var block in blocks)
        {
            var blockTokens = ChunkingUtilities.CountTokens(tokenizer, block.Content);
            if (block.Kind == DocumentBlockKind.Table && blockTokens > request.Options.ParagraphSemantic.ChunkTokenSize)
            {
                output.AddRange(await SplitTableAsync(block, request, tokenizer, cancellationToken));
                continue;
            }

            if (blockTokens > request.Options.ParagraphSemantic.ChunkTokenSize)
            {
                output.AddRange(await SplitLongBlockAsync(block, request, tokenizer, cancellationToken));
                continue;
            }

            output.Add(CreateSegment(block, block.Content, blockTokens, output.Count));
        }

        return MergeSmallBlocks(output, tokenizer, request.Options.ParagraphSemantic.ChunkTokenSize)
            .Select((segment, index) => new ChunkingSegment
            {
                Content = segment.Content,
                Tokens = segment.Tokens,
                Order = index,
                Strategy = Strategy,
                SourceSpan = segment.SourceSpan,
                Heading = segment.Heading,
                Metadata = segment.Metadata
            })
            .ToList();
    }

    private async Task<IReadOnlyList<ChunkingSegment>> SplitLongBlockAsync(
        DocumentBlock block,
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var recursive = await recursiveFallback.ChunkAsync(
            request with
            {
                Content = block.Content,
                Options = request.Options with { Strategy = LightRagChunkingStrategy.RecursiveCharacter }
            },
            tokenizer,
            cancellationToken);

        return recursive.Select((segment, index) => new ChunkingSegment
        {
            Content = segment.Content,
            Tokens = segment.Tokens,
            Order = index,
            Strategy = Strategy,
            SourceSpan = segment.SourceSpan,
            Heading = new ChunkHeading(
                block.Level,
                $"{block.Heading} [part {index + 1}]".Trim(),
                block.ParentHeadings)
        }).ToList();
    }

    private async Task<IReadOnlyList<ChunkingSegment>> SplitTableAsync(
        DocumentBlock block,
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var lines = block.Content.Split('\n').Where(line => line.Trim().Length > 0).ToList();
        if (lines.Count <= 2)
        {
            return await SplitLongBlockAsync(block, request, tokenizer, cancellationToken);
        }

        var header = lines.Take(2).ToList();
        var rows = lines.Skip(2).ToList();
        var chunks = new List<ChunkingSegment>();
        var buffer = new List<string>(header);
        foreach (var row in rows)
        {
            var candidate = string.Join("\n", buffer.Concat([row]));
            if (buffer.Count > header.Count &&
                ChunkingUtilities.CountTokens(tokenizer, candidate) > request.Options.ParagraphSemantic.ChunkTokenSize)
            {
                var content = string.Join("\n", buffer);
                chunks.Add(CreateSegment(block, content, ChunkingUtilities.CountTokens(tokenizer, content), chunks.Count));
                buffer = [.. header, row];
                continue;
            }

            buffer.Add(row);
        }

        if (buffer.Count > header.Count)
        {
            var content = string.Join("\n", buffer);
            if (ChunkingUtilities.CountTokens(tokenizer, content) <= request.Options.ParagraphSemantic.ChunkTokenSize)
            {
                chunks.Add(CreateSegment(block, content, ChunkingUtilities.CountTokens(tokenizer, content), chunks.Count));
            }
            else
            {
                chunks.AddRange(await SplitLongBlockAsync(
                    new DocumentBlock
                    {
                        Content = content,
                        Level = block.Level,
                        Heading = block.Heading,
                        ParentHeadings = block.ParentHeadings,
                        Kind = block.Kind,
                        SourceSpan = block.SourceSpan
                    },
                    request,
                    tokenizer,
                    cancellationToken));
            }
        }

        return chunks;
    }

    private static ChunkingSegment CreateSegment(DocumentBlock block, string content, int tokens, int order)
    {
        return new ChunkingSegment
        {
            Content = content.Trim(),
            Tokens = tokens,
            Order = order,
            Strategy = LightRagChunkingStrategy.ParagraphSemantic,
            SourceSpan = block.SourceSpan,
            Heading = new ChunkHeading(block.Level, block.Heading, block.ParentHeadings)
        };
    }

    private static List<ChunkingSegment> MergeSmallBlocks(
        List<ChunkingSegment> chunks,
        ITokenizer tokenizer,
        int chunkSize)
    {
        if (chunks.Count <= 1)
        {
            return chunks;
        }

        var output = new List<ChunkingSegment>();
        foreach (var chunk in chunks)
        {
            if (output.Count == 0)
            {
                output.Add(chunk);
                continue;
            }

            var previous = output[^1];
            var canMergeByHeading =
                previous.Heading?.Level == chunk.Heading?.Level &&
                previous.Heading?.Heading == chunk.Heading?.Heading;
            if (!canMergeByHeading)
            {
                output.Add(chunk);
                continue;
            }

            var mergedContent = previous.Content + "\n\n" + chunk.Content;
            var mergedTokens = ChunkingUtilities.CountTokens(tokenizer, mergedContent);
            if (mergedTokens <= chunkSize)
            {
                output[^1] = new ChunkingSegment
                {
                    Content = mergedContent,
                    Tokens = mergedTokens,
                    Order = previous.Order,
                    Strategy = previous.Strategy,
                    SourceSpan = previous.SourceSpan,
                    Heading = previous.Heading
                };
            }
            else
            {
                output.Add(chunk);
            }
        }

        return output;
    }
}
```

- [ ] **Step 4: Run paragraph tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~ParagraphSemanticChunkingStrategyTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Commit paragraph semantic strategy**

```powershell
git add src\LightRAGNet\Services\DocumentProcessing\Chunking\ParagraphSemanticChunkingStrategy.cs tests\LightRAGNet.Tests\DocumentProcessing\Chunking\ParagraphSemanticChunkingStrategyTests.cs
git commit -m "feat: add paragraph semantic chunking"
```

## Task 7: Wire Strategies into DI and Insert Flow

**Files:**
- Modify: `src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs`
- Modify: `src/LightRAGNet/LightRAG.cs`
- Modify: `src/LightRAGNet/Services/DocumentLifecycle/DocumentLifecycleService.cs`
- Test: `tests/LightRAGNet.Tests/DocumentLifecycle/DocumentLifecycleServiceTests.cs`
- Test: `tests/LightRAGNet.Tests/DocumentLifecycle/LightRAGLifecycleIntegrationTests.cs`

- [ ] **Step 1: Write lifecycle metadata test**

Add to `tests/LightRAGNet.Tests/DocumentLifecycle/DocumentLifecycleServiceTests.cs`:

```csharp
[Fact]
public async Task RecordChunkingMetadataAsync_StoresStrategyAndSnapshot()
{
    var service = CreateService();
    await service.PrepareIngestionAsync("alpha beta", "doc-1", "file.md");

    await service.RecordChunkingMetadataAsync(
        "_",
        "doc-1",
        new Dictionary<string, object>
        {
            ["chunking_strategy"] = "R",
            ["chunk_token_size"] = 12
        });

    var record = await Store.GetAsync("_", "doc-1");
    record!.Metadata["chunking_strategy"].Should().Be("R");
    record.Metadata["chunk_token_size"].Should().Be(12);
}
```

Use the existing store field/helper in that test file. If no accessible store field exists, expose the in-memory store from the local test helper by returning `(service, store)`.

- [ ] **Step 2: Run lifecycle metadata test to verify it fails**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~RecordChunkingMetadataAsync_StoresStrategyAndSnapshot" --no-restore --verbosity minimal
```

Expected: build fails because `RecordChunkingMetadataAsync` does not exist.

- [ ] **Step 3: Implement lifecycle metadata recording**

Add to `DocumentLifecycleService`:

```csharp
public async Task RecordChunkingMetadataAsync(
    string workspace,
    string docId,
    IReadOnlyDictionary<string, object> metadata,
    CancellationToken cancellationToken = default)
{
    var normalizedWorkspace = NormalizeWorkspace(workspace);
    var record = await _statusStore.GetAsync(normalizedWorkspace, docId, cancellationToken);
    if (record is null)
    {
        LogMissingStatusMutation(normalizedWorkspace, docId, nameof(RecordChunkingMetadataAsync));
        return;
    }

    foreach (var item in metadata)
    {
        record.Metadata[item.Key] = item.Value;
    }

    Touch(record);
    await _statusStore.UpsertAsync(record, cancellationToken);
}
```

- [ ] **Step 4: Add chunking metadata helper**

Add to `LightRagChunkingService`:

```csharp
public static Dictionary<string, object> CreateMetadata(LightRagChunkingSnapshot snapshot)
{
    return new Dictionary<string, object>
    {
        ["chunking_strategy"] = snapshot.Strategy.ToWireValue(),
        ["chunk_token_size"] = snapshot.Strategy switch
        {
            LightRagChunkingStrategy.FixedToken => snapshot.FixedToken.ChunkTokenSize,
            LightRagChunkingStrategy.RecursiveCharacter => snapshot.RecursiveCharacter.ChunkTokenSize,
            LightRagChunkingStrategy.SemanticVector => snapshot.SemanticVector.ChunkTokenSize,
            LightRagChunkingStrategy.ParagraphSemantic => snapshot.ParagraphSemantic.ChunkTokenSize,
            _ => snapshot.ChunkTokenSize
        }
    };
}
```

- [ ] **Step 5: Register chunking services**

Modify `ServiceCollectionExtensions` near `DocumentProcessingService` registration:

```csharp
services.AddSingleton<FixedTokenChunkingStrategy>();
services.AddSingleton<RecursiveCharacterChunkingStrategy>();
services.AddSingleton<SemanticVectorChunkingStrategy>();
services.AddSingleton<ParagraphSemanticChunkingStrategy>();
services.AddSingleton<IChunkingStrategy>(sp => sp.GetRequiredService<FixedTokenChunkingStrategy>());
services.AddSingleton<IChunkingStrategy>(sp => sp.GetRequiredService<RecursiveCharacterChunkingStrategy>());
services.AddSingleton<IChunkingStrategy>(sp => sp.GetRequiredService<SemanticVectorChunkingStrategy>());
services.AddSingleton<IChunkingStrategy>(sp => sp.GetRequiredService<ParagraphSemanticChunkingStrategy>());
services.AddSingleton<LightRagChunkingService>();
services.AddSingleton<DocumentProcessingService>();
```

Add `using LightRAGNet.Services.DocumentProcessing.Chunking;`.

- [ ] **Step 6: Switch LightRAG.InsertAsync to async chunking**

Replace the existing chunking call in `LightRAG.InsertAsync`:

```csharp
var chunkingSnapshot = documentProcessingService.CreateChunkingSnapshot();
await documentLifecycleService.RecordChunkingMetadataAsync(
    ingestion.Workspace,
    docId,
    LightRagChunkingService.CreateMetadata(chunkingSnapshot),
    cancellationToken);

var chunks = (await documentProcessingService.ChunkDocumentAsync(
    content,
    docId,
    filePath,
    chunkingSnapshot,
    cancellationToken)).ToList();
```

Add to `DocumentProcessingService`:

```csharp
public LightRagChunkingSnapshot CreateChunkingSnapshot()
{
    return _options.CreateChunkingSnapshot();
}
```

- [ ] **Step 7: Run integration-adjacent tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~DocumentLifecycleServiceTests|FullyQualifiedName~LightRAGLifecycleIntegrationTests" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 8: Commit DI and insert flow wiring**

```powershell
git add src\LightRAGNet.Hosting\ServiceCollectionExtensions.cs src\LightRAGNet\LightRAG.cs src\LightRAGNet\Services\DocumentLifecycle src\LightRAGNet\Services\DocumentProcessing tests\LightRAGNet.Tests\DocumentLifecycle
git commit -m "feat: wire chunking strategies into ingestion"
```

## Task 8: Update Direct Constructor Tests and Run Full Verification

**Files:**
- Modify: tests that manually construct `DocumentProcessingService`
- Modify: `docs/superpowers/archives/2026-06/2026-06-02-chunking-strategy-parity-archives.md` after implementation verification
- Modify: `docs/superpowers/archives/INDEX.md` after archive creation

- [ ] **Step 1: Find remaining constructor call sites**

Run:

```powershell
rg -n --encoding utf-8 "new DocumentProcessingService" tests src
```

Expected: lists all direct construction sites. Every construction site must either pass a `LightRagChunkingService` or intentionally rely on the nullable fallback for tests that never call async chunking.

- [ ] **Step 2: Add local test helper**

In each test file that constructs `DocumentProcessingService` and calls `InsertAsync`, add this helper:

```csharp
private static LightRagChunkingService CreateChunkingService(
    ITokenizer tokenizer,
    LightRAGOptions options)
{
    var fixedToken = new FixedTokenChunkingStrategy();
    var recursive = new RecursiveCharacterChunkingStrategy(fixedToken);
    var semantic = new SemanticVectorChunkingStrategy(
        Substitute.For<IEmbeddingService>(),
        recursive,
        NullLogger<SemanticVectorChunkingStrategy>.Instance);
    var paragraph = new ParagraphSemanticChunkingStrategy(recursive);

    return new LightRagChunkingService(
        [fixedToken, recursive, semantic, paragraph],
        tokenizer,
        Options.Create(options),
        NullLogger<LightRagChunkingService>.Instance);
}
```

Then pass the helper result to `DocumentProcessingService`.

- [ ] **Step 3: Run focused DocumentProcessing tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~DocumentProcessing" --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 4: Run full core tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --verbosity minimal
```

Expected: pass.

- [ ] **Step 5: Run server tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --verbosity minimal
```

Expected: pass. If failures involve external Qdrant or Neo4j dependencies, stop and inspect the test isolation boundary before changing production code.

- [ ] **Step 6: Run full solution tests**

Run:

```powershell
dotnet test .\LightRAGNet.slnx --verbosity minimal
```

Expected: pass.

- [ ] **Step 7: Run asset closeout gate**

Run:

```powershell
$env:PYTHONIOENCODING='utf-8'; python 'C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.2.3\skills\compound-development-asset\scripts\asset_closeout.py' . --topic 'chunking-strategy-parity' --json
```

Expected: reports missing archive for completed topic. Route to archive after implementation verification.

- [ ] **Step 8: Archive completed requirement**

Use `superpowers-asset-compounding:archive-superpowers-feature` to create:

```text
docs/superpowers/archives/2026-06/2026-06-02-chunking-strategy-parity-archives.md
```

The archive must include:

- spec path,
- implementation commit range,
- commands from Steps 3-6,
- summary of F/R/V/P strategy behavior,
- known out-of-scope follow-ups for UI selection and reindexing.

- [ ] **Step 9: Commit archive**

```powershell
git add docs\superpowers\archives
git commit -m "docs: archive chunking strategy parity"
```

## Plan Self-Review

- Spec coverage: the plan covers contracts/options, F compatibility, R recursive splitting, V embedding breakpoint splitting, P Markdown block chunking, small-block merge rules, config snapshot metadata, DI wiring, and verification.
- Scope check: React strategy controls, batch reindexing, and full Python sidecar parity remain out of scope as required by the spec.
- Type consistency: strategy names, option records, and `ChunkDocumentAsync` signatures are used consistently across tasks.
- Type consistency check: `DocumentBlock` is used as a class throughout Task 5 and Task 6, and long-table fallback creates a replacement instance with an object initializer.
