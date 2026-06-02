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
    private static readonly Regex HeadingRegex = new(
        @"^\s{0,3}(#{1,6})[ \t]+(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex TableSeparatorCellRegex = new(
        @"^:?-+:?$",
        RegexOptions.Compiled);

    public static IReadOnlyList<DocumentBlock> Build(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var lines = ParseLines(content);
        var blocks = new List<DocumentBlock>();
        var headings = new List<(int Level, string Heading)>();
        var currentLevel = 0;
        var currentHeading = string.Empty;
        int? textStart = null;
        var textEnd = 0;

        void FlushText()
        {
            if (textStart is null)
            {
                return;
            }

            AddBlock(
                blocks,
                content,
                DocumentBlockKind.Text,
                textStart.Value,
                textEnd,
                currentLevel,
                currentHeading,
                headings);
            textStart = null;
            textEnd = 0;
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var heading = HeadingRegex.Match(line.Text);
            if (heading.Success)
            {
                FlushText();
                currentLevel = heading.Groups[1].Value.Length;
                currentHeading = CleanHeadingText(heading.Groups[2].Value);
                while (headings.Count > 0 && headings[^1].Level >= currentLevel)
                {
                    headings.RemoveAt(headings.Count - 1);
                }

                headings.Add((currentLevel, currentHeading));
                continue;
            }

            var fenceMarker = GetFenceMarker(line.Text);
            if (fenceMarker is not null)
            {
                FlushText();
                var endIndex = FindFenceEnd(lines, index + 1, fenceMarker);
                AddBlock(
                    blocks,
                    content,
                    DocumentBlockKind.Code,
                    line.Start,
                    lines[endIndex].End,
                    currentLevel,
                    currentHeading,
                    headings);
                index = endIndex;
                continue;
            }

            if (IsTableStart(lines, index))
            {
                FlushText();
                var endIndex = FindTableEnd(lines, index + 2);
                AddBlock(
                    blocks,
                    content,
                    DocumentBlockKind.Table,
                    line.Start,
                    lines[endIndex].End,
                    currentLevel,
                    currentHeading,
                    headings);
                index = endIndex;
                continue;
            }

            textStart ??= line.Start;
            textEnd = line.EndIncludingLineBreak;
        }

        FlushText();
        return blocks;
    }

    private static void AddBlock(
        ICollection<DocumentBlock> blocks,
        string content,
        DocumentBlockKind kind,
        int start,
        int end,
        int level,
        string heading,
        IReadOnlyList<(int Level, string Heading)> headings)
    {
        var span = ChunkingUtilities.TrimmedSpan(content, start, end);
        if (span is null)
        {
            return;
        }

        blocks.Add(new DocumentBlock
        {
            Content = content[span.Start..span.End],
            Level = level,
            Heading = heading,
            ParentHeadings = headings
                .Where(item => item.Level < level)
                .Select(item => item.Heading)
                .ToList(),
            Kind = kind,
            SourceSpan = span
        });
    }

    private static List<MarkdownLine> ParseLines(string content)
    {
        var lines = new List<MarkdownLine>();
        var start = 0;
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] is not ('\r' or '\n'))
            {
                continue;
            }

            var lineBreakEnd = index + 1;
            if (content[index] == '\r' && lineBreakEnd < content.Length && content[lineBreakEnd] == '\n')
            {
                lineBreakEnd++;
            }

            lines.Add(new MarkdownLine(content[start..index], start, index, lineBreakEnd));
            start = lineBreakEnd;
            index = lineBreakEnd - 1;
        }

        if (start < content.Length)
        {
            lines.Add(new MarkdownLine(content[start..], start, content.Length, content.Length));
        }

        return lines;
    }

    private static string? GetFenceMarker(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("```", StringComparison.Ordinal) ? "```" :
            trimmed.StartsWith("~~~", StringComparison.Ordinal) ? "~~~" :
            null;
    }

    private static string CleanHeadingText(string value)
    {
        var text = value.Trim();
        var markerStart = text.Length - 1;
        while (markerStart >= 0 && text[markerStart] == '#')
        {
            markerStart--;
        }

        if (markerStart < text.Length - 1 &&
            markerStart >= 0 &&
            char.IsWhiteSpace(text[markerStart]))
        {
            return text[..markerStart].TrimEnd();
        }

        return text;
    }

    private static int FindFenceEnd(IReadOnlyList<MarkdownLine> lines, int startIndex, string fenceMarker)
    {
        for (var index = startIndex; index < lines.Count; index++)
        {
            if (GetFenceMarker(lines[index].Text) == fenceMarker)
            {
                return index;
            }
        }

        return lines.Count - 1;
    }

    private static bool IsTableStart(IReadOnlyList<MarkdownLine> lines, int index)
    {
        return index + 1 < lines.Count &&
            IsTableRow(lines[index].Text) &&
            IsTableSeparator(lines[index + 1].Text);
    }

    private static int FindTableEnd(IReadOnlyList<MarkdownLine> lines, int startIndex)
    {
        var endIndex = startIndex - 1;
        for (var index = startIndex; index < lines.Count; index++)
        {
            if (!IsTableRow(lines[index].Text))
            {
                break;
            }

            endIndex = index;
        }

        return endIndex;
    }

    private static bool IsTableRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0 &&
            trimmed.IndexOf('|') >= 0 &&
            trimmed.StartsWith("|", StringComparison.Ordinal);
    }

    private static bool IsTableSeparator(string line)
    {
        var trimmed = line.Trim().Trim('|');
        if (trimmed.Length == 0)
        {
            return false;
        }

        return trimmed
            .Split('|')
            .Select(cell => cell.Trim())
            .All(cell => TableSeparatorCellRegex.IsMatch(cell));
    }

    private sealed record MarkdownLine(
        string Text,
        int Start,
        int End,
        int EndIncludingLineBreak);
}
