using System.Text.Json;
using FluentAssertions;

namespace LightRAGNet.Tests.Evaluation;

public sealed class RetrievalEvaluationDataLoaderTests
{
    [Fact]
    public void SampleDatasetJson_IsCopiedToOutputAndContainsExpectedTestCases()
    {
        var dataPath = Path.Combine(DataDirectory, "sample_dataset.json");
        using var document = JsonDocument.Parse(File.ReadAllText(dataPath));

        var testCases = document.RootElement.GetProperty("test_cases").EnumerateArray().ToArray();

        testCases.Should().HaveCount(10);
        testCases
            .Count(testCase => testCase.GetProperty("project").GetString() == "lightrag_evaluation_sample")
            .Should()
            .Be(6);
        testCases
            .Count(testCase => testCase.GetProperty("project").GetString() == "lightragnet_evaluation_extended")
            .Should()
            .Be(4);
    }

    [Fact]
    public void EvaluationDataFiles_AreCopiedAndInternallyConsistent()
    {
        using var dataset = ReadJson("sample_dataset.json");
        using var documentOracle = ReadJson("sample_retrieval_oracle.json");
        using var extendedOracle = ReadJson("lightragnet_retrieval_oracle.json");

        var sampleDocuments = ExpectedSampleDocuments.ToHashSet(StringComparer.Ordinal);
        foreach (var sampleDocument in sampleDocuments)
        {
            File.Exists(Path.Combine(DataDirectory, "sample_documents", sampleDocument))
                .Should()
                .BeTrue($"{sampleDocument} should be copied to test output");
        }

        var evaluationDeploymentDocument = File.ReadAllText(
            Path.Combine(DataDirectory, "sample_documents", "05_evaluation_and_deployment.md"));
        evaluationDeploymentDocument
            .Should()
            .NotContain("cache management", "deployment readiness should not compete with the operations oracle");

        var datasetQuestionsList = dataset.RootElement
            .GetProperty("test_cases")
            .EnumerateArray()
            .Select(testCase => RequiredString(testCase, "question"))
            .ToArray();
        var documentOracleEntries = documentOracle.RootElement
            .GetProperty("oracle")
            .EnumerateArray()
            .ToArray();
        var documentOracleQuestionsList = documentOracleEntries
            .Select(entry => RequiredString(entry, "question"))
            .ToArray();
        datasetQuestionsList.Should().OnlyHaveUniqueItems();
        documentOracleQuestionsList.Should().OnlyHaveUniqueItems();

        var datasetQuestions = datasetQuestionsList.ToHashSet(StringComparer.Ordinal);
        var documentOracleByQuestion = documentOracleEntries.ToDictionary(
            entry => RequiredString(entry, "question"),
            entry => RequiredStringArray(entry, "expected_documents"),
            StringComparer.Ordinal);
        var documentOracleQuestions = documentOracleQuestionsList.ToHashSet(StringComparer.Ordinal);

        documentOracleQuestions.Should().BeEquivalentTo(datasetQuestions);

        var expectedDocuments = documentOracle.RootElement
            .GetProperty("oracle")
            .EnumerateArray()
            .SelectMany(entry => RequiredStringArray(entry, "expected_documents"));
        expectedDocuments.Should().OnlyContain(documentName => sampleDocuments.Contains(documentName));

        var corpus = extendedOracle.RootElement.GetProperty("corpus");
        var chunks = corpus.GetProperty("chunks").EnumerateArray().ToArray();
        var chunkIdsList = chunks
            .Select(chunk => RequiredString(chunk, "id"))
            .ToArray();
        var chunkContents = chunks
            .Select(chunk => RequiredString(chunk, "content"))
            .ToHashSet(StringComparer.Ordinal);
        var entities = corpus.GetProperty("entities").EnumerateArray().ToArray();
        var entityIdsList = entities
            .Select(entity => RequiredString(entity, "id"))
            .ToArray();
        chunkIdsList.Should().OnlyHaveUniqueItems();
        entityIdsList.Should().OnlyHaveUniqueItems();

        var chunkIds = chunkIdsList.ToHashSet(StringComparer.Ordinal);
        var entityIds = entityIdsList.ToHashSet(StringComparer.Ordinal);

        chunks
            .Select(chunk => RequiredString(chunk, "documentName"))
            .Should()
            .OnlyContain(documentName => sampleDocuments.Contains(documentName));
        entities
            .Select(entity => RequiredString(entity, "sourceId"))
            .Should()
            .OnlyContain(sourceId => chunkIds.Contains(sourceId));

        var relationships = corpus.GetProperty("relationships").EnumerateArray().ToArray();
        relationships.Select(relationship => RequiredString(relationship, "sourceId"))
            .Should()
            .OnlyContain(sourceId => entityIds.Contains(sourceId));
        relationships.Select(relationship => RequiredString(relationship, "targetId"))
            .Should()
            .OnlyContain(targetId => entityIds.Contains(targetId));
        relationships
            .SelectMany(relationship => SplitSourceIdList(RequiredString(relationship, "sourceIdList")))
            .Should()
            .OnlyContain(sourceId => chunkIds.Contains(sourceId));

        var relationshipPairs = relationships
            .Select(relationship => RelationshipPair(
                RequiredString(relationship, "sourceId"),
                RequiredString(relationship, "targetId")))
            .ToHashSet(StringComparer.Ordinal);

        var evaluationCases = extendedOracle.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        evaluationCases
            .Select(evaluationCase => RequiredString(evaluationCase, "name"))
            .Should()
            .OnlyHaveUniqueItems();

        foreach (var evaluationCase in evaluationCases)
        {
            var question = RequiredString(evaluationCase, "question");
            datasetQuestions.Should().Contain(question);
            documentOracleQuestions.Should().Contain(question);

            var expectedDocumentNames = RequiredStringArray(evaluationCase, "expectedDocumentNames").ToArray();
            expectedDocumentNames.Should().OnlyContain(documentName => sampleDocuments.Contains(documentName));
            expectedDocumentNames.Should().BeEquivalentTo(documentOracleByQuestion[question]);

            var expectedRelationshipPairs = evaluationCase.GetProperty("expectedRelationshipPairs")
                .EnumerateArray()
                .Select(pair => RelationshipPair(
                    RequiredString(pair, "sourceId"),
                    RequiredString(pair, "targetId")));
            expectedRelationshipPairs.Should().OnlyContain(pair => relationshipPairs.Contains(pair));

            var expectedChunkIds = RequiredStringArray(evaluationCase, "expectedChunkIds").ToArray();
            expectedChunkIds.Should().OnlyContain(chunkId => chunkIds.Contains(chunkId));
            OptionalStringArray(evaluationCase, "forbiddenChunkIds")
                .Should()
                .OnlyContain(chunkId => chunkIds.Contains(chunkId));

            var expectedChunkOrder = OptionalStringArray(evaluationCase, "expectedChunkOrder").ToArray();
            expectedChunkOrder.Should().OnlyContain(chunkId => chunkIds.Contains(chunkId));
            if (expectedChunkOrder.Length > 0)
            {
                expectedChunkOrder.Should().Contain(expectedChunkIds);
            }

            OptionalObjectPropertyNames(evaluationCase, "vectorScoresByChunkId")
                .Should()
                .OnlyContain(chunkId => chunkIds.Contains(chunkId));
            OptionalObjectPropertyNames(evaluationCase, "rerankScoresByContent")
                .Should()
                .OnlyContain(content => chunkContents.Contains(content));
        }
    }

