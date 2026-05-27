using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.Evaluation;

internal sealed class RagasEvaluationTextSnapshotter(IOptions<RagasEvaluationOptions> options)
{
    public RagasEvaluationOperationResult<object> ValidateFullTextRequest(bool includeFullText)
    {
        var value = options.Value;
        if (includeFullText && !value.AllowPersistFullText)
        {
            return RagasEvaluationOperationResult<object>.Fail(
                "full_text_disabled",
                "Full-text persistence is disabled by Evaluation:Ragas:AllowPersistFullText.",
                StatusCodes.Status400BadRequest);
        }

        return RagasEvaluationOperationResult<object>.Ok(new object());
    }

    public RagasTextSnapshot Snapshot(string text, bool includeFullText)
    {
        ArgumentNullException.ThrowIfNull(text);

        var value = options.Value;
        var previewLength = Math.Max(0, value.PreviewMaxChars);
        var preview = text.Length <= previewLength ? text : text[..previewLength];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var persistedText = includeFullText && value.AllowPersistFullText ? text : null;

        return new RagasTextSnapshot(preview, hash, persistedText);
    }
}
