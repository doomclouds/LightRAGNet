using LightRAGNet.Server.Models;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Extensions;

public static class MarkdownModelMapper
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
            RagProgress = model.RagProgress,
            RagCurrentStage = null, // Current stage information is obtained from task status updates
            RagErrorMessage = model.RagErrorMessage,
            RagDocumentId = model.RagDocumentId,
            FileUrl = model.FileUrl,
            FileHash = model.FileHash,
            Content = model.Content
        };
    }
}