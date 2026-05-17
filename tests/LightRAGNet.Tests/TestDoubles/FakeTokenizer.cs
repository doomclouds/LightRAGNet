using LightRAGNet.Core.Utils;

namespace LightRAGNet.Tests.TestDoubles;

internal sealed class FakeTokenizer : ITokenizer
{
    public List<int> Encode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var tokenCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Enumerable.Range(1, tokenCount).ToList();
    }

    public string Decode(List<int> tokens)
    {
        return string.Join(" ", tokens.Select(token => $"t{token}"));
    }

    public int CountTokens(string text)
    {
        return Encode(text).Count;
    }
}
