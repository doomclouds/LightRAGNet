namespace LightRAGNet.Server.Services.DocumentArtifacts;

public sealed class DocumentArtifactStoreOptions
{
    public string RootPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rag_storage");
}
