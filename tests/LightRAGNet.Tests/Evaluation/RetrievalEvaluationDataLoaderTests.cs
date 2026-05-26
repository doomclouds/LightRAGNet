using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace LightRAGNet.Tests.Evaluation;

public sealed class RetrievalEvaluationDataLoaderTests
{
    [Fact]
    public void LoadDefault_LoadsPythonCompatibleDatasetAndExtendedCases()
    {
        var dataSet = RetrievalEvaluationDataLoader.LoadDefault();

        dataSet.TestCases.Should().HaveCount(10);
        dataSet.TestCases
            .Count(testCase => testCase.Project == "lightrag_evaluation_sample")
            .Should()
            .Be(6);
        dataSet.TestCases
            .Count(testCase => testCase.Project == "lightragnet_evaluation_extended")
            .Should()
            .Be(4);
        dataSet.DocumentOracleByQuestion
            .Should()
            .ContainKey("What are the three main components required in a RAG system?");
        dataSet.Cases
            .Select(evaluationCase => evaluationCase.Name)
            .Should()
            .BeEquivalentTo(
                [
                    "Naive_ReturnsExpectedArchitectureChunk",
                    "Local_UsesLowLevelEntityFocus",
                    "Global_UsesHighLevelRelationshipFocus",
                    "Mix_ReturnsKgEntityRelationshipAndRelatedChunk",
                    "Rerank_KeepsRelevantChunkInFinalContext"
                ]);
        dataSet.Chunks.Should().HaveCount(5);
        dataSet.Entities.Should().HaveCount(3);
        dataSet.Relationships.Should().HaveCount(2);

        var rerankCase = dataSet.Cases.Should()
            .ContainSingle(evaluationCase => evaluationCase.Name == "Rerank_KeepsRelevantChunkInFinalContext")
            .Subject;
        rerankCase.TopK.Should().Be(5);
        rerankCase.ChunkTopK.Should().Be(3);
        rerankCase.ExpectedChunkOrder.Should().Equal(
            [
                "chunk-operations-health-cache",
                "chunk-storage-vector-databases",
                "chunk-evaluation-quality-metrics"
            ]);
        rerankCase.VectorScoresByChunkId.Should().ContainKeys(
            "chunk-operations-health-cache",
            "chunk-storage-vector-databases",
            "chunk-evaluation-quality-metrics");
        rerankCase.RerankScoresByContent.Should().ContainKey(
            "Operations include health checks, cache management, deployment readiness, and safe maintenance workflows.");
    }

    [Fact]
    public void LoadDefault_ValidatesExtendedCaseQuestionsAgainstDataset()
    {
        var dataSet = RetrievalEvaluationDataLoader.LoadDefault();

        var questions = dataSet.TestCases
            .Select(testCase => testCase.Question)
            .ToHashSet(StringComparer.Ordinal);
        dataSet.Cases
            .Select(evaluationCase => evaluationCase.Query)
            .Should()
            .OnlyContain(question => questions.Contains(question));
    }

    [Fact]
    public void LoadFromDirectory_WhenExpectedDocumentIsMissing_ThrowsHelpfulMessage()
    {
        using var temp = TestTempDirectory.Create();
        CopyDirectory(DataDirectory, temp.Path);
        File.Delete(Path.Combine(temp.Path, "sample_documents", "02_rag_architecture.md"));

        var act = () => RetrievalEvaluationDataLoader.LoadFromDirectory(temp.Path);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Expected document '02_rag_architecture.md' was not found*");
    }

    [Fact]
    public void LoadFromDirectory_WhenCaseExpectedRelationshipPairsFieldIsMissing_ThrowsHelpfulMessage()
    {
        using var temp = TestTempDirectory.Create();
        CopyDirectory(DataDirectory, temp.Path);
        RemoveCaseField(
            temp.Path,
            "Local_UsesLowLevelEntityFocus",
            "expectedRelationshipPairs");

        var act = () => RetrievalEvaluationDataLoader.LoadFromDirectory(temp.Path);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Local_UsesLowLevelEntityFocus*expectedRelationshipPairs*");
    }

    [Theory]
    [InlineData("topK")]
    [InlineData("chunkTopK")]
    [InlineData("enableRerank")]
    public void LoadFromDirectory_WhenCasePrimitiveRequiredFieldIsMissing_ThrowsHelpfulMessage(string fieldName)
    {
        using var temp = TestTempDirectory.Create();
        CopyDirectory(DataDirectory, temp.Path);
        RemoveCaseField(
            temp.Path,
            "Rerank_KeepsRelevantChunkInFinalContext",
            fieldName);

        var act = () => RetrievalEvaluationDataLoader.LoadFromDirectory(temp.Path);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*Rerank_KeepsRelevantChunkInFinalContext*{fieldName}*");
    }

    [Fact]
    public void LoadFromDirectory_WhenRelationshipWeightIsMissing_ThrowsHelpfulMessage()
    {
        using var temp = TestTempDirectory.Create();
        CopyDirectory(DataDirectory, temp.Path);
        RemoveFirstRelationshipField(temp.Path, "weight");

        var act = () => RetrievalEvaluationDataLoader.LoadFromDirectory(temp.Path);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*RETRIEVAL_SYSTEM->EMBEDDING_MODEL*weight*");
    }

