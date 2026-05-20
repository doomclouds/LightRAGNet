using FluentAssertions;

namespace LightRAGNet.Tests.Web;

public sealed class RagQueryContractSourceTests
{
    [Fact]
    public void RagQueryRequest_ExposesChatQueryOptions()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Share", "Models", "RagQueryRequest.cs");

        source.Should().Contain("public sealed class RagQueryRequest");
        source.Should().Contain("public string Query { get; set; } = string.Empty;");
        source.Should().Contain("public QueryMode Mode { get; set; } = QueryMode.Mix;");
        source.Should().Contain("public bool Stream { get; set; } = true;");
        source.Should().Contain("public bool IncludeReferences { get; set; } = true;");
        source.Should().Contain("public string ResponseType { get; set; } = \"Multiple Paragraphs\";");
        source.Should().Contain("public int TopK { get; set; } = 40;");
        source.Should().Contain("public int ChunkTopK { get; set; } = 20;");
        source.Should().Contain("public bool EnableRerank { get; set; } = true;");
        source.Should().Contain("public List<string> HighLevelKeywords { get; set; } = [];");
        source.Should().Contain("public List<string> LowLevelKeywords { get; set; } = [];");
        source.Should().Contain("public bool OnlyNeedContext { get; set; }");
        source.Should().Contain("public bool OnlyNeedPrompt { get; set; }");
    }

    [Fact]
    public void RagQueryEvent_ExposesMetadataEvent()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Share", "Models", "RagQueryEvent.cs");

        source.Should().Contain("[JsonPolymorphic(TypeDiscriminatorPropertyName = \"type\")]");
        source.Should().Contain("[JsonDerivedType(typeof(TextChunkEvent), \"text_chunk\")]");
        source.Should().Contain("[JsonDerivedType(typeof(ErrorEvent), \"error\")]");
        source.Should().Contain("[JsonDerivedType(typeof(DoneEvent), \"done\")]");
        source.Should().Contain("[JsonDerivedType(typeof(QueryMetadataEvent), \"metadata\")]");
        source.Should().Contain("public sealed class QueryMetadataEvent : RagQueryEvent");
        source.Should().Contain("public QueryMode Mode { get; init; }");
        source.Should().Contain("public bool Stream { get; init; }");
        source.Should().Contain("public bool IncludeReferences { get; init; }");
        source.Should().Contain("public string ResponseType { get; init; } = \"Multiple Paragraphs\";");
        source.Should().Contain("public string CachePolicy { get; init; } = \"Unknown\";");
        source.Should().Contain("public IReadOnlyList<RagQueryReferenceDto> References { get; init; }");
        source.Should().Contain("public IReadOnlyList<string> HighLevelKeywords { get; init; }");
        source.Should().Contain("public IReadOnlyList<string> LowLevelKeywords { get; init; }");
        source.Should().Contain("public IReadOnlyDictionary<string, string> Diagnostics { get; init; }");
        source.Should().Contain("public sealed class RagQueryReferenceDto");
        source.Should().Contain("public string ReferenceId { get; init; } = string.Empty;");
        source.Should().Contain("public string FilePath { get; init; } = string.Empty;");
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
