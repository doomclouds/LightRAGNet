using System.Text;
using System.Text.Json;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Tests.Evaluation;

public static class RetrievalEvaluationDataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string GetDefaultDataDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "Evaluation", "Data");
    }

    public static RetrievalEvaluationDataSet LoadDefault()
    {
        return LoadFromDirectory(GetDefaultDataDirectory());
    }

    public static RetrievalEvaluationDataSet LoadFromDirectory(string dataDirectory)
    {
        var datasetJson = ReadJsonFile<EvaluationDatasetJson>(
            Path.Combine(dataDirectory, "sample_dataset.json"));
        var documentOracleJson = ReadJsonFile<EvaluationDocumentOracleJson>(
            Path.Combine(dataDirectory, "sample_retrieval_oracle.json"));
        var evaluationOracleJson = ReadJsonFile<LightRagNetEvaluationOracleJson>(
            Path.Combine(dataDirectory, "lightragnet_retrieval_oracle.json"));

        var testCases = RequireList(datasetJson.TestCases, "sample_dataset.json:test_cases")
            .Select((testCase, index) => new RetrievalEvaluationTestCase(
                RequireString(testCase.Question, $"sample_dataset.json:test_cases[{index}].question"),
                RequireString(testCase.GroundTruth, $"sample_dataset.json:test_cases[{index}].ground_truth"),
                RequireString(testCase.Project, $"sample_dataset.json:test_cases[{index}].project")))
            .ToArray();

        var documentOracleEntries = RequireList(documentOracleJson.Oracle, "sample_retrieval_oracle.json:oracle")
            .Select((entry, index) => new
            {
                Question = RequireString(entry.Question, $"sample_retrieval_oracle.json:oracle[{index}].question"),
                ExpectedDocuments = RequireStringList(
                    entry.ExpectedDocuments,
                    $"sample_retrieval_oracle.json:oracle[{index}].expected_documents")
            })
            .ToArray();
        EnsureUnique(documentOracleEntries.Select(entry => entry.Question), "document oracle questions");
        var documentOracleByQuestion = documentOracleEntries.ToDictionary(
            entry => entry.Question,
            entry => entry.ExpectedDocuments,
            StringComparer.Ordinal);

        var corpus = evaluationOracleJson.Corpus
            ?? throw new InvalidOperationException("Required field 'lightragnet_retrieval_oracle.json:corpus' is missing.");
        var chunks = RequireList(corpus.Chunks, "lightragnet_retrieval_oracle.json:corpus.chunks")
            .Select((chunk, index) => new RetrievalEvaluationChunkSpec(
                RequireString(chunk.Id, $"lightragnet_retrieval_oracle.json:corpus.chunks[{index}].id"),
                RequireString(chunk.DocumentName, $"lightragnet_retrieval_oracle.json:corpus.chunks[{index}].documentName"),
                RequireString(chunk.FilePath, $"lightragnet_retrieval_oracle.json:corpus.chunks[{index}].filePath"),
                RequireString(chunk.Content, $"lightragnet_retrieval_oracle.json:corpus.chunks[{index}].content")))
            .ToArray();
        var entities = RequireList(corpus.Entities, "lightragnet_retrieval_oracle.json:corpus.entities")
            .Select((entity, index) => new RetrievalEvaluationEntitySpec(
                RequireString(entity.Id, $"lightragnet_retrieval_oracle.json:corpus.entities[{index}].id"),
                RequireString(entity.Type, $"lightragnet_retrieval_oracle.json:corpus.entities[{index}].type"),
                RequireString(entity.Description, $"lightragnet_retrieval_oracle.json:corpus.entities[{index}].description"),
                RequireString(entity.SourceId, $"lightragnet_retrieval_oracle.json:corpus.entities[{index}].sourceId"),
                RequireString(entity.FilePath, $"lightragnet_retrieval_oracle.json:corpus.entities[{index}].filePath")))
            .ToArray();
        var relationships = RequireList(corpus.Relationships, "lightragnet_retrieval_oracle.json:corpus.relationships")
            .Select(ToRelationship)
            .ToArray();

        var cases = RequireList(evaluationOracleJson.Cases, "lightragnet_retrieval_oracle.json:cases")
            .Select((evaluationCase, index) => ToEvaluationCase(evaluationCase, index))
            .ToArray();

        Validate(dataDirectory, testCases, documentOracleByQuestion, chunks, entities, relationships, cases);

        return new RetrievalEvaluationDataSet(
            testCases,
            documentOracleByQuestion,
            chunks,
            entities,
            relationships,
            cases);
    }

    private static T ReadJsonFile<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Evaluation data file was not found: {path}");
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Evaluation data file could not be parsed: {path}");
    }

    private static RetrievalEvaluationCase ToEvaluationCase(RetrievalEvaluationCaseJson evaluationCase, int index)
    {
        var name = RequireString(evaluationCase.Name, $"lightragnet_retrieval_oracle.json:cases[{index}].name");
        string Field(string fieldName) => $"lightragnet_retrieval_oracle.json:cases[{index}]('{name}').{fieldName}";

        return new RetrievalEvaluationCase(
            Name: name,
            Query: RequireString(evaluationCase.Question, Field("question")),
            Mode: ParseMode(
                RequireString(evaluationCase.Mode, Field("mode")),
                name),
            HighLevelKeywords: RequireStringList(
                evaluationCase.HighLevelKeywords,
                Field("highLevelKeywords")),
            LowLevelKeywords: RequireStringList(
                evaluationCase.LowLevelKeywords,
                Field("lowLevelKeywords")),
            TopK: RequireValue(evaluationCase.TopK, Field("topK")),
            ChunkTopK: RequireValue(evaluationCase.ChunkTopK, Field("chunkTopK")),
            ExpectedDocumentNames: RequireStringList(
                evaluationCase.ExpectedDocumentNames,
                Field("expectedDocumentNames")),
            ExpectedChunkIds: RequireStringList(
                evaluationCase.ExpectedChunkIds,
                Field("expectedChunkIds")),
            ExpectedReferenceFilePaths: RequireStringList(
                evaluationCase.ExpectedReferenceFilePaths,
                Field("expectedReferenceFilePaths")),
            ExpectedEntityIds: RequireStringList(
                evaluationCase.ExpectedEntityIds,
                Field("expectedEntityIds")),
            ExpectedRelationshipPairs: RequireList(
                evaluationCase.ExpectedRelationshipPairs,
                Field("expectedRelationshipPairs")),
            ForbiddenChunkIds: evaluationCase.ForbiddenChunkIds ?? [],
            EnableRerank: RequireValue(evaluationCase.EnableRerank, Field("enableRerank")),
            ExpectedChunkOrder: evaluationCase.ExpectedChunkOrder ?? [],
            VectorScoresByChunkId: ToOrdinalDictionary(evaluationCase.VectorScoresByChunkId),
            RerankScoresByContent: ToOrdinalDictionary(evaluationCase.RerankScoresByContent));
    }

    private static RetrievalEvaluationRelationshipSpec ToRelationship(
        RetrievalEvaluationRelationshipJson relationship,
        int index)
    {
        var sourceId = RequireString(
            relationship.SourceId,
            $"lightragnet_retrieval_oracle.json:corpus.relationships[{index}].sourceId");
        var targetId = RequireString(
            relationship.TargetId,
            $"lightragnet_retrieval_oracle.json:corpus.relationships[{index}].targetId");
        var pair = RelationshipPair(sourceId, targetId);
        string Field(string fieldName) => $"lightragnet_retrieval_oracle.json:corpus.relationships[{index}]('{pair}').{fieldName}";

        return new RetrievalEvaluationRelationshipSpec(
            sourceId,
            targetId,
            RequireString(relationship.Keywords, Field("keywords")),
            RequireString(relationship.Description, Field("description")),
            RequireValue(relationship.Weight, Field("weight")),
            RequireString(relationship.SourceIdList, Field("sourceIdList")));
    }

    private static QueryMode ParseMode(string mode, string caseName)
    {
        return Enum.TryParse<QueryMode>(mode, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Unknown retrieval evaluation mode '{mode}' in case '{caseName}'.");
    }

    private static void Validate(
        string dataDirectory,
        IReadOnlyList<RetrievalEvaluationTestCase> testCases,
        IReadOnlyDictionary<string, IReadOnlyList<string>> documentOracleByQuestion,
        IReadOnlyList<RetrievalEvaluationChunkSpec> chunks,
        IReadOnlyList<RetrievalEvaluationEntitySpec> entities,
        IReadOnlyList<RetrievalEvaluationRelationshipSpec> relationships,
        IReadOnlyList<RetrievalEvaluationCase> cases)
    {
        EnsureUnique(testCases.Select(testCase => testCase.Question), "dataset questions");
        var datasetQuestions = testCases.Select(testCase => testCase.Question).ToHashSet(StringComparer.Ordinal);
        var documentOracleQuestions = documentOracleByQuestion.Keys.ToHashSet(StringComparer.Ordinal);
        if (!datasetQuestions.SetEquals(documentOracleQuestions))
        {
            throw new InvalidOperationException("Document oracle questions must exactly match dataset questions.");
        }

        EnsureUnique(cases.Select(evaluationCase => evaluationCase.Name), "evaluation case names");
        EnsureUnique(chunks.Select(chunk => chunk.Id), "chunk ids");
        EnsureUnique(entities.Select(entity => entity.Id), "entity ids");
        EnsureUniqueRelationshipPairs(relationships);

        var sampleDocumentsDirectory = Path.Combine(dataDirectory, "sample_documents");
        foreach (var expectedDocument in documentOracleByQuestion.Values.SelectMany(documentNames => documentNames).Distinct(StringComparer.Ordinal))
        {
            EnsureSampleDocumentExists(sampleDocumentsDirectory, expectedDocument);
        }

        foreach (var chunk in chunks)
        {
            EnsureSampleDocumentExists(sampleDocumentsDirectory, chunk.DocumentName);
        }

        var chunkIds = chunks.Select(chunk => chunk.Id).ToHashSet(StringComparer.Ordinal);
        var chunkById = chunks.ToDictionary(chunk => chunk.Id, StringComparer.Ordinal);
        var entityIds = entities.Select(entity => entity.Id).ToHashSet(StringComparer.Ordinal);
        var chunkContents = chunks.Select(chunk => chunk.Content).ToHashSet(StringComparer.Ordinal);
        var relationshipPairs = relationships
            .Select(relationship => RelationshipPair(relationship.SourceId, relationship.TargetId))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            EnsureContains(chunkIds, entity.SourceId, $"Entity '{entity.Id}' sourceId");
        }

        foreach (var relationship in relationships)
        {
            EnsureContains(entityIds, relationship.SourceId, "Relationship sourceId");
            EnsureContains(entityIds, relationship.TargetId, "Relationship targetId");
            foreach (var sourceId in SplitSourceIdList(relationship.SourceIdList))
            {
                EnsureContains(chunkIds, sourceId, "Relationship sourceIdList value");
            }
        }

        foreach (var evaluationCase in cases)
        {
            EnsureContains(datasetQuestions, evaluationCase.Query, $"Case '{evaluationCase.Name}' question");
            EnsureContains(documentOracleQuestions, evaluationCase.Query, $"Case '{evaluationCase.Name}' oracle question");
            EnsureEquivalent(
                documentOracleByQuestion[evaluationCase.Query],
                evaluationCase.ExpectedDocumentNames,
                $"Case '{evaluationCase.Name}' expectedDocumentNames");
            EnsureAllContain(chunkIds, evaluationCase.ExpectedChunkIds, $"Case '{evaluationCase.Name}' expectedChunkIds");
            EnsureExpectedChunksBelongToExpectedDocuments(evaluationCase, chunkById);
            EnsureExpectedReferenceFilePathsMatchExpectedChunks(evaluationCase, chunkById);
            EnsureAllContain(chunkIds, evaluationCase.ForbiddenChunkIds, $"Case '{evaluationCase.Name}' forbiddenChunkIds");
            EnsureAllContain(chunkIds, evaluationCase.ExpectedChunkOrder, $"Case '{evaluationCase.Name}' expectedChunkOrder");
            if (evaluationCase.ExpectedChunkOrder.Count > 0)
            {
                foreach (var expectedChunkId in evaluationCase.ExpectedChunkIds)
                {
                    EnsureContains(
                        evaluationCase.ExpectedChunkOrder.ToHashSet(StringComparer.Ordinal),
                        expectedChunkId,
                        $"Case '{evaluationCase.Name}' expectedChunkOrder");
                }
            }

            foreach (var expectedRelationshipPair in evaluationCase.ExpectedRelationshipPairs)
            {
                EnsureContains(
                    relationshipPairs,
                    RelationshipPair(expectedRelationshipPair.SourceId, expectedRelationshipPair.TargetId),
                    $"Case '{evaluationCase.Name}' expectedRelationshipPairs");
            }

            EnsureAllContain(
                chunkIds,
                evaluationCase.VectorScoresByChunkId.Keys,
                $"Case '{evaluationCase.Name}' vectorScoresByChunkId");
            EnsureAllContain(
                chunkContents,
                evaluationCase.RerankScoresByContent.Keys,
                $"Case '{evaluationCase.Name}' rerankScoresByContent");
        }
    }

    private static void EnsureExpectedChunksBelongToExpectedDocuments(
        RetrievalEvaluationCase evaluationCase,
        IReadOnlyDictionary<string, RetrievalEvaluationChunkSpec> chunkById)
    {
        var expectedDocumentNames = evaluationCase.ExpectedDocumentNames.ToHashSet(StringComparer.Ordinal);
        foreach (var expectedChunkId in evaluationCase.ExpectedChunkIds)
        {
            var chunk = chunkById[expectedChunkId];
            if (!expectedDocumentNames.Contains(chunk.DocumentName))
            {
                throw new InvalidOperationException(
                    $"Case '{evaluationCase.Name}' expectedChunkIds references chunk '{expectedChunkId}' " +
                    $"from document '{chunk.DocumentName}', which is not listed in expectedDocumentNames.");
            }
        }
    }

    private static void EnsureExpectedReferenceFilePathsMatchExpectedChunks(
        RetrievalEvaluationCase evaluationCase,
        IReadOnlyDictionary<string, RetrievalEvaluationChunkSpec> chunkById)
    {
        var expectedChunkFilePaths = evaluationCase.ExpectedChunkIds
            .Select(expectedChunkId => chunkById[expectedChunkId].FilePath)
            .ToHashSet(StringComparer.Ordinal);
        var expectedReferenceFilePaths = evaluationCase.ExpectedReferenceFilePaths.ToHashSet(StringComparer.Ordinal);

        if (!expectedChunkFilePaths.SetEquals(expectedReferenceFilePaths))
        {
            throw new InvalidOperationException(
                $"Case '{evaluationCase.Name}' expectedReferenceFilePaths must match file paths for expectedChunkIds.");
        }
    }

    private static void EnsureSampleDocumentExists(string sampleDocumentsDirectory, string documentName)
    {
        if (!File.Exists(Path.Combine(sampleDocumentsDirectory, documentName)))
        {
            throw new InvalidOperationException($"Expected document '{documentName}' was not found.");
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string name)
    {
        var duplicates = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"{name} must be unique. Duplicates: {string.Join(", ", duplicates)}");
        }
    }

    private static void EnsureUniqueRelationshipPairs(IReadOnlyList<RetrievalEvaluationRelationshipSpec> relationships)
    {
        var duplicates = relationships
            .Select(relationship => RelationshipPair(relationship.SourceId, relationship.TargetId))
            .GroupBy(pair => pair, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate relationship pairs are not allowed: {string.Join(", ", duplicates)}");
        }
    }

    private static void EnsureEquivalent(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> actual,
        string name)
    {
        if (!expected.ToHashSet(StringComparer.Ordinal).SetEquals(actual))
        {
            throw new InvalidOperationException($"{name} must match the document oracle.");
        }
    }

    private static void EnsureAllContain(
        IReadOnlySet<string> knownValues,
        IEnumerable<string> values,
        string name)
    {
        foreach (var value in values)
        {
            EnsureContains(knownValues, value, name);
        }
    }

    private static void EnsureContains(IReadOnlySet<string> knownValues, string value, string name)
    {
        if (!knownValues.Contains(value))
        {
            throw new InvalidOperationException($"{name} references unknown value '{value}'.");
        }
    }

    private static IReadOnlyList<T> RequireList<T>(IReadOnlyList<T>? values, string fieldName)
    {
        return values ?? throw new InvalidOperationException($"Required field '{fieldName}' is missing.");
    }

    private static T RequireValue<T>(T? value, string fieldName)
        where T : struct
    {
        return value ?? throw new InvalidOperationException($"Required field '{fieldName}' is missing.");
    }

    private static string RequireString(string? value, string fieldName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Required field '{fieldName}' is missing or empty.")
            : value;
    }

    private static IReadOnlyList<string> RequireStringList(IReadOnlyList<string>? values, string fieldName)
    {
        _ = values ?? throw new InvalidOperationException($"Required field '{fieldName}' is missing.");

        return values
            .Select((value, index) => RequireString(value, $"{fieldName}[{index}]"))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, float> ToOrdinalDictionary(IReadOnlyDictionary<string, float>? values)
    {
        return values is null
            ? new Dictionary<string, float>(StringComparer.Ordinal)
            : new Dictionary<string, float>(values, StringComparer.Ordinal);
    }

    private static IEnumerable<string> SplitSourceIdList(string sourceIdList)
    {
        return sourceIdList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string RelationshipPair(string sourceId, string targetId)
    {
        return $"{sourceId}->{targetId}";
    }
}
