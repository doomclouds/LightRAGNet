using LightRAGNet.Core.Utils;

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
        cancellationToken.ThrowIfCancellationRequested();

        var options = Normalize(request.Options.ParagraphSemantic);
        var blocks = MarkdownDocumentBlockBuilder.Build(request.Content);
        if (blocks.Count == 0)
        {
            return await recursiveFallback.ChunkAsync(
                request with { Options = CreateRecursiveOptions(request.Options, options) },
                tokenizer,
                cancellationToken);
        }

        var chunks = new List<ChunkingSegment>();
        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var blockTokens = ChunkingUtilities.CountTokens(tokenizer, block.Content);
            if (blockTokens == 0)
            {
                continue;
            }

            if (block.Kind == DocumentBlockKind.Table && blockTokens > options.ChunkTokenSize)
            {
                chunks.AddRange(await SplitTableAsync(block, request, options, tokenizer, cancellationToken));
                continue;
            }

            if (blockTokens > options.ChunkTokenSize)
            {
                chunks.AddRange(await SplitLongBlockAsync(block, request, options, tokenizer, cancellationToken));
                continue;
            }

            chunks.Add(CreateSegment(
                block,
                block.Content,
                blockTokens,
                block.SourceSpan));
        }

        return Reindex(MergeSmallBlocks(chunks, tokenizer, options.ChunkTokenSize));
    }

    private static ParagraphSemanticChunkingSnapshot Normalize(ParagraphSemanticChunkingSnapshot options)
    {
        var chunkSize = ChunkingUtilities.RequirePositiveChunkSize(
            options.ChunkTokenSize,
            "Chunking:ParagraphSemantic:ChunkTokenSize");
        var overlap = ChunkingUtilities.ClampOverlap(chunkSize, options.ChunkOverlapTokenSize);
        var minChunkSize = Math.Max(0, options.MinChunkTokenSize);

        return new ParagraphSemanticChunkingSnapshot(chunkSize, overlap, minChunkSize);
    }

    private static LightRagChunkingSnapshot CreateRecursiveOptions(
        LightRagChunkingSnapshot snapshot,
        ParagraphSemanticChunkingSnapshot options)
    {
        var separators = snapshot.RecursiveCharacter.Separators is { Count: > 0 }
            ? snapshot.RecursiveCharacter.Separators
            : RecursiveCharacterChunkingOptions.CreateDefaultSeparators();

        return snapshot with
        {
            Strategy = LightRagChunkingStrategy.RecursiveCharacter,
            RecursiveCharacter = new RecursiveCharacterChunkingSnapshot(
                options.ChunkTokenSize,
                options.ChunkOverlapTokenSize,
                [.. separators])
        };
    }

    private async Task<IReadOnlyList<ChunkingSegment>> SplitLongBlockAsync(
        DocumentBlock block,
        ChunkingRequest request,
        ParagraphSemanticChunkingSnapshot options,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var recursive = await recursiveFallback.ChunkAsync(
            request with
            {
                Content = block.Content,
                Options = CreateRecursiveOptions(request.Options, options)
            },
            tokenizer,
            cancellationToken);

        return recursive
            .Select((segment, index) => new ChunkingSegment
            {
                Content = segment.Content,
                Tokens = segment.Tokens,
                Strategy = Strategy,
                SourceSpan = MapSourceSpan(block, segment),
                Heading = CreateHeading(block, recursive.Count > 1 ? index + 1 : null),
                Metadata = segment.Metadata
            })
            .ToList();
    }

    private async Task<IReadOnlyList<ChunkingSegment>> SplitTableAsync(
        DocumentBlock block,
        ChunkingRequest request,
        ParagraphSemanticChunkingSnapshot options,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var lines = ParseTableLines(block.Content);
        if (lines.Count <= 2)
        {
            return await SplitLongBlockAsync(block, request, options, tokenizer, cancellationToken);
        }

        var header = lines.Take(2).ToList();
        var rows = lines.Skip(2).ToList();
        var chunks = new List<ChunkingSegment>();
        var currentRows = new List<TableLine>();

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidateRows = currentRows.Concat([row]).ToList();
            var candidate = BuildTableChunk(header, candidateRows);
            var candidateTokens = ChunkingUtilities.CountTokens(tokenizer, candidate);
            if (candidateTokens <= options.ChunkTokenSize)
            {
                currentRows.Add(row);
                continue;
            }

            if (currentRows.Count > 0)
            {
                chunks.Add(CreateTableSegment(block, header, currentRows, tokenizer));
                currentRows = [row];

                var singleRow = BuildTableChunk(header, currentRows);
                if (ChunkingUtilities.CountTokens(tokenizer, singleRow) <= options.ChunkTokenSize)
                {
                    continue;
                }
            }

            chunks.AddRange(await SplitLongBlockAsync(
                CreateSyntheticTableBlock(block, BuildTableChunk(header, [row])),
                request,
                options,
                tokenizer,
                cancellationToken));
            currentRows.Clear();
        }

        if (currentRows.Count > 0)
        {
            chunks.Add(CreateTableSegment(block, header, currentRows, tokenizer));
        }

        return chunks;
    }

    private static ChunkingSegment CreateSegment(
        DocumentBlock block,
        string content,
        int tokens,
        SourceSpan? sourceSpan)
    {
        return new ChunkingSegment
        {
            Content = content.Trim(),
            Tokens = tokens,
            Strategy = LightRagChunkingStrategy.ParagraphSemantic,
            SourceSpan = sourceSpan,
            Heading = CreateHeading(block, partNumber: null)
        };
    }

    private static ChunkingSegment CreateTableSegment(
        DocumentBlock block,
        IReadOnlyList<TableLine> header,
        IReadOnlyList<TableLine> rows,
        ITokenizer tokenizer)
    {
        var content = BuildTableChunk(header, rows);
        return CreateSegment(
            block,
            content,
            ChunkingUtilities.CountTokens(tokenizer, content),
            sourceSpan: null);
    }

    private static DocumentBlock CreateSyntheticTableBlock(DocumentBlock block, string content)
    {
        return new DocumentBlock
        {
            Content = content,
            Level = block.Level,
            Heading = block.Heading,
            ParentHeadings = block.ParentHeadings,
            Kind = block.Kind,
            SourceSpan = null
        };
    }

    private static ChunkHeading CreateHeading(DocumentBlock block, int? partNumber)
    {
        var heading = block.Heading;
        if (partNumber is not null)
        {
            heading = string.IsNullOrWhiteSpace(heading)
                ? $"[part {partNumber.Value}]"
                : $"{heading} [part {partNumber.Value}]";
        }

        return new ChunkHeading(block.Level, heading, block.ParentHeadings);
    }

    private static SourceSpan? MapSourceSpan(DocumentBlock block, ChunkingSegment segment)
    {
        if (segment.SourceSpan is not null)
        {
            if (block.SourceSpan is null)
            {
                return segment.SourceSpan;
            }

            return new SourceSpan(
                block.SourceSpan.Start + segment.SourceSpan.Start,
                block.SourceSpan.Start + segment.SourceSpan.End);
        }

        if (block.SourceSpan is null)
        {
            return null;
        }

        var relativeStart = block.Content.IndexOf(segment.Content, StringComparison.Ordinal);
        return relativeStart < 0
            ? null
            : new SourceSpan(
                block.SourceSpan.Start + relativeStart,
                block.SourceSpan.Start + relativeStart + segment.Content.Length);
    }

    private static List<ChunkingSegment> MergeSmallBlocks(
        IReadOnlyList<ChunkingSegment> chunks,
        ITokenizer tokenizer,
        int chunkSize)
    {
        if (chunks.Count <= 1)
        {
            return [.. chunks];
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
            if (!HasSameHeadingPath(previous.Heading, chunk.Heading))
            {
                output.Add(chunk);
                continue;
            }

            var mergedContent = previous.Content + "\n\n" + chunk.Content;
            var mergedTokens = ChunkingUtilities.CountTokens(tokenizer, mergedContent);
            if (mergedTokens > chunkSize)
            {
                output.Add(chunk);
                continue;
            }

            output[^1] = new ChunkingSegment
            {
                Content = mergedContent,
                Tokens = mergedTokens,
                Strategy = LightRagChunkingStrategy.ParagraphSemantic,
                SourceSpan = MergeSourceSpans(previous.SourceSpan, chunk.SourceSpan),
                Heading = previous.Heading,
                Metadata = previous.Metadata
            };
        }

        return output;
    }

    private static bool HasSameHeadingPath(ChunkHeading? left, ChunkHeading? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Level == right.Level &&
            string.Equals(left.Heading, right.Heading, StringComparison.Ordinal) &&
            left.ParentHeadings.SequenceEqual(right.ParentHeadings, StringComparer.Ordinal);
    }

    private static SourceSpan? MergeSourceSpans(SourceSpan? left, SourceSpan? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        return new SourceSpan(left.Start, right.End);
    }

    private static List<ChunkingSegment> Reindex(IReadOnlyList<ChunkingSegment> chunks)
    {
        return chunks
            .Select((chunk, index) => new ChunkingSegment
            {
                Content = chunk.Content,
                Tokens = chunk.Tokens,
                Order = index,
                Strategy = LightRagChunkingStrategy.ParagraphSemantic,
                SourceSpan = chunk.SourceSpan,
                Heading = chunk.Heading,
                Metadata = chunk.Metadata
            })
            .ToList();
    }

    private static List<TableLine> ParseTableLines(string content)
    {
        var lines = new List<TableLine>();
        var start = 0;
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] is not ('\r' or '\n'))
            {
                continue;
            }

            var end = index;
            var lineBreakEnd = index + 1;
            if (content[index] == '\r' && lineBreakEnd < content.Length && content[lineBreakEnd] == '\n')
            {
                lineBreakEnd++;
            }

            AddTableLine(content, start, end, lines);
            start = lineBreakEnd;
            index = lineBreakEnd - 1;
        }

        if (start < content.Length)
        {
            AddTableLine(content, start, content.Length, lines);
        }

        return lines;
    }

    private static void AddTableLine(
        string content,
        int start,
        int end,
        ICollection<TableLine> lines)
    {
        var span = ChunkingUtilities.TrimmedSpan(content, start, end);
        if (span is null)
        {
            return;
        }

        lines.Add(new TableLine(content[span.Start..span.End]));
    }

    private static string BuildTableChunk(
        IReadOnlyList<TableLine> header,
        IReadOnlyList<TableLine> rows)
    {
        return string.Join("\n", header.Concat(rows).Select(line => line.Text));
    }

    private sealed record TableLine(string Text);
}
