using LightRAGNet.Core.Models;

namespace LightRAGNet.Example;

public sealed record QueryCommandOptions(string Question, QueryParam QueryParam)
{
    public static QueryCommandOptions Parse(string input)
    {
        var tokens = SplitCommandLine(input);
        var questionParts = new List<string>();
        var queryParam = CreateDefaultQueryParam();

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                questionParts.Add(token);
                continue;
            }

            switch (token.ToLowerInvariant())
            {
                case "--mode":
                    queryParam.Mode = ReadEnum<QueryMode>(tokens, ref index, token);
                    break;
                case "--stream":
                    queryParam.Stream = ReadBool(tokens, ref index, token);
                    break;
                case "--no-stream":
                    queryParam.Stream = false;
                    break;
                case "--references":
                case "--include-references":
                    queryParam.IncludeReferences = ReadBool(tokens, ref index, token);
                    break;
                case "--no-references":
                    queryParam.IncludeReferences = false;
                    break;
                case "--response":
                case "--response-type":
                    queryParam.ResponseType = ReadValue(tokens, ref index, token);
                    break;
                case "--top-k":
                case "--topk":
                    queryParam.TopK = ReadPositiveInt(tokens, ref index, token);
                    break;
                case "--chunk-top-k":
                case "--chunktopk":
                    queryParam.ChunkTopK = ReadPositiveInt(tokens, ref index, token);
                    break;
                case "--rerank":
                    queryParam.EnableRerank = ReadBool(tokens, ref index, token);
                    break;
                case "--no-rerank":
                    queryParam.EnableRerank = false;
                    break;
                case "--hl":
                case "--high-keywords":
                    queryParam.HighLevelKeywords = ParseKeywords(ReadValue(tokens, ref index, token));
                    break;
                case "--ll":
                case "--low-keywords":
                    queryParam.LowLevelKeywords = ParseKeywords(ReadValue(tokens, ref index, token));
                    break;
                case "--context-only":
                    queryParam.OnlyNeedContext = true;
                    queryParam.OnlyNeedPrompt = false;
                    queryParam.Stream = false;
                    break;
                case "--prompt-only":
                    queryParam.OnlyNeedPrompt = true;
                    queryParam.OnlyNeedContext = false;
                    queryParam.Stream = false;
                    break;
                default:
                    throw new ArgumentException($"Unknown query option: {token}");
            }
        }

        return new QueryCommandOptions(string.Join(' ', questionParts).Trim(), queryParam);
    }

    public static QueryParam CreateDefaultQueryParam()
    {
        return new QueryParam
        {
            Mode = QueryMode.Mix,
            TopK = 10,
            ChunkTopK = 5,
            EnableRerank = true,
            IncludeReferences = true,
            Stream = true
        };
    }

    public string FormatSummary()
    {
        var hl = QueryParam.HighLevelKeywords.Count == 0
            ? "-"
            : string.Join(", ", QueryParam.HighLevelKeywords);
        var ll = QueryParam.LowLevelKeywords.Count == 0
            ? "-"
            : string.Join(", ", QueryParam.LowLevelKeywords);

        return $"Mode={QueryParam.Mode}, Stream={QueryParam.Stream}, References={QueryParam.IncludeReferences}, " +
               $"ResponseType={QueryParam.ResponseType}, TopK={QueryParam.TopK}, ChunkTopK={QueryParam.ChunkTopK}, " +
               $"Rerank={QueryParam.EnableRerank}, ContextOnly={QueryParam.OnlyNeedContext}, PromptOnly={QueryParam.OnlyNeedPrompt}, " +
               $"HL=[{hl}], LL=[{ll}]";
    }

    private static List<string> SplitCommandLine(string input)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        var quote = '\0';

        foreach (var character in input)
        {
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Add(character);
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                AddCurrentToken(tokens, current);
                continue;
            }

            current.Add(character);
        }

        if (quote != '\0')
        {
            throw new ArgumentException("Unterminated quote in query command.");
        }

        AddCurrentToken(tokens, current);
        return tokens;
    }

    private static void AddCurrentToken(ICollection<string> tokens, List<char> current)
    {
        if (current.Count == 0)
        {
            return;
        }

        tokens.Add(new string([.. current]));
        current.Clear();
    }

    private static TEnum ReadEnum<TEnum>(IReadOnlyList<string> tokens, ref int index, string option)
        where TEnum : struct
    {
        var value = ReadValue(tokens, ref index, option);
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Invalid value '{value}' for {option}.");
    }

    private static bool ReadBool(IReadOnlyList<string> tokens, ref int index, string option)
    {
        var value = ReadValue(tokens, ref index, option);
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        if (value is "1" or "yes" or "on")
        {
            return true;
        }

        if (value is "0" or "no" or "off")
        {
            return false;
        }

        throw new ArgumentException($"Invalid boolean value '{value}' for {option}.");
    }

    private static int ReadPositiveInt(IReadOnlyList<string> tokens, ref int index, string option)
    {
        var value = ReadValue(tokens, ref index, option);
        if (int.TryParse(value, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        throw new ArgumentException($"Invalid positive integer value '{value}' for {option}.");
    }

    private static string ReadValue(IReadOnlyList<string> tokens, ref int index, string option)
    {
        if (index + 1 >= tokens.Count || tokens[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return tokens[index];
    }

    private static List<string> ParseKeywords(string value)
    {
        return value
            .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
