using System.Text.RegularExpressions;
using LightRAGNet.Server.Models;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Extensions;

public static partial class MarkdownModelMapper
{
    
    public static MarkdownDocumentDto ToDto(this MarkdownDocument model)
    {
        return new MarkdownDocumentDto
        {
            Id = model.Id,
            FileName = model.FileName,
            FileSize = model.FileSize,
            UploadTime = model.UploadTime,
            LastModified = model.LastModified,
            IsInRagSystem = model.IsInRagSystem,
            RagAddedTime = model.RagAddedTime,
            RagStatus = model.RagStatus,
            TrackId = model.TrackId,
            RagProgress = model.RagProgress,
            RagCurrentStage = model.RagCurrentStage,
            ActiveRagTaskId = model.ActiveRagTaskId,
            PipelineStartedAt = model.PipelineStartedAt,
            PipelineCompletedAt = model.PipelineCompletedAt,
            PipelineCancelledAt = model.PipelineCancelledAt,
            RagRetryCount = model.RagRetryCount,
            RagErrorMessage = model.RagErrorMessage,
            RagDocumentId = model.RagDocumentId,
            FileUrl = model.FileUrl,
            FileHash = model.FileHash,
            OriginalFileName = model.OriginalFileName,
            OriginalContentType = model.OriginalContentType,
            OriginalContentHash = model.OriginalContentHash,
            ConvertedMarkdownHash = model.ConvertedMarkdownHash,
            ConversionStatus = model.ConversionStatus,
            ConversionErrorMessage = SanitizeConversionErrorMessage(model.ConversionErrorMessage),
            ConversionStartedAt = model.ConversionStartedAt,
            ConversionCompletedAt = model.ConversionCompletedAt,
            ConversionTool = model.ConversionTool,
            ConversionToolVersion = model.ConversionToolVersion,
            Content = model.Content
        };
    }

    private static string? SanitizeConversionErrorMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        var sanitized = WindowsPathPattern().Replace(message, "[path]");
        sanitized = ArtifactPathPattern().Replace(sanitized, "[path]");
        return sanitized;
    }

    [GeneratedRegex(@"[A-Za-z]:\\[^\s""'<>),;]+")]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"(?<![\w/\\.-])documents/[^\s""'<>),;]+")]
    private static partial Regex ArtifactPathPattern();
}
