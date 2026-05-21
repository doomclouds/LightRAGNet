using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Web.Models;

public sealed class ChatQuerySettingsModel
{
    public QueryMode SelectedMode { get; set; } = QueryMode.Mix;
    public bool StreamResponse { get; set; } = true;
    public bool IncludeReferences { get; set; } = true;
    public bool EnableRerank { get; set; } = true;
    public int TopK { get; set; } = 40;
    public int ChunkTopK { get; set; } = 20;
    public string ResponseType { get; set; } = "Multiple Paragraphs";
    public string HighLevelKeywordsText { get; set; } = string.Empty;
    public string LowLevelKeywordsText { get; set; } = string.Empty;
    public ChatQueryDebugOutputMode DebugOutputMode { get; set; } = ChatQueryDebugOutputMode.Answer;

    public bool IsBypassMode => SelectedMode == QueryMode.Bypass;
    public bool AreRagOptionsEnabled => !IsBypassMode;
    public bool EffectiveIncludeReferences
    {
        get => !IsBypassMode && IncludeReferences;
        set => IncludeReferences = value;
    }

    public RagQueryRequest BuildRequest(string query)
    {
        return new RagQueryRequest
        {
            Query = query,
            Mode = SelectedMode,
            Stream = StreamResponse,
            IncludeReferences = EffectiveIncludeReferences,
            ResponseType = ResponseType,
            TopK = TopK,
            ChunkTopK = ChunkTopK,
            EnableRerank = EnableRerank,
            HighLevelKeywords = ParseKeywords(HighLevelKeywordsText),
            LowLevelKeywords = ParseKeywords(LowLevelKeywordsText),
            OnlyNeedContext = DebugOutputMode == ChatQueryDebugOutputMode.ContextOnly,
            OnlyNeedPrompt = DebugOutputMode == ChatQueryDebugOutputMode.PromptOnly
        };
    }

    public static RagQueryRequest CloneRequest(RagQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RagQueryRequest
        {
            Query = request.Query,
            Mode = request.Mode,
            Stream = request.Stream,
            IncludeReferences = request.IncludeReferences,
            ResponseType = request.ResponseType,
            TopK = request.TopK,
            ChunkTopK = request.ChunkTopK,
            EnableRerank = request.EnableRerank,
            HighLevelKeywords = [.. request.HighLevelKeywords],
            LowLevelKeywords = [.. request.LowLevelKeywords],
            OnlyNeedContext = request.OnlyNeedContext,
            OnlyNeedPrompt = request.OnlyNeedPrompt
        };
    }

    public static List<string> ParseKeywords(string value)
    {
        return value
            .Split([',', '，', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void ApplyMetadata(ChatMessageModel message, QueryMetadataEvent metadataEvent)
    {
        message.Mode = metadataEvent.Mode;
        message.IsStreaming = metadataEvent.Stream;
        message.IsCacheable = !metadataEvent.Stream;
        message.References = metadataEvent.IncludeReferences ? [.. metadataEvent.References] : [];
        message.HighLevelKeywords = [.. metadataEvent.HighLevelKeywords];
        message.LowLevelKeywords = [.. metadataEvent.LowLevelKeywords];
        message.Diagnostics = metadataEvent.Diagnostics.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}
