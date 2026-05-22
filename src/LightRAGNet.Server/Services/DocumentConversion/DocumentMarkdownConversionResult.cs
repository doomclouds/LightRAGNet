namespace LightRAGNet.Server.Services.DocumentConversion;

public sealed record DocumentMarkdownConversionResult(
    string Markdown,
    string? DetectedMediaType = null,
    IReadOnlyList<string>? Warnings = null);
