namespace LightRAGNet.Services.DocumentDeletion;

public static class GraphSourceReferenceParser
{
    public const string GraphFieldSep = "<SEP>";

    public static IReadOnlyList<string> Split(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return [];
        }

        var sourceIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in sourceId.Split(GraphFieldSep, StringSplitOptions.None))
        {
            var trimmedValue = value.Trim();
            if (trimmedValue.Length == 0 || !seen.Add(trimmedValue))
            {
                continue;
            }

            sourceIds.Add(trimmedValue);
        }

        return sourceIds;
    }

    public static IReadOnlyList<string> Prune(string? sourceId, ISet<string> deletedChunkIds)
    {
        return Split(sourceId)
            .Where(id => !deletedChunkIds.Contains(id))
            .ToList();
    }

    public static string Join(IEnumerable<string> sourceIds)
    {
        return string.Join(GraphFieldSep, sourceIds
            .Select(id => id.Trim())
            .Where(id => id.Length > 0));
    }

    public static string MakeRelationKey(string sourceId, string targetId)
    {
        return string.Compare(sourceId, targetId, StringComparison.Ordinal) <= 0
            ? Join([sourceId, targetId])
            : Join([targetId, sourceId]);
    }
}
