using FluentAssertions;

namespace LightRAGNet.Tests.Evaluation;

public static class RetrievalEvaluationRunner
{
    public static void AssertCase(
        RetrievalEvaluationResult result,
        RetrievalEvaluationCase evaluationCase)
    {
        result.RawData.Should().NotBeNull($"{evaluationCase.Name} should produce raw retrieval data");
        var data = result.RawData!["data"].Should().BeOfType<Dictionary<string, object>>().Subject;
        var metadata = result.RawData!["metadata"].Should().BeOfType<Dictionary<string, object>>().Subject;

        metadata["query_mode"].Should().Be(evaluationCase.Mode.ToString());
        metadata.Should().ContainKey("processing_info");

        var chunks = GetList(data, "chunks");
        var references = GetList(data, "references");
        var entities = GetList(data, "entities");
        var relationships = GetList(data, "relationships");

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

        foreach (var entityId in evaluationCase.ExpectedEntityIds)
        {
            entities.Should().Contain(
                entity => ValueEquals(entity, "entity_name", entityId),
                $"{evaluationCase.Name} should include expected entity {entityId}");
        }

        foreach (var pair in evaluationCase.ExpectedRelationshipPairs)
        {
            relationships.Should().Contain(
                relationship =>
                    ValueEquals(relationship, "src_id", pair.SourceId)
                    && ValueEquals(relationship, "tgt_id", pair.TargetId),
                $"{evaluationCase.Name} should include relationship {pair.SourceId}->{pair.TargetId}");
        }
    }

    private static List<Dictionary<string, object>> GetList(
        Dictionary<string, object> data,
        string key)
    {
        return data[key]
            .Should()
            .BeAssignableTo<IEnumerable<object>>()
            .Subject
            .Should()
            .AllBeAssignableTo<Dictionary<string, object>>()
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
