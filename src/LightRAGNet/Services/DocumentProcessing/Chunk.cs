using LightRAGNet.Core.Models;

namespace LightRAGNet.Services.DocumentProcessing;

public class Chunk
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Tokens { get; set; }
    public int ChunkOrderIndex { get; set; }
    public string FullDocId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public class ChunkResult
{
    public string ChunkId { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = [];
    public List<Entity> Entities { get; set; } = [];
    public List<Relationship> Relationships { get; set; } = [];
}

