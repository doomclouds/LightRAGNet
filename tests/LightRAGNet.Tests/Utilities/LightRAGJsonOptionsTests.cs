using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Core.Utils;

namespace LightRAGNet.Tests.Utilities;

public sealed class LightRAGJsonOptionsTests
{
    [Fact]
    public void HumanReadable_SerializesChineseWithoutUnicodeEscapes()
    {
        var json = JsonSerializer.Serialize(
            new { Keywords = new[] { "采集流程", "100字" } },
            LightRAGJsonOptions.HumanReadable);

        json.Should().Contain("采集流程");
        json.Should().Contain("100字");
        json.Should().NotContain("\\u91C7");
    }

    [Fact]
    public void HumanReadableCamelCase_UsesCamelCaseAndSerializesChineseWithoutUnicodeEscapes()
    {
        var json = JsonSerializer.Serialize(
            new { HighLevelKeywords = new[] { "线性修正" } },
            LightRAGJsonOptions.HumanReadableCamelCase);

        json.Should().Contain("highLevelKeywords");
        json.Should().Contain("线性修正");
        json.Should().NotContain("\\u7EBF");
    }
}
