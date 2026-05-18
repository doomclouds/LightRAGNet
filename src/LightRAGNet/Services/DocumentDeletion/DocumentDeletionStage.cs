namespace LightRAGNet.Services.DocumentDeletion;

public static class DocumentDeletionStage
{
    public const string PrepareDeletion = "prepare_deletion";
    public const string CollectChunks = "collect_chunks";
    public const string CollectLlmCache = "collect_llm_cache";
    public const string AnalyzeGraphReferences = "analyze_graph_references";
    public const string DeleteChunkVectors = "delete_chunk_vectors";
    public const string DeleteTextChunks = "delete_text_chunks";
    public const string DeleteGraphRelations = "delete_graph_relations";
    public const string DeleteGraphEntities = "delete_graph_entities";
    public const string UpdateGraphReferences = "update_graph_references";
    public const string DeleteRelationVectors = "delete_relation_vectors";
    public const string DeleteEntityVectors = "delete_entity_vectors";
    public const string UpdateRelationVectors = "update_relation_vectors";
    public const string UpdateEntityVectors = "update_entity_vectors";
    public const string DeleteRelationTracking = "delete_relation_tracking";
    public const string DeleteEntityTracking = "delete_entity_tracking";
    public const string DeleteLlmCache = "delete_llm_cache";
    public const string DeleteDocumentMetadata = "delete_document_metadata";
    public const string DeleteDocStatus = "delete_doc_status";
    public const string DeleteMarkdownRecord = "delete_markdown_record";
    public const string DeleteUploadedFile = "delete_uploaded_file";
}
