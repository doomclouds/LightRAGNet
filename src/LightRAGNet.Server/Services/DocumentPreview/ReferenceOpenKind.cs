namespace LightRAGNet.Server.Services.DocumentPreview;

public static class ReferenceOpenKind
{
    public const string DocumentPreview = nameof(DocumentPreview);
    public const string ConvertedMarkdown = nameof(ConvertedMarkdown);
    public const string OriginalArtifact = nameof(OriginalArtifact);
    public const string UploadedFile = nameof(UploadedFile);
    public const string ExternalOrUnresolved = nameof(ExternalOrUnresolved);
}
