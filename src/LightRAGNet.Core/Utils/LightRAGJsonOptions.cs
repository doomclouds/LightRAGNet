using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightRAGNet.Core.Utils;

public static class LightRAGJsonOptions
{
    public static readonly JsonSerializerOptions HumanReadable = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static readonly JsonSerializerOptions HumanReadableIndented = new(HumanReadable)
    {
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions HumanReadableCamelCase = new(HumanReadable)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static readonly JsonSerializerOptions HumanReadableCamelCaseWithStringEnums = new(HumanReadableCamelCase)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static readonly JsonSerializerOptions Compact = new(HumanReadable)
    {
        WriteIndented = false
    };

    public static readonly JsonSerializerOptions HumanReadableCamelCaseIndented = new(HumanReadableCamelCase)
    {
        WriteIndented = true
    };
}
