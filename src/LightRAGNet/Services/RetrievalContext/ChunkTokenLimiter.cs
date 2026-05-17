using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.RetrievalContext;

internal sealed class ChunkTokenLimiter(ITokenizer tokenizer)
{
    public List<ChunkData> Limit(IEnumerable<ChunkData> chunks, int maxTokens)
    {
        var result = new List<ChunkData>();
        var currentTokens = 0;

        foreach (var chunk in chunks)
        {
            var fileName = ReferenceListBuilder.ExtractFileName(chunk.FilePath);
            var chunkText = $"[{fileName}]\n{chunk.Content}";
            var chunkTokens = tokenizer.CountTokens(chunkText);
            if (currentTokens + chunkTokens > maxTokens)
            {
                break;
            }

            result.Add(chunk);
            currentTokens += chunkTokens;
        }

        return result;
    }
}
