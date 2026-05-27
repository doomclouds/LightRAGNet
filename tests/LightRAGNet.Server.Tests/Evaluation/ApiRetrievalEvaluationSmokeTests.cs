using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class ApiRetrievalEvaluationSmokeTests
{
    public static TheoryData<string> SmokeCaseNames => new()
    {
        "Naive_ReturnsExpectedArchitectureChunk",
        "Local_UsesLowLevelEntityFocus"
    };

    [Theory]
    [MemberData(nameof(SmokeCaseNames))]
    public async Task QueryDataEndpoint_WhenDrivenByRetrievalOracle_ReturnsExpectedRawData(string caseName)
    {
        var dataSet = ApiRetrievalEvaluationDataLoader.LoadDefault();
        var evaluationCase = dataSet.GetCase(caseName);
        using var factory = ApiRetrievalEvaluationTestDoubles.CreateServerFactory(dataSet);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/RagQuery/data", new RagQueryRequest
        {
            Query = evaluationCase.Question,
            Mode = evaluationCase.Mode,
            Stream = true,
            IncludeReferences = false,
            TopK = evaluationCase.TopK,
            ChunkTopK = evaluationCase.ChunkTopK,
            EnableRerank = evaluationCase.EnableRerank,
            HighLevelKeywords = [.. evaluationCase.HighLevelKeywords],
            LowLevelKeywords = [.. evaluationCase.LowLevelKeywords]
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RagQueryDataResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("success");
        body.Message.Should().Be("Retrieval data returned.");
        ReadString(body.Metadata, "query_mode").Should().Be(evaluationCase.Mode.ToString());

        var chunks = ReadObjectArray(body.Data, "chunks");
        var references = ReadObjectArray(body.Data, "references");
        var entities = ReadObjectArray(body.Data, "entities");
        var relationships = ReadObjectArray(body.Data, "relationships");

        foreach (var chunkId in evaluationCase.ExpectedChunkIds)
        {
            chunks.Should().Contain(
                chunk => ReadString(chunk, "chunk_id") == chunkId,
                $"{caseName} should include expected chunk {chunkId}");
        }

        foreach (var filePath in evaluationCase.ExpectedReferenceFilePaths)
        {
            references.Should().Contain(
                reference => ReadString(reference, "file_path") == filePath,
                $"{caseName} should include expected reference {filePath}");
        }

        foreach (var entityId in evaluationCase.ExpectedEntityIds)
        {
            entities.Should().Contain(
                entity => ReadString(entity, "entity_name") == entityId,
                $"{caseName} should include expected entity {entityId}");
        }

        foreach (var pair in evaluationCase.ExpectedRelationshipPairs)
        {
            relationships.Should().Contain(
                relationship => RelationshipMatches(relationship, pair),
                $"{caseName} should include relationship {pair.SourceId}->{pair.TargetId}");
        }
    }

    private static IReadOnlyList<Dictionary<string, object>> ReadObjectArray(
        IReadOnlyDictionary<string, object> source,
        string key)
    {
        source.Should().ContainKey(key);

        var element = source[key].Should().BeOfType<JsonElement>().Subject;
        element.ValueKind.Should().Be(JsonValueKind.Array);

        return element
            .EnumerateArray()
            .Select(item => JsonSerializer.Deserialize<Dictionary<string, object>>(item.GetRawText()))
            .OfType<Dictionary<string, object>>()
            .ToArray();
    }

    private static string ReadString(IReadOnlyDictionary<string, object> source, string key)
    {
        source.Should().ContainKey(key);

        return source[key] switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } value => value.GetString() ?? string.Empty,
            JsonElement value => value.ToString(),
            var value => value?.ToString() ?? string.Empty
        };
    }

    private static bool RelationshipMatches(
        IReadOnlyDictionary<string, object> relationship,
        ExpectedRelationshipPair expected)
    {
        return ReadString(relationship, "src_id") == expected.SourceId &&
               ReadString(relationship, "tgt_id") == expected.TargetId
            || ReadString(relationship, "src_id") == expected.TargetId &&
               ReadString(relationship, "tgt_id") == expected.SourceId;
    }
}
