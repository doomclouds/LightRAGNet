using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Server.Controllers;
using LightRAGNet.Share.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Server.Tests;

public sealed class RagQueryControllerTests
{
    [Fact]
    public async Task QueryDataEndpoint_WhenBypassDebugRequest_ReturnsJsonSuccessWithoutExternalStorage()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/RagQuery/data", new RagQueryRequest
        {
            Query = "hello",
            Mode = QueryMode.Bypass,
            Stream = true,
            IncludeReferences = false,
            OnlyNeedContext = false,
            OnlyNeedPrompt = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Content.Headers.ContentType.Should().NotBeNull();
        var mediaType = response.Content.Headers.ContentType!.MediaType;
        mediaType.Should().NotBe("text/event-stream");
        IsJsonCompatibleMediaType(mediaType).Should().BeTrue($"media type '{mediaType}' should be JSON-compatible");

        var body = await response.Content.ReadFromJsonAsync<RagQueryDataResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("success");
        body.Data.Should().BeEmpty();
        AssertMetadataStringValue(body.Metadata, "query_mode", QueryMode.Bypass.ToString());
    }

    [Fact]
    public async Task QueryDataEndpoint_WhenBackendThrows_ReturnsGenericFailureMessage()
    {
        using var factory = new LightRagServerFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/RagQuery/data", new RagQueryRequest
        {
            Query = "force store failure",
            Mode = QueryMode.Naive
        });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var body = await response.Content.ReadFromJsonAsync<RagQueryDataResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("failure");
        body.Message.Should().Be("Error retrieving query data.");
        body.Message.Should().NotContain("Server tests must not use real external RAG storage");
    }

    [Fact]
    public async Task QueryDataAsync_WhenQueryIsBlank_ReturnsBadRequest()
    {
        var controller = new RagQueryController(null!, null!, NullLogger<RagQueryController>.Instance);

        var result = await controller.QueryDataAsync(new RagQueryRequest { Query = " " });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void SplitRawData_WhenRawDataContainsDataAndMetadata_ReturnsBothSections()
    {
        var rawData = new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>
            {
                ["chunks"] = 2,
                ["summary"] = "retrieved"
            },
            ["metadata"] = new Dictionary<string, object>
            {
                ["elapsed_ms"] = 42,
                ["cache_status"] = "live"
            }
        };

        var (data, metadata) = RagQueryController.SplitRawData(rawData);

        data.Should().ContainKey("chunks").WhoseValue.Should().Be(2);
        data.Should().ContainKey("summary").WhoseValue.Should().Be("retrieved");
        metadata.Should().ContainKey("elapsed_ms").WhoseValue.Should().Be(42);
        metadata.Should().ContainKey("cache_status").WhoseValue.Should().Be("live");
    }

    [Fact]
    public void SplitRawData_WhenRawDataIsNull_ReturnsEmptySections()
    {
        var result = RagQueryController.SplitRawData(null);

        result.Data.Should().BeEmpty();
        result.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void SplitRawData_WhenRawDataIsMissingDataAndMetadataKeys_ReturnsEmptySections()
    {
        var result = RagQueryController.SplitRawData(new Dictionary<string, object>
        {
            ["source"] = "query"
        });

        result.Data.Should().BeEmpty();
        result.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void SplitRawData_WhenRawDataContainsNonDictionarySections_ReturnsEmptySections()
    {
        var result = RagQueryController.SplitRawData(new Dictionary<string, object>
        {
            ["data"] = null!,
            ["metadata"] = "not a dictionary"
        });

        result.Data.Should().BeEmpty();
        result.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void RagQueryEventJsonOptions_SerializeMetadataForReactSseContract()
    {
        RagQueryEvent evt = new QueryMetadataEvent
        {
            Mode = QueryMode.Mix,
            Stream = true,
            IncludeReferences = true,
            ResponseType = "Multiple Paragraphs",
            CachePolicy = "Streaming request",
            References =
            [
                new RagQueryReferenceDto
                {
                    ReferenceId = "1",
                    FilePath = "/uploads/doc.md",
                    FileName = "doc.md",
                    PreviewUrl = "http://localhost/document-preview/1",
                    OpenKind = "DocumentPreview"
                }
            ],
            HighLevelKeywords = ["system"],
            LowLevelKeywords = ["queue"],
            Diagnostics = new Dictionary<string, string>
            {
                ["query_mode"] = "Mix"
            }
        };

        var json = JsonSerializer.Serialize(evt, LightRAGJsonOptions.HumanReadableCamelCaseWithStringEnums);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("type").GetString().Should().Be("metadata");
        root.GetProperty("mode").GetString().Should().Be("Mix");
        root.GetProperty("includeReferences").GetBoolean().Should().BeTrue();
        root.GetProperty("highLevelKeywords")[0].GetString().Should().Be("system");

        var reference = root.GetProperty("references")[0];
        reference.GetProperty("referenceId").GetString().Should().Be("1");
        reference.GetProperty("fileName").GetString().Should().Be("doc.md");
        reference.GetProperty("previewUrl").GetString().Should().Be("http://localhost/document-preview/1");
        reference.GetProperty("openKind").GetString().Should().Be("DocumentPreview");
    }

    private static bool IsJsonCompatibleMediaType(string? mediaType)
    {
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void AssertMetadataStringValue(
        IReadOnlyDictionary<string, object> metadata,
        string key,
        string expectedValue)
    {
        metadata.Should().ContainKey(key);

        var value = metadata[key] switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement element => element.ToString(),
            var other => other?.ToString()
        };

        value.Should().Be(expectedValue);
    }
}
