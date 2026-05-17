using MudBlazor;

namespace LightRAGNet.Web.Extensions;

/// <summary>
/// Formatting-related extension methods
/// </summary>
public static class FormatExtensions
{
    /// <summary>
    /// Format bytes to readable file size string
    /// </summary>
    /// <param name="bytes">Number of bytes</param>
    /// <returns>Formatted file size string (e.g., 1.5 MB)</returns>
    public static string FormatFileSize(this long bytes)
    {
        if (bytes < 0) return "0 B";
        
        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        var order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Get display text for task stage
    /// </summary>
    /// <param name="stage">Task stage string</param>
    /// <returns>Stage display text</returns>
    public static string GetTaskStageText(this string? stage)
    {
        if (string.IsNullOrEmpty(stage))
            return string.Empty;
        
        return stage switch
        {
            "DocumentChunking" => "Document Chunking",
            "ProcessingChunks" => "Processing Chunks",
            "StoringTextChunks" => "Storing Text Chunks",
            "StoringChunkVectors" => "Storing Chunk Vectors",
            "MergingEntities" => "Merging Entities",
            "MergingRelations" => "Merging Relations",
            "UpdatingStorage" => "Updating Storage",
            "StoringFullDocument" => "Storing Full Document",
            "Persisting" => "Persisting",
            "Completed" => "Completed",
            _ => stage
        };
    }

    /// <summary>
    /// Get display text for RAG status (if specific stage is provided, show stage description)
    /// </summary>
    /// <param name="status">RAG status string</param>
    /// <param name="currentStage">Current task stage</param>
    /// <returns>Status display text</returns>
    public static string GetRagStatusText(this string? status, string? currentStage = null)
    {
        if (string.IsNullOrEmpty(status))
            return "Not Added";
        
        // If status is Processing and has specific stage, show stage description
        if (status == "Processing" && !string.IsNullOrEmpty(currentStage))
        {
            return currentStage.GetTaskStageText();
        }
        
        return status switch
        {
            "Pending" => "Pending",
            "Processing" => "Processing",
            "Completed" => "Completed",
            "Failed" => "Failed",
            _ => status
        };
    }

    /// <summary>
    /// Get color corresponding to RAG status
    /// </summary>
    /// <param name="status">RAG status string</param>
    /// <returns>Color corresponding to status</returns>
    public static Color GetRagStatusColor(this string? status)
    {
        if (string.IsNullOrEmpty(status))
            return Color.Default;
        
        return status switch
        {
            "Completed" => Color.Success,
            "Processing" => Color.Info,
            "Failed" => Color.Error,
            _ => Color.Default
        };
    }

    /// <summary>
    /// Get icon corresponding to RAG status
    /// </summary>
    /// <param name="status">RAG status string</param>
    /// <returns>Icon corresponding to status</returns>
    public static string GetRagStatusIcon(this string? status)
    {
        if (string.IsNullOrEmpty(status))
            return Icons.Material.Filled.Pending;
        
        return status switch
        {
            "Completed" => Icons.Material.Filled.CheckCircle,
            "Processing" => Icons.Material.Filled.HourglassEmpty,
            "Failed" => Icons.Material.Filled.Error,
            _ => Icons.Material.Filled.Pending
        };
    }
}
