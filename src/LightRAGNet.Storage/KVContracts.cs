namespace LightRAGNet.Storage;

public static class KVContracts
{
    public const string TextChunks = "text_chunks";

    public const string FullDocs = "full_docs";

    public const string FullEntities = "full_entities";

    public const string FullRelations = "full_relations";

    public const string EntityChunks = "entity_chunks";

    public const string RelationChunks = "relation_chunks";

    public const string LLMCache = "llm_cache";

    public static IEnumerable<string> GetKVStoreNames()
    {
        yield return TextChunks;
        yield return FullDocs;
        yield return FullEntities;
        yield return FullRelations;
        yield return EntityChunks;
        yield return RelationChunks;
        yield return LLMCache;
    }
}