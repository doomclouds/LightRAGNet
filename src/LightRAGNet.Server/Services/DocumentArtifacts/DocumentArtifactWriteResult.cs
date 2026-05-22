namespace LightRAGNet.Server.Services.DocumentArtifacts;

public sealed record DocumentArtifactWriteResult(string AbsolutePath, string RelativePath, string Hash, long Size);