    private static readonly IReadOnlyList<string> ExpectedSampleDocuments =
    [
        "01_lightrag_overview.md",
        "02_rag_architecture.md",
        "03_lightrag_improvements.md",
        "04_supported_databases.md",
        "05_evaluation_and_deployment.md"
    ];

    private static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "Evaluation", "Data");

    private static JsonDocument ReadJson(string fileName)
    {
        var path = Path.Combine(DataDirectory, fileName);
        File.Exists(path).Should().BeTrue($"{fileName} should be copied to test output");

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        value.Should().NotBeNull();

        return value!;
    }

    private static IReadOnlyList<string> RequiredStringArray(JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName)
            .EnumerateArray()
            .Select(RequiredString)
            .ToArray();
    }

    private static IReadOnlyList<string> OptionalStringArray(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.EnumerateArray().Select(RequiredString).ToArray()
            : [];
    }

    private static IReadOnlyList<string> OptionalObjectPropertyNames(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.EnumerateObject().Select(propertyItem => propertyItem.Name).ToArray()
            : [];
    }

    private static IReadOnlyList<string> SplitSourceIdList(string sourceIdList)
    {
        return sourceIdList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string RelationshipPair(string sourceId, string targetId)
    {
        return $"{sourceId}->{targetId}";
    }

    private static string RequiredString(JsonElement element)
    {
        var value = element.GetString();
        value.Should().NotBeNull();

        return value!;
    }
}
