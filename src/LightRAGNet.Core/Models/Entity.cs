namespace LightRAGNet.Core.Models;

public class Entity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long Timestamp { get; set; }
}

public class Relationship
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public float Weight { get; set; } = 1.0f;
    public string SourceChunkId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long Timestamp { get; set; }
}

public class EntityExtractionResult
{
    public List<Entity> Entities { get; set; } = [];
    public List<Relationship> Relationships { get; set; } = [];
}

public class KeywordsResult
{
    public List<string> HighLevelKeywords { get; set; } = [];
    public List<string> LowLevelKeywords { get; set; } = [];
}

