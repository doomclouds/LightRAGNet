using FluentAssertions;

namespace LightRAGNet.Server.Tests;

public sealed class RagQueryControllerSourceTests
{
    [Fact]
    public void QueryAsync_PreservesCancellationAndNullBodyGuards()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Server", "Controllers", "RagQueryController.cs");

        source.Should().Contain("[FromBody] RagQueryRequest? request");
        source.Should().Contain("request is null || string.IsNullOrWhiteSpace(request.Query)");

        var cancellationCatchIndex = source.IndexOf(
            "catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)",
            StringComparison.Ordinal);
        var broadCatchIndex = source.IndexOf("catch (Exception ex)", StringComparison.Ordinal);

        cancellationCatchIndex.Should().BeGreaterThanOrEqualTo(0);
        broadCatchIndex.Should().BeGreaterThan(cancellationCatchIndex);
    }

    [Fact]
    public void QueryDataAsync_ExposesJsonEndpointAndForcesRetrievalDataMode()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Server", "Controllers", "RagQueryController.cs");

        source.Should().Contain("[HttpPost(\"data\")]");
        source.Should().Contain("public async Task<ActionResult<RagQueryDataResponse>> QueryDataAsync(");
        source.Should().Contain("ForceRetrievalDataRequest(request)");
        source.Should().Contain("Stream = false");
        source.Should().Contain("IncludeReferences = true");
        source.Should().Contain("OnlyNeedContext = true");
        source.Should().Contain("OnlyNeedPrompt = false");
        source.Should().Contain("SplitRawData(queryResult.RawData)");
    }

    [Fact]
    public void SerializeEvent_UsesHumanReadableJsonOptions()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Server", "Controllers", "RagQueryController.cs");

        source.Should().Contain("LightRAGJsonOptions.HumanReadable");
        source.Should().Contain("JsonSerializer.Serialize(_jsonWriter, item.Data, LightRAGJsonOptions.HumanReadable)");
    }

    [Fact]
    public void Program_UsesHumanReadableJsonOptionsForApiAndSignalR()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Server", "Program.cs");

        source.Should().Contain("options.JsonSerializerOptions.Encoder = LightRAGJsonOptions.HumanReadable.Encoder");
        source.Should().Contain("options.PayloadSerializerOptions.Encoder = LightRAGJsonOptions.HumanReadable.Encoder");
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([repositoryRoot, .. relativeParts]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LightRAGNet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing LightRAGNet.slnx.");
    }
}
