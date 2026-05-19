using LightRAGNet.Core.Models;

namespace LightRAGNet.Services.Query;

internal static class NaiveQueryPromptBuilder
{
    public static string BuildResponsePrompt(QueryContextResult contextResult, QueryParam queryParam)
    {
        return BuildPrompt(queryParam, contextResult.Context);
    }

    public static string BuildPromptOverhead(QueryParam queryParam)
    {
        return BuildPrompt(queryParam, string.Empty);
    }

    private static string BuildPrompt(QueryParam queryParam, string context)
    {
        var responseType = string.IsNullOrEmpty(queryParam.ResponseType)
            ? "Multiple Paragraphs"
            : queryParam.ResponseType;
        var userPrompt = queryParam.UserPrompt ?? "n/a";

        return $"""
                ---Role---

                You are an expert AI assistant answering from retrieved document chunks.

                ---Goal---

                Generate a {responseType} answer using only the provided document chunks.

                ---Instructions---

                - Use only the information in the context.
                - If the answer is not present in the context, say that there is not enough information.
                - Answer in the same language as the user query.
                - Additional instructions: {userPrompt}

                ---Context---

                {context}
                """;
    }
}
