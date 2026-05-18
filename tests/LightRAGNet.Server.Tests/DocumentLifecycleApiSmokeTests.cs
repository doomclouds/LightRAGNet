using System.Globalization;
using System.Net;
using FluentAssertions;

namespace LightRAGNet.Server.Tests;

public class DocumentLifecycleApiSmokeTests
{
    [Fact]
    public async Task MarkdownDocumentsCount_ReturnsEmptyIsolatedStoreCount()
    {
        using var factory = new LightRagServerFactory();

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/MarkdownDocuments/count");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        int.Parse(body, CultureInfo.InvariantCulture).Should().Be(0);
    }
}
