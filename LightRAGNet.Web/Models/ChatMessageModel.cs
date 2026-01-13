namespace LightRAGNet.Web.Models;

/// <summary>
/// Chat message model for UI display
/// </summary>
public class ChatMessageModel
{
    /// <summary>
    /// Message role (User or Assistant)
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Message text content
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
