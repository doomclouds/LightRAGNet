using LightRAGNet.Core.Models;

namespace LightRAGNet.Services.RetrievalContext;

internal sealed class ReferenceListBuilder
{
    public (List<ReferenceItem> References, List<ChunkData> ChunksWithRefIds) Build(IEnumerable<ChunkData> chunks)
    {
        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
        {
            return ([], []);
        }

        var filePathCounts = new Dictionary<string, int>();
        foreach (var chunk in chunkList)
        {
            var filePath = chunk.FilePath;
            if (!string.IsNullOrEmpty(filePath) && filePath != "unknown_source")
            {
                filePathCounts[filePath] = filePathCounts.GetValueOrDefault(filePath, 0) + 1;
            }
        }

        var filePathWithIndices = new List<(string FilePath, int Count, int FirstIndex)>();
        var seenPaths = new HashSet<string>();
        for (var i = 0; i < chunkList.Count; i++)
        {
            var filePath = chunkList[i].FilePath;
            if (!string.IsNullOrEmpty(filePath) && filePath != "unknown_source" && seenPaths.Add(filePath))
            {
                filePathWithIndices.Add((filePath, filePathCounts[filePath], i));
            }
        }

        var sortedFilePaths = filePathWithIndices
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.FirstIndex)
            .Select(x => x.FilePath)
            .ToList();

        var filePathToRefId = new Dictionary<string, string>();
        for (var i = 0; i < sortedFilePaths.Count; i++)
        {
            filePathToRefId[sortedFilePaths[i]] = (i + 1).ToString();
        }

        var chunksWithRefIds = chunkList.Select(chunk =>
        {
            var filePath = chunk.FilePath;
            var referenceId =
                !string.IsNullOrEmpty(filePath)
                && filePath != "unknown_source"
                && filePathToRefId.TryGetValue(filePath, out var refId)
                    ? refId
                    : string.Empty;

            return new ChunkData
            {
                ChunkId = chunk.ChunkId,
                Content = chunk.Content,
                FilePath = chunk.FilePath,
                ReferenceId = referenceId
            };
        }).ToList();

        var references = sortedFilePaths
            .Select((filePath, i) => new ReferenceItem
            {
                ReferenceId = (i + 1).ToString(),
                FilePath = filePath
            })
            .ToList();

        return (references, chunksWithRefIds);
    }

    public static string ExtractFileName(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return "unknown";
        }

        if (filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(filePath);
                var fileName = Path.GetFileName(uri.AbsolutePath);

                if (!string.IsNullOrEmpty(fileName))
                {
                    return DecodeFileName(fileName);
                }

                return "unknown";
            }
            catch
            {
                var lastSlash = filePath.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash < filePath.Length - 1)
                {
                    return DecodeFileName(filePath[(lastSlash + 1)..]);
                }

                return "unknown";
            }
        }

        var fileNameFromPath = Path.GetFileName(filePath);
        return !string.IsNullOrEmpty(fileNameFromPath) ? fileNameFromPath : filePath;
    }

    private static string DecodeFileName(string fileName)
    {
        try
        {
            return Uri.UnescapeDataString(fileName);
        }
        catch
        {
            return fileName;
        }
    }
}