    [Fact]
    public void LoadFromDirectory_WhenRelationshipPairIsDuplicated_ThrowsHelpfulMessage()
    {
        using var temp = TestTempDirectory.Create();
        CopyDirectory(DataDirectory, temp.Path);
        DuplicateFirstRelationship(temp.Path);

        var act = () => RetrievalEvaluationDataLoader.LoadFromDirectory(temp.Path);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*uplicate*RETRIEVAL_SYSTEM->EMBEDDING_MODEL*");
    }

    [Fact]
    public void LoadFromDirectory_WhenExpectedChunkDocumentDoesNotMatchExpectedDocuments_ThrowsHelpfulMessage()
    {
        using var temp = TestTempDirectory.Create();
        CopyDirectory(DataDirectory, temp.Path);
        SetCaseStringArray(
            temp.Path,
            "Local_UsesLowLevelEntityFocus",
            "expectedChunkIds",
            ["chunk-storage-vector-databases"]);

        var act = () => RetrievalEvaluationDataLoader.LoadFromDirectory(temp.Path);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Local_UsesLowLevelEntityFocus*expectedChunkIds*");
    }

    [Fact]
    public void LoadFromDirectory_WhenExpectedReferencePathDoesNotMatchExpectedChunks_ThrowsHelpfulMessage()
    {
        using var temp = TestTempDirectory.Create();
        CopyDirectory(DataDirectory, temp.Path);
        SetCaseStringArray(
            temp.Path,
            "Local_UsesLowLevelEntityFocus",
            "expectedReferenceFilePaths",
            ["docs/eval/04_supported_databases.md"]);

        var act = () => RetrievalEvaluationDataLoader.LoadFromDirectory(temp.Path);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Local_UsesLowLevelEntityFocus*expectedReferenceFilePaths*");
    }

    [Fact]
    public void SampleDatasetJson_IsCopiedToOutputAndContainsExpectedTestCases()
    {
        var dataPath = Path.Combine(DataDirectory, "sample_dataset.json");
        using var document = JsonDocument.Parse(File.ReadAllText(dataPath, Encoding.UTF8));

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
            Path.Combine(DataDirectory, "sample_documents", "05_evaluation_and_deployment.md"),
            Encoding.UTF8);
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

        return JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
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

    private static void RemoveFirstRelationshipField(string dataDirectory, string fieldName)
    {
        var root = ReadExtendedOracleNode(dataDirectory);
        var firstRelationship = FirstRelationship(root);

        firstRelationship.Remove(fieldName).Should().BeTrue();

        WriteExtendedOracleNode(dataDirectory, root);
    }

    private static void DuplicateFirstRelationship(string dataDirectory)
    {
        var root = ReadExtendedOracleNode(dataDirectory);
        var relationships = Relationships(root);
        relationships.Add(FirstRelationship(root).DeepClone());

        WriteExtendedOracleNode(dataDirectory, root);
    }

    private static void RemoveCaseField(string dataDirectory, string caseName, string fieldName)
    {
        var root = ReadExtendedOracleNode(dataDirectory);
        var cases = root["cases"]!.AsArray();
        var evaluationCase = cases
            .Select(node => node!.AsObject())
            .Single(jsonCase => jsonCase["name"]!.GetValue<string>() == caseName);

        evaluationCase.Remove(fieldName).Should().BeTrue();

        WriteExtendedOracleNode(dataDirectory, root);
    }

    private static void SetCaseStringArray(
        string dataDirectory,
        string caseName,
        string fieldName,
        IReadOnlyList<string> values)
    {
        var root = ReadExtendedOracleNode(dataDirectory);
        var cases = root["cases"]!.AsArray();
        var evaluationCase = cases
            .Select(node => node!.AsObject())
            .Single(jsonCase => jsonCase["name"]!.GetValue<string>() == caseName);

        evaluationCase[fieldName] = new JsonArray(values.Select(value => JsonValue.Create(value)).ToArray());

        WriteExtendedOracleNode(dataDirectory, root);
    }

    private static JsonObject ReadExtendedOracleNode(string dataDirectory)
    {
        return JsonNode.Parse(File.ReadAllText(ExtendedOraclePath(dataDirectory), Encoding.UTF8))!.AsObject();
    }

    private static void WriteExtendedOracleNode(string dataDirectory, JsonObject root)
    {
        File.WriteAllText(
            ExtendedOraclePath(dataDirectory),
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
    }

    private static JsonArray Relationships(JsonObject root)
    {
        return root["corpus"]!["relationships"]!.AsArray();
    }

    private static JsonObject FirstRelationship(JsonObject root)
    {
        return Relationships(root)[0]!.AsObject();
    }

    private static string ExtendedOraclePath(string dataDirectory)
    {
        return Path.Combine(dataDirectory, "lightragnet_retrieval_oracle.json");
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            File.Copy(file, Path.Combine(targetDirectory, relativePath), overwrite: true);
        }
    }

    private sealed class TestTempDirectory : IDisposable
    {
        private TestTempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TestTempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"lightragnet-evaluation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);

            return new TestTempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
