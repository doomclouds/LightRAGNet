using FluentAssertions;
using LightRAGNet.Server.Controllers;
using LightRAGNet.Share.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Server.Tests;

public sealed class RagQueryControllerTests
{
    [Fact]
    public async Task QueryDataAsync_WhenQueryIsBlank_ReturnsBadRequest()
    {
        var controller = new RagQueryController(null!, NullLogger<RagQueryController>.Instance);

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
}
