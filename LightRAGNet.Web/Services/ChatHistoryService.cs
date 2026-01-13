using LightRAGNet.Web.Models;

namespace LightRAGNet.Web.Services;

/// <summary>
/// Chat history service - stores chat messages in memory (singleton)
/// </summary>
public class ChatHistoryService
{
    private readonly List<ChatMessageModel> _chatHistory = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Get all chat messages
    /// </summary>
    public List<ChatMessageModel> GetChatHistory()
    {
        lock (_lock)
        {
            return [.._chatHistory];
        }
    }

    /// <summary>
    /// Add a chat message
    /// </summary>
    public void AddMessage(ChatMessageModel message)
    {
        lock (_lock)
        {
            _chatHistory.Add(message);
        }
    }

    /// <summary>
    /// Clear all chat messages
    /// </summary>
    public void ClearHistory()
    {
        lock (_lock)
        {
            _chatHistory.Clear();
        }
    }
}
