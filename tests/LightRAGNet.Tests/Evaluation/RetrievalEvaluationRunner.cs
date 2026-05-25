using FluentAssertions;

namespace LightRAGNet.Tests.Evaluation;

public static class RetrievalEvaluationRunner
{
    public static void AssertCase(
        RetrievalEvaluationResult result,
        RetrievalEvaluationCase evaluationCase)
    {
        var rawData = GetRawData(result, evaluationCase);
        var data = GetDictionary(rawData, "data", evaluationCase, "raw data");
        var metadata = GetDictionary(rawData, "metadata", evaluationCase, "raw data");

        metadata.Should().ContainKey(
            "query_mode",
            $"{evaluationCase.Name} metadata should include key 'query_mode'");
        metadata["query_mode"].Should().Be(evaluationCase.Mode.ToString());
        metadata.Should().ContainKey(
            "processing_info",
            $"{evaluationCase.Name} metadata should include key 'processing_info'");

        var chunks = GetList(data, "chunks", evaluationCase);
        var references = GetList(data, "references", evaluationCase);
        var entities = GetList(data, "entities", evaluationCase);
        var relationships = GetList(data, "relationships", evaluationCase);

        foreach (var chunkId in evaluationCase.ExpectedChunkIds)
        {
            chunks.Should().Contain(
                chunk => ValueEquals(chunk, "chunk_id", chunkId),
                $"{evaluationCase.Name} should include expected chunk {chunkId}");
        }

        foreach (var chunkId in evaluationCase.ForbiddenChunkIds)
        {
            chunks.Should().NotContain(
                chunk => ValueEquals(chunk, "chunk_id", chunkId),
                $"{evaluationCase.Name} should not include forbidden chunk {chunkId}");
        }

        foreach (var filePath in evaluationCase.ExpectedReferenceFilePaths)
        {
            references.Should().Contain(
                reference => ValueEquals(reference, "file_path", filePath),
                $"{evaluationCase.Name} should include expected reference {filePath}");
        }

        if (evaluationCase.ExpectedEntityIds.Count == 0)
        {
            entities.Should().BeEmpty($"{evaluationCase.Name} should not include KG entities");
        }
        else
        {
            foreach (var entityId in evaluationCase.ExpectedEntityIds)
            {
                entities.Should().Contain(
                    entity => ValueEquals(entity, "entity_name", entityId),
                    $"{evaluationCase.Name} should include expected entity {entityId}");
            }
        }

        if (evaluationCase.ExpectedRelationshipPairs.Count == 0)
        {
            relationships.Should().BeEmpty($"{evaluationCase.Name} should not include KG relationships");
        }
        else
        {
            foreach (var pair in evaluationCase.ExpectedRelationshipPairs)
            {
                relationships.Should().Contain(
                    relationship =>
                        ValueEquals(relationship, "src_id", pair.SourceId)
                        && ValueEquals(relationship, "tgt_id", pair.TargetId),
                    $"{evaluationCase.Name} should include relationship {pair.SourceId}->{pair.TargetId}");
            }
        }
    }

    public static void AssertChunkIds(
        RetrievalEvaluationResult result,
        RetrievalEvaluationCase evaluationCase,
        IReadOnlyList<string> expectedChunkIds)
    {
        var rawData = GetRawData(result, evaluationCase);
        var data = GetDictionary(rawData, "data", evaluationCase, "raw data");
        var chunks = GetList(data, "chunks", evaluationCase);

        chunks
            .Select(chunk => chunk["chunk_id"].ToString())
            .Should()
            .Equal(expectedChunkIds, $"{evaluationCase.Name} should return expected chunks in order");
    }

    private static Dictionary<string, object> GetRawData(
        RetrievalEvaluationResult result,
        RetrievalEvaluationCase evaluationCase)
    {
        result.RawData.Should().NotBeNull($"{evaluationCase.Name} should produce raw retrieval data");
        result.RawData!.Should().ContainKey("data", $"{evaluationCase.Name} should include raw data key 'data'");
        result.RawData.Should().ContainKey("metadata", $"{evaluationCase.Name} should include raw data key 'metadata'");
        return result.RawData;
    }

    private static Dictionary<string, object> GetDictionary(
        Dictionary<string, object> source,
        string key,
        RetrievalEvaluationCase evaluationCase,
        string section)
    {
        source.Should().ContainKey(key, $"{evaluationCase.Name} should include {section} key '{key}'");
        return source[key]
            .Should()
            .BeOfType<Dictionary<string, object>>($"{evaluationCase.Name} {section} key '{key}' should be an object")
            .Subject;
    }

    private static List<Dictionary<string, object>> GetList(
        Dictionary<string, object> data,
        string key,
        RetrievalEvaluationCase evaluationCase)
    {
        data.Should().ContainKey(key, $"{evaluationCase.Name} data section should include key '{key}'");

        return data[key]
            .Should()
            .BeAssignableTo<IEnumerable<object>>($"{evaluationCase.Name} data key '{key}' should be a list")
            .Subject
            .Should()
            .AllBeAssignableTo<Dictionary<string, object>>($"{evaluationCase.Name} data key '{key}' should contain objects")
            .Subject
            .Cast<Dictionary<string, object>>()
            .ToList();
    }

    private static bool ValueEquals(
        Dictionary<string, object> item,
        string key,
        string expected)
    {
        return item.TryGetValue(key, out var value)
               && string.Equals(value?.ToString(), expected, StringComparison.Ordinal);
    }
}
