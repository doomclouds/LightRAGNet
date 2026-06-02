using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.DocumentProcessing.Chunking;

public sealed class RecursiveCharacterChunkingStrategy : IChunkingStrategy
{
    public LightRagChunkingStrategy Strategy => LightRagChunkingStrategy.RecursiveCharacter;

    public Task<IReadOnlyList<ChunkingSegment>> ChunkAsync(
        ChunkingRequest request,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Task.FromResult<IReadOnlyList<ChunkingSegment>>([]);
        }

        var options = Normalize(request.Options.RecursiveCharacter);
        var pieces = SplitRecursive(
            request.Content,
            0,
            request.Content.Length,
            options.Separators,
            separatorIndex: 0,
            options.ChunkTokenSize,
            options.ChunkOverlapTokenSize,
            tokenizer,
            cancellationToken);
        var chunks = MergePieces(
            request.Content,
            pieces,
            options.ChunkTokenSize,
            options.ChunkOverlapTokenSize,
            tokenizer);

        return Task.FromResult<IReadOnlyList<ChunkingSegment>>(chunks);
    }

    private static RecursiveCharacterChunkingSnapshot Normalize(RecursiveCharacterChunkingSnapshot options)
    {
        var chunkSize = ChunkingUtilities.RequirePositiveChunkSize(
            options.ChunkTokenSize,
            "Chunking:RecursiveCharacter:ChunkTokenSize");
        var overlap = ChunkingUtilities.ClampOverlap(chunkSize, options.ChunkOverlapTokenSize);
        var separators = options.Separators is { Count: > 0 }
            ? options.Separators
            : RecursiveCharacterChunkingOptions.CreateDefaultSeparators();

        return new RecursiveCharacterChunkingSnapshot(chunkSize, overlap, [.. separators]);
    }

    private static List<Piece> SplitRecursive(
        string source,
        int start,
        int end,
        IReadOnlyList<string> separators,
        int separatorIndex,
        int chunkSize,
        int overlap,
        ITokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var span = ChunkingUtilities.TrimmedSpan(source, start, end);
        if (span is null)
        {
            return [];
        }

        var content = source[span.Start..span.End];
        var tokens = ChunkingUtilities.CountTokens(tokenizer, content);
        if (tokens <= chunkSize)
        {
            return [Piece.FromSource(content, tokens, span.Start, span.End)];
        }

        var selectedIndex = SelectSeparatorIndex(content, separators, separatorIndex);
        if (selectedIndex < 0 || string.IsNullOrEmpty(separators[selectedIndex]))
        {
            return HardSplit(content, chunkSize, overlap, tokenizer);
        }

        var separator = separators[selectedIndex];
        var pieces = new List<Piece>();
        var segmentStart = span.Start;
        var cursor = span.Start;
        var segmentIndex = 0;

        while (cursor <= span.End)
        {
            var separatorStart = source.IndexOf(
                separator,
                cursor,
                span.End - cursor,
                StringComparison.Ordinal);

            if (separatorStart < 0)
            {
                AddSplitSegment(
                    source,
                    segmentStart,
                    span.End,
                    separators,
                    selectedIndex,
                    chunkSize,
                    overlap,
                    tokenizer,
                    cancellationToken,
                    pieces,
                    ShouldStartNewChunk(selectedIndex, segmentIndex));
                break;
            }

            AddSplitSegment(
                source,
                segmentStart,
                separatorStart,
                separators,
                selectedIndex,
                chunkSize,
                overlap,
                tokenizer,
                cancellationToken,
                pieces,
                ShouldStartNewChunk(selectedIndex, segmentIndex));

            segmentStart = separatorStart + separator.Length;
            cursor = segmentStart;
            segmentIndex++;
        }

        return pieces.Count == 0
            ? HardSplit(content, chunkSize, overlap, tokenizer)
            : pieces;
    }

    private static int SelectSeparatorIndex(
        string content,
        IReadOnlyList<string> separators,
        int separatorIndex)
    {
        for (var index = separatorIndex; index < separators.Count; index++)
        {
            var separator = separators[index];
            if (string.IsNullOrEmpty(separator) ||
                content.Contains(separator, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static void AddSplitSegment(
        string source,
        int start,
        int end,
        IReadOnlyList<string> separators,
        int selectedIndex,
        int chunkSize,
        int overlap,
        ITokenizer tokenizer,
        CancellationToken cancellationToken,
        List<Piece> pieces,
        bool forceNewChunkBefore)
    {
        var span = ChunkingUtilities.TrimmedSpan(source, start, end);
        if (span is null)
        {
            return;
        }

        var content = source[span.Start..span.End];
        var tokens = ChunkingUtilities.CountTokens(tokenizer, content);
        if (tokens <= chunkSize)
        {
            pieces.Add(Piece.FromSource(content, tokens, span.Start, span.End, forceNewChunkBefore));
            return;
        }

        var nestedPieces = SplitRecursive(
            source,
            span.Start,
            span.End,
            separators,
            selectedIndex + 1,
            chunkSize,
            overlap,
            tokenizer,
            cancellationToken);
        if (forceNewChunkBefore && nestedPieces.Count > 0)
        {
            nestedPieces[0] = nestedPieces[0] with { ForceNewChunkBefore = true };
        }

        pieces.AddRange(nestedPieces);
    }

    private static bool ShouldStartNewChunk(int selectedIndex, int segmentIndex) =>
        selectedIndex == 0 && segmentIndex > 0;

    private static List<Piece> HardSplit(
        string content,
        int chunkSize,
        int overlap,
        ITokenizer tokenizer)
    {
        var tokens = tokenizer.Encode(content);
        if (tokens.Count == 0)
        {
            return [];
        }

        var pieces = new List<Piece>();
        var stepSize = chunkSize - overlap;

        for (var start = 0; start < tokens.Count; start += stepSize)
        {
            var remainingTokens = tokens.Count - start;
            if (remainingTokens <= overlap && pieces.Count > 0)
            {
                break;
            }

            var count = Math.Min(chunkSize, remainingTokens);
            var chunkTokens = tokens.Skip(start).Take(count).ToList();
            if (chunkTokens.Count == 0)
            {
                break;
            }

            var chunkContent = tokenizer.Decode(chunkTokens).Trim();
            if (chunkContent.Length == 0)
            {
                continue;
            }

            pieces.Add(Piece.FromContent(chunkContent, chunkTokens.Count));
        }

        return pieces;
    }

    private static List<ChunkingSegment> MergePieces(
        string source,
        IReadOnlyList<Piece> pieces,
        int chunkSize,
        int overlap,
        ITokenizer tokenizer)
    {
        var chunks = new List<ChunkingSegment>();
        var current = new List<Piece>();

        foreach (var piece in pieces)
        {
            if (current.Count == 0)
            {
                current.Add(piece);
                continue;
            }

            if (piece.ForceNewChunkBefore)
            {
                AddChunk(source, chunks, current, tokenizer);
                current = [piece];
                continue;
            }

            var candidate = current.Concat([piece]).ToList();
            if (CountMergedTokens(source, candidate, tokenizer) <= chunkSize)
            {
                current.Add(piece);
                continue;
            }

            AddChunk(source, chunks, current, tokenizer);
            current = CreateNextWindow(source, current, piece, overlap, chunkSize, tokenizer);
        }

        if (current.Count > 0)
        {
            AddChunk(source, chunks, current, tokenizer);
        }

        return chunks;
    }

    private static List<Piece> CreateNextWindow(
        string source,
        IReadOnlyList<Piece> previous,
        Piece next,
        int overlap,
        int chunkSize,
        ITokenizer tokenizer)
    {
        var window = new List<Piece>();
        var overlapTokens = 0;

        for (var index = previous.Count - 1; index >= 0 && overlapTokens < overlap; index--)
        {
            window.Insert(0, previous[index]);
            overlapTokens += previous[index].Tokens;
        }

        window.Add(next);
        while (window.Count > 1 && CountMergedTokens(source, window, tokenizer) > chunkSize)
        {
            window.RemoveAt(0);
        }

        return window;
    }

    private static void AddChunk(
        string source,
        List<ChunkingSegment> chunks,
        IReadOnlyList<Piece> pieces,
        ITokenizer tokenizer)
    {
        var content = BuildMergedContent(source, pieces);
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var span = BuildMergedSpan(pieces);
        chunks.Add(new ChunkingSegment
        {
            Content = content,
            Tokens = ChunkingUtilities.CountTokens(tokenizer, content),
            Order = chunks.Count,
            Strategy = LightRagChunkingStrategy.RecursiveCharacter,
            SourceSpan = span
        });
    }

    private static int CountMergedTokens(
        string source,
        IReadOnlyList<Piece> pieces,
        ITokenizer tokenizer)
    {
        return ChunkingUtilities.CountTokens(tokenizer, BuildMergedContent(source, pieces));
    }

    private static string BuildMergedContent(string source, IReadOnlyList<Piece> pieces)
    {
        var span = BuildMergedSpan(pieces);
        if (span is not null)
        {
            return source[span.Start..span.End];
        }

        return string.Join(" ", pieces.Select(piece => piece.Content)).Trim();
    }

    private static SourceSpan? BuildMergedSpan(IReadOnlyList<Piece> pieces)
    {
        if (pieces.Count == 0 || pieces.Any(piece => piece.SourceSpan is null))
        {
            return null;
        }

        return new SourceSpan(pieces[0].SourceSpan!.Start, pieces[^1].SourceSpan!.End);
    }

    private sealed record Piece(
        string Content,
        int Tokens,
        SourceSpan? SourceSpan,
        bool ForceNewChunkBefore)
    {
        public static Piece FromSource(
            string content,
            int tokens,
            int start,
            int end,
            bool forceNewChunkBefore = false) =>
            new(content, tokens, new SourceSpan(start, end), forceNewChunkBefore);

        public static Piece FromContent(string content, int tokens) =>
            new(content, tokens, null, false);
    }
}
