using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Services.DocumentDeletion;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Services.GraphCuration;

public sealed class GraphCurationService
{
    private const string EntitiesCollection = "entities";
    private const string RelationshipsCollection = "relationships";
    private const string SourceSeparator = "<SEP>";
    private static readonly HashSet<string> ImmutableProvenanceFields = new(StringComparer.Ordinal)
    {
        "source_id",
        "file_path",
        "created_at"
    };

    private readonly IGraphStore graphStore;
    private readonly IVectorStore vectorStore;
    private readonly IEmbeddingService embeddingService;
    private readonly IKVStore textChunksStore;
    private readonly IKVStore fullEntitiesStore;
    private readonly IKVStore fullRelationsStore;
    private readonly IKVStore entityChunksStore;
    private readonly IKVStore relationChunksStore;
    private readonly Func<Task> bumpQueryRevisionAsync;
    private readonly ILogger<GraphCurationService> logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> graphMutationLocks = new(StringComparer.Ordinal);

    public GraphCurationService(
        IGraphStore graphStore,
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IKVStore textChunksStore,
        IKVStore fullEntitiesStore,
        IKVStore fullRelationsStore,
        IKVStore entityChunksStore,
        IKVStore relationChunksStore,
        Func<Task> bumpQueryRevisionAsync,
        ILogger<GraphCurationService> logger)
    {
        this.graphStore = graphStore;
        this.vectorStore = vectorStore;
        this.embeddingService = embeddingService;
        this.textChunksStore = textChunksStore;
        this.fullEntitiesStore = fullEntitiesStore;
        this.fullRelationsStore = fullRelationsStore;
        this.entityChunksStore = entityChunksStore;
        this.relationChunksStore = relationChunksStore;
        this.bumpQueryRevisionAsync = bumpQueryRevisionAsync;
        this.logger = logger;
    }

    public Task<bool> EntityExistsAsync(string entityName, CancellationToken cancellationToken = default)
    {
        return graphStore.HasNodeAsync(entityName, cancellationToken);
    }

    public async Task<GraphCurationOperationResult> CreateEntityAsync(
        GraphEntityCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var entityName = request.EntityName.Trim();
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return GraphCurationOperationResult.Failure(
                "Entity name is required.",
                "validation",
                "validation_error");
        }

        var description = GetString(request.EntityData, "description");
        if (string.IsNullOrWhiteSpace(description))
        {
            return GraphCurationOperationResult.Failure(
                "Entity description is required.",
                "validation",
                "validation_error");
        }

        return await ExecuteWithEntityLocksAsync(
            [entityName],
            () => CreateEntityCoreAsync(entityName, request, cancellationToken),
            cancellationToken);
    }

    private async Task<GraphCurationOperationResult> CreateEntityCoreAsync(
        string entityName,
        GraphEntityCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (await graphStore.HasNodeAsync(entityName, cancellationToken))
        {
            return GraphCurationOperationResult.Failure(
                $"Entity '{entityName}' already exists.",
                "graph",
                "conflict");
        }

        var nodeData = BuildEntityData(entityName, request.EntityData);
        var entityVector = await BuildEntityVectorDocumentAsync(entityName, nodeData, cancellationToken);

        await graphStore.UpsertNodeAsync(entityName, nodeData, cancellationToken);
        await UpsertEntityTrackingAsync(entityName, nodeData, cancellationToken);
        await UpsertFullEntityIndexAsync(entityName, nodeData, cancellationToken);
        await vectorStore.UpsertAsync(EntitiesCollection, [entityVector], cancellationToken);
        await bumpQueryRevisionAsync();

        return GraphCurationOperationResult.Success(
            $"Entity '{entityName}' created.",
            nodeData,
            new GraphCurationOperationSummary(
                Merged: false,
                MergeStatus: "not_required",
                MergeError: null,
                OperationStatus: "created",
                TargetEntity: entityName,
                FinalEntity: entityName,
                Renamed: false));
    }

    public async Task<GraphCurationOperationResult> EditEntityAsync(
        GraphEntityEditRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.HasBlankDescription())
        {
            return GraphCurationOperationResult.Failure(
                "Entity description cannot be blank.",
                "validation",
                "validation_error");
        }

        var immutableField = request.UpdatedData.Keys.FirstOrDefault(ImmutableProvenanceFields.Contains);
        if (immutableField is not null)
        {
            return GraphCurationOperationResult.Failure(
                $"Entity field '{immutableField}' cannot be edited.",
                "validation",
                "validation_error");
        }

        var currentName = request.EntityName.Trim();
        if (string.IsNullOrWhiteSpace(currentName))
        {
            return GraphCurationOperationResult.Failure(
                "Entity name is required.",
                "validation",
                "validation_error");
        }

        var requestedName = GetString(request.UpdatedData, "entity_name");
        var finalName = string.IsNullOrWhiteSpace(requestedName) ? currentName : requestedName.Trim();
        var renamed = !string.Equals(currentName, finalName, StringComparison.Ordinal);

        if (renamed && !request.AllowRename)
        {
            return GraphCurationOperationResult.Failure(
                "Entity rename is not allowed.",
                "validation",
                "rename_not_allowed");
        }

        if (renamed)
        {
            return await EditEntityWithRenameLocksAsync(
                request,
                currentName,
                finalName,
                cancellationToken);
        }

        return await ExecuteWithEntityLocksAsync(
            [currentName],
            () => EditEntityCoreAsync(request, currentName, finalName, renamed: false, cancellationToken),
            cancellationToken);
    }

    private async Task<GraphCurationOperationResult> EditEntityWithRenameLocksAsync(
        GraphEntityEditRequest request,
        string currentName,
        string finalName,
        CancellationToken cancellationToken)
    {
        var lockKeys = new HashSet<string>(
            [EntityLockKey(currentName), EntityLockKey(finalName)],
            StringComparer.Ordinal);

        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var attemptResult = await ExecuteWithGraphMutationLocksAsync(
                lockKeys,
                async () =>
                {
                    var currentNode = await graphStore.GetNodeAsync(currentName, cancellationToken);
                    if (currentNode is null)
                    {
                        return EntityRenameLockAttempt.Completed(GraphCurationOperationResult.Failure(
                            $"Entity '{currentName}' was not found.",
                            "graph",
                            "not_found"));
                    }

                    if (await graphStore.HasNodeAsync(finalName, cancellationToken))
                    {
                        if (!request.AllowMerge)
                        {
                            return EntityRenameLockAttempt.Completed(GraphCurationOperationResult.Failure(
                                $"Entity '{finalName}' already exists.",
                                "graph",
                                "conflict"));
                        }

                        var mergePlanResult = await BuildMergePlanAsync([currentName], finalName, cancellationToken);
                        if (mergePlanResult.Failure is not null)
                        {
                            return EntityRenameLockAttempt.Completed(mergePlanResult.Failure);
                        }

                        if (!mergePlanResult.RequiredLockKeys.SetEquals(lockKeys))
                        {
                            return EntityRenameLockAttempt.Retry(mergePlanResult.RequiredLockKeys);
                        }

                        var mergePlan = mergePlanResult.Plan!;
                        var entityVector = await BuildEntityVectorDocumentAsync(
                            mergePlan.TargetEntity,
                            mergePlan.TargetData,
                            cancellationToken);
                        var relationVectors = await CreateRelationVectorDocumentsAsync(
                            mergePlan.NewRelationEdges,
                            cancellationToken);

                        return EntityRenameLockAttempt.Completed(await ApplyMergePlanAsync(
                            mergePlan,
                            entityVector,
                            relationVectors,
                            cancellationToken));
                    }

                    var connectedEdges = await GetConnectedEdgesAsync(currentName, finalName, cancellationToken);
                    var requiredLockKeys = BuildEntityRenameLockKeys(currentName, finalName, connectedEdges);
                    if (!requiredLockKeys.SetEquals(lockKeys))
                    {
                        return EntityRenameLockAttempt.Retry(requiredLockKeys);
                    }

                    return EntityRenameLockAttempt.Completed(await EditEntityCoreAsync(
                        request,
                        currentName,
                        finalName,
                        renamed: true,
                        cancellationToken));
                },
                cancellationToken);

            if (attemptResult.Result is not null)
            {
                return attemptResult.Result;
            }

            lockKeys = attemptResult.RequiredLockKeys;
        }

        return GraphCurationOperationResult.Failure(
            $"Entity '{currentName}' rename lock set changed repeatedly; retry the operation.",
            "graph",
            "retry_failed");
    }

    private async Task<GraphCurationOperationResult> EditEntityCoreAsync(
        GraphEntityEditRequest request,
        string currentName,
        string finalName,
        bool renamed,
        CancellationToken cancellationToken)
    {
        var currentNode = await graphStore.GetNodeAsync(currentName, cancellationToken);
        if (currentNode is null)
        {
            return GraphCurationOperationResult.Failure(
                $"Entity '{currentName}' was not found.",
                "graph",
                "not_found");
        }

        if (renamed && await graphStore.HasNodeAsync(finalName, cancellationToken))
        {
            if (!request.AllowMerge)
            {
                return GraphCurationOperationResult.Failure(
                    $"Entity '{finalName}' already exists.",
                    "graph",
                    "conflict");
            }

            return GraphCurationOperationResult.Failure(
                $"Entity '{finalName}' appeared during rename; retry the operation.",
                "graph",
                "retry_failed");
        }

        var updatedData = currentNode.Properties.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        foreach (var (key, value) in request.UpdatedData)
        {
            updatedData[key] = value;
        }

        updatedData["entity_id"] = finalName;
        updatedData["entity_name"] = finalName;

        var connectedEdges = renamed
            ? await GetConnectedEdgesAsync(currentName, finalName, cancellationToken)
            : [];
        var entityChunkIds = await CollectEntityChunkIdsAsync(currentName, updatedData, cancellationToken);
        var connectedRelationChunkIds = renamed
            ? await BuildRelationChunkOverridesByNewKeyAsync(connectedEdges, cancellationToken)
            : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var entityVector = await BuildEntityVectorDocumentAsync(finalName, updatedData, cancellationToken);
        var relationVectors = renamed
            ? await CreateRelationVectorDocumentsAsync(connectedEdges, cancellationToken)
            : [];

        if (renamed)
        {
            await graphStore.UpsertNodeAsync(finalName, updatedData, cancellationToken);
            await UpsertRewiredEdgesAsync(connectedEdges, cancellationToken);
            await graphStore.DeleteNodeAsync(currentName, cancellationToken);
            await entityChunksStore.DeleteAsync([currentName], cancellationToken);
            await DeleteOldRelationTrackingAsync(connectedEdges, cancellationToken);
            await UpsertRelationTrackingAsync(connectedEdges, connectedRelationChunkIds, cancellationToken);
            await UpsertFullEntityIndexAsync(finalName, entityChunkIds, cancellationToken, currentName);
            await UpsertFullRelationIndexesAsync(connectedEdges, connectedRelationChunkIds, cancellationToken);
        }
        else
        {
            await graphStore.UpsertNodeAsync(finalName, updatedData, cancellationToken);
            await UpsertFullEntityIndexAsync(finalName, entityChunkIds, cancellationToken);
        }

        await UpsertEntityTrackingAsync(finalName, entityChunkIds, cancellationToken);
        if (renamed)
        {
            await vectorStore.DeleteAsync(EntitiesCollection, [GraphCurationVectorIds.Entity(currentName)], cancellationToken);
            await DeleteOldRelationVectorsAsync(connectedEdges, cancellationToken);
        }

        await vectorStore.UpsertAsync(EntitiesCollection, [entityVector], cancellationToken);
        if (renamed)
        {
            await UpsertRelationVectorsAsync(relationVectors, cancellationToken);
        }

        await bumpQueryRevisionAsync();

        return GraphCurationOperationResult.Success(
            $"Entity '{currentName}' updated.",
            updatedData,
            new GraphCurationOperationSummary(
                Merged: false,
                MergeStatus: "not_required",
                MergeError: null,
                OperationStatus: "updated",
                TargetEntity: finalName,
                FinalEntity: finalName,
                Renamed: renamed));
    }

    public async Task<GraphCurationOperationResult> MergeEntitiesAsync(
        GraphEntityMergeRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedRequest = NormalizeMergeRequest(request);
        if (normalizedRequest.Failure is not null)
        {
            return normalizedRequest.Failure;
        }

        var targetEntity = normalizedRequest.TargetEntity!;
        var sourceEntities = normalizedRequest.SourceEntities!;
        var lockKeys = BuildEntityMergeInitialLockKeys(sourceEntities, targetEntity);

        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var attemptResult = await ExecuteWithGraphMutationLocksAsync(
                lockKeys,
                async () =>
                {
                    var planResult = await BuildMergePlanAsync(sourceEntities, targetEntity, cancellationToken);
                    if (planResult.Failure is not null)
                    {
                        return EntityRenameLockAttempt.Completed(planResult.Failure);
                    }

                    if (!planResult.RequiredLockKeys.SetEquals(lockKeys))
                    {
                        return EntityRenameLockAttempt.Retry(planResult.RequiredLockKeys);
                    }

                    var plan = planResult.Plan!;
                    var entityVector = await BuildEntityVectorDocumentAsync(
                        plan.TargetEntity,
                        plan.TargetData,
                        cancellationToken);
                    var relationVectors = await CreateRelationVectorDocumentsAsync(
                        plan.NewRelationEdges,
                        cancellationToken);

                    return EntityRenameLockAttempt.Completed(await ApplyMergePlanAsync(
                        plan,
                        entityVector,
                        relationVectors,
                        cancellationToken));
                },
                cancellationToken);

            if (attemptResult.Result is not null)
            {
                return attemptResult.Result;
            }

            lockKeys = attemptResult.RequiredLockKeys;
        }

        return GraphCurationOperationResult.Failure(
            $"Entity merge into '{targetEntity}' lock set changed repeatedly; retry the operation.",
            "graph",
            "retry_failed",
            new GraphCurationOperationSummary(
                Merged: false,
                MergeStatus: "retry_failed",
                MergeError: "lock_set_changed",
                OperationStatus: "retry_failed",
                TargetEntity: targetEntity,
                FinalEntity: targetEntity,
                Renamed: false));
    }

    public async Task<GraphCurationOperationResult> DeleteRelationAsync(
        string sourceEntity,
        string targetEntity,
        CancellationToken cancellationToken = default)
    {
        sourceEntity = sourceEntity.Trim();
        targetEntity = targetEntity.Trim();
        if (string.IsNullOrWhiteSpace(sourceEntity) || string.IsNullOrWhiteSpace(targetEntity))
        {
            return GraphCurationOperationResult.Failure(
                "Relation source and target are required.",
                "validation",
                "validation_error");
        }

        var normalizedPair = GraphCurationVectorIds.NormalizePair(sourceEntity, targetEntity);
        return await ExecuteWithRelationAndEndpointLocksAsync(
            normalizedPair.Source,
            normalizedPair.Target,
            async () =>
            {
                var currentEdge = await graphStore.GetEdgeAsync(
                    normalizedPair.Source,
                    normalizedPair.Target,
                    cancellationToken);
                if (currentEdge is null)
                {
                    return GraphCurationOperationResult.Failure(
                        $"Relation '{GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target)}' was not found.",
                        "graph",
                        "not_found");
                }

                var edge = new RewiredEdge(
                    normalizedPair.Source,
                    normalizedPair.Target,
                    normalizedPair.Source,
                    normalizedPair.Target,
                    currentEdge.Properties.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.Ordinal));
                var chunkIds = await CollectRelationChunkIdsAsync(edge, cancellationToken);

                await graphStore.RemoveEdgesAsync([(normalizedPair.Source, normalizedPair.Target)], cancellationToken);
                await DeleteOldRelationVectorsAsync([edge], cancellationToken);
                await relationChunksStore.DeleteAsync(
                    [GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target)],
                    cancellationToken);
                await RemoveFullRelationIndexesAsync(
                    GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target),
                    chunkIds,
                    cancellationToken);
                await bumpQueryRevisionAsync();

                return GraphCurationOperationResult.Success(
                    $"Relation '{GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target)}' deleted.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["source_entity"] = normalizedPair.Source,
                        ["target_entity"] = normalizedPair.Target
                    },
                    new GraphCurationOperationSummary(
                        Merged: false,
                        MergeStatus: "not_required",
                        MergeError: null,
                        OperationStatus: "deleted",
                        TargetEntity: normalizedPair.Source,
                        FinalEntity: GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target),
                        Renamed: false));
            },
            cancellationToken);
    }

    public async Task<GraphCurationOperationResult> DeleteEntityAsync(
        string entityName,
        CancellationToken cancellationToken = default)
    {
        entityName = entityName.Trim();
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return GraphCurationOperationResult.Failure(
                "Entity name is required.",
                "validation",
                "validation_error");
        }

        var lockKeys = new HashSet<string>([EntityLockKey(entityName)], StringComparer.Ordinal);

        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var attemptResult = await ExecuteWithGraphMutationLocksAsync(
                lockKeys,
                async () =>
                {
                    var currentNode = await graphStore.GetNodeAsync(entityName, cancellationToken);
                    if (currentNode is null)
                    {
                        return EntityRenameLockAttempt.Completed(GraphCurationOperationResult.Failure(
                            $"Entity '{entityName}' was not found.",
                            "graph",
                            "not_found"));
                    }

                    var connectedEdges = await GetConnectedEdgesAsync(entityName, entityName, cancellationToken);
                    var requiredLockKeys = BuildEntityDeleteLockKeys(entityName, connectedEdges);
                    if (!requiredLockKeys.SetEquals(lockKeys))
                    {
                        return EntityRenameLockAttempt.Retry(requiredLockKeys);
                    }

                    var relationChunkIds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                    foreach (var edge in connectedEdges)
                    {
                        relationChunkIds[GraphSourceReferenceParser.MakeRelationKey(
                            edge.OriginalSourceId,
                            edge.OriginalTargetId)] = await CollectRelationChunkIdsAsync(edge, cancellationToken);
                    }

                    var entityChunkIds = await CollectEntityChunkIdsAsync(
                        entityName,
                        currentNode.Properties,
                        cancellationToken);

                    await graphStore.RemoveEdgesAsync(
                        connectedEdges
                            .Select(edge => (edge.OriginalSourceId, edge.OriginalTargetId))
                            .Distinct()
                            .ToList(),
                        cancellationToken);
                    await graphStore.DeleteNodeAsync(entityName, cancellationToken);
                    await DeleteOldRelationVectorsAsync(connectedEdges, cancellationToken);
                    await DeleteOldRelationTrackingAsync(connectedEdges, cancellationToken);
                    foreach (var (relationKey, chunkIds) in relationChunkIds)
                    {
                        await RemoveFullRelationIndexesAsync(relationKey, chunkIds, cancellationToken);
                    }

                    await vectorStore.DeleteAsync(
                        EntitiesCollection,
                        [GraphCurationVectorIds.Entity(entityName)],
                        cancellationToken);
                    await entityChunksStore.DeleteAsync([entityName], cancellationToken);
                    await RemoveFullEntityIndexAsync(entityName, entityChunkIds, cancellationToken);
                    await bumpQueryRevisionAsync();

                    return EntityRenameLockAttempt.Completed(GraphCurationOperationResult.Success(
                        $"Entity '{entityName}' deleted.",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["deleted_entity"] = entityName,
                            ["deleted_relations"] = connectedEdges.Count
                        },
                        new GraphCurationOperationSummary(
                            Merged: false,
                            MergeStatus: "not_required",
                            MergeError: null,
                            OperationStatus: "deleted",
                            TargetEntity: null,
                            FinalEntity: entityName,
                            Renamed: false)));
                },
                cancellationToken);

            if (attemptResult.Result is not null)
            {
                return attemptResult.Result;
            }

            lockKeys = attemptResult.RequiredLockKeys;
        }

        return GraphCurationOperationResult.Failure(
            $"Entity '{entityName}' delete lock set changed repeatedly; retry the operation.",
            "graph",
            "retry_failed");
    }

    public async Task<GraphCurationOperationResult> CreateRelationAsync(
        GraphRelationCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var sourceEntity = request.SourceEntity.Trim();
        var targetEntity = request.TargetEntity.Trim();
        if (string.IsNullOrWhiteSpace(sourceEntity) || string.IsNullOrWhiteSpace(targetEntity))
        {
            return GraphCurationOperationResult.Failure(
                "Relation source and target are required.",
                "validation",
                "validation_error");
        }

        var description = GetString(request.RelationData, "description");
        if (string.IsNullOrWhiteSpace(description))
        {
            return GraphCurationOperationResult.Failure(
                "Relation description is required.",
                "validation",
                "validation_error");
        }

        if (!await graphStore.HasNodeAsync(sourceEntity, cancellationToken) ||
            !await graphStore.HasNodeAsync(targetEntity, cancellationToken))
        {
            return GraphCurationOperationResult.Failure(
                "Relation endpoints must exist before creating an edge.",
                "graph",
                "validation_error");
        }

        var normalizedPair = GraphCurationVectorIds.NormalizePair(sourceEntity, targetEntity);
        if (await graphStore.HasEdgeAsync(normalizedPair.Source, normalizedPair.Target, cancellationToken))
        {
            return GraphCurationOperationResult.Failure(
                $"Relation '{GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target)}' already exists.",
                "graph",
                "conflict");
        }

        var edgeData = BuildRelationData(request.RelationData);
        var edge = new RewiredEdge(
            normalizedPair.Source,
            normalizedPair.Target,
            normalizedPair.Source,
            normalizedPair.Target,
            edgeData);
        var relationVector = await CreateRelationVectorDocumentAsync(edge, cancellationToken);

        return await ExecuteWithRelationAndEndpointLocksAsync(
            normalizedPair.Source,
            normalizedPair.Target,
            async () =>
            {
                if (!await graphStore.HasNodeAsync(normalizedPair.Source, cancellationToken) ||
                    !await graphStore.HasNodeAsync(normalizedPair.Target, cancellationToken))
                {
                    return GraphCurationOperationResult.Failure(
                        "Relation endpoints must exist before creating an edge.",
                        "graph",
                        "validation_error");
                }

                if (await graphStore.HasEdgeAsync(normalizedPair.Source, normalizedPair.Target, cancellationToken))
                {
                    return GraphCurationOperationResult.Failure(
                        $"Relation '{GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target)}' already exists.",
                        "graph",
                        "conflict");
                }

                await graphStore.UpsertEdgeAsync(
                    normalizedPair.Source,
                    normalizedPair.Target,
                    edgeData,
                    cancellationToken);
                await UpsertRelationTrackingAsync([edge], cancellationToken);
                await UpsertFullRelationIndexesAsync([edge], cancellationToken);
                await UpsertRelationVectorsAsync([relationVector], cancellationToken);
                await bumpQueryRevisionAsync();

                return GraphCurationOperationResult.Success(
                    $"Relation '{GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target)}' created.",
                    edgeData,
                    new GraphCurationOperationSummary(
                        Merged: false,
                        MergeStatus: "not_required",
                        MergeError: null,
                        OperationStatus: "created",
                        TargetEntity: normalizedPair.Source,
                        FinalEntity: GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target),
                        Renamed: false));
            },
            cancellationToken);
    }

    public async Task<GraphCurationOperationResult> EditRelationAsync(
        GraphRelationEditRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.HasBlankDescription())
        {
            return GraphCurationOperationResult.Failure(
                "Relation description cannot be blank.",
                "validation",
                "validation_error");
        }

        var immutableField = request.UpdatedData.Keys.FirstOrDefault(ImmutableProvenanceFields.Contains);
        if (immutableField is not null)
        {
            return GraphCurationOperationResult.Failure(
                $"Relation field '{immutableField}' cannot be edited.",
                "validation",
                "validation_error");
        }

        var sourceEntity = request.SourceEntity.Trim();
        var targetEntity = request.TargetEntity.Trim();
        if (string.IsNullOrWhiteSpace(sourceEntity) || string.IsNullOrWhiteSpace(targetEntity))
        {
            return GraphCurationOperationResult.Failure(
                "Relation source and target are required.",
                "validation",
                "validation_error");
        }

        var normalizedPair = GraphCurationVectorIds.NormalizePair(sourceEntity, targetEntity);
        return await ExecuteWithRelationAndEndpointLocksAsync(
            normalizedPair.Source,
            normalizedPair.Target,
            async () =>
            {
                var currentEdge = await graphStore.GetEdgeAsync(
                    normalizedPair.Source,
                    normalizedPair.Target,
                    cancellationToken);
                if (currentEdge is null)
                {
                    return GraphCurationOperationResult.Failure(
                        $"Relation '{GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target)}' was not found.",
                        "graph",
                        "not_found");
                }

                var updatedData = currentEdge.Properties.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);

                foreach (var (key, value) in request.UpdatedData)
                {
                    updatedData[key] = value;
                }

                var edge = new RewiredEdge(
                    normalizedPair.Source,
                    normalizedPair.Target,
                    normalizedPair.Source,
                    normalizedPair.Target,
                    updatedData);
                var relationChunkIds = await CollectRelationChunkIdsAsync(edge, cancellationToken);
                var relationChunkOverrides = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [GraphSourceReferenceParser.MakeRelationKey(edge.SourceId, edge.TargetId)] = relationChunkIds
                };
                var relationVector = await CreateRelationVectorDocumentAsync(edge, cancellationToken);

                await graphStore.UpsertEdgeAsync(
                    normalizedPair.Source,
                    normalizedPair.Target,
                    updatedData,
                    cancellationToken);
                await UpsertRelationTrackingAsync([edge], relationChunkOverrides, cancellationToken);
                await UpsertFullRelationIndexesAsync([edge], relationChunkOverrides, cancellationToken);
                await DeleteOldRelationVectorsAsync([edge], cancellationToken);
                await UpsertRelationVectorsAsync([relationVector], cancellationToken);
                await bumpQueryRevisionAsync();

                return GraphCurationOperationResult.Success(
                    $"Relation '{GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target)}' updated.",
                    updatedData,
                    new GraphCurationOperationSummary(
                        Merged: false,
                        MergeStatus: "not_required",
                        MergeError: null,
                        OperationStatus: "updated",
                        TargetEntity: normalizedPair.Source,
                        FinalEntity: GraphSourceReferenceParser.MakeRelationKey(normalizedPair.Source, normalizedPair.Target),
                        Renamed: false));
            },
            cancellationToken);
    }

    private async Task<VectorDocument> BuildEntityVectorDocumentAsync(
        string entityName,
        Dictionary<string, object> entityData,
        CancellationToken cancellationToken)
    {
        var description = GetString(entityData, "description") ?? string.Empty;
        var content = $"{entityName}\n{description}";
        var embedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

        return new VectorDocument
        {
            Id = GraphCurationVectorIds.Entity(entityName),
            Content = content,
            Vector = embedding,
            Metadata = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = GraphCurationVectorIds.Entity(entityName),
                ["content"] = content,
                ["entity_name"] = entityName,
                ["source_id"] = GetString(entityData, "source_id") ?? string.Empty,
                ["description"] = description,
                ["entity_type"] = GetString(entityData, "entity_type") ?? string.Empty,
                ["file_path"] = GetString(entityData, "file_path") ?? string.Empty
            }
        };
    }

    private async Task UpsertEntityTrackingAsync(
        string entityName,
        Dictionary<string, object> entityData,
        CancellationToken cancellationToken)
    {
        await UpsertEntityTrackingAsync(
            entityName,
            ExtractChunkIds(entityData),
            cancellationToken);
    }

    private async Task UpsertEntityTrackingAsync(
        string entityName,
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken)
    {
        var chunkIdsList = chunkIds.Distinct(StringComparer.Ordinal).ToList();
        await entityChunksStore.UpsertAsync(
            new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal)
            {
                [entityName] = new(StringComparer.Ordinal)
                {
                    ["chunk_ids"] = chunkIdsList,
                    ["count"] = chunkIdsList.Count
                }
            },
            cancellationToken);
    }

    private async Task UpsertFullEntityIndexAsync(
        string entityName,
        Dictionary<string, object> entityData,
        CancellationToken cancellationToken,
        string? previousEntityName = null)
    {
        await UpsertFullEntityIndexAsync(
            entityName,
            ExtractChunkIds(entityData),
            cancellationToken,
            previousEntityName);
    }

    private async Task UpsertFullEntityIndexAsync(
        string entityName,
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken,
        string? previousEntityName = null)
    {
        var docIds = await ResolveFullDocIdsAsync(chunkIds, cancellationToken);
        if (docIds.Count == 0)
        {
            logger.LogDebug(
                "No full_doc_id resolved for entity {EntityName}; skipping full_entities index update.",
                entityName);
            return;
        }

        var updates = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (var docId in docIds)
        {
            var existing = await fullEntitiesStore.GetByIdAsync(docId, cancellationToken)
                ?? new Dictionary<string, object>(StringComparer.Ordinal);
            var entityNames = ReadStringList(existing, "entity_names").ToList();

            if (!string.IsNullOrWhiteSpace(previousEntityName))
            {
                entityNames.RemoveAll(name => string.Equals(name, previousEntityName, StringComparison.Ordinal));
            }

            if (!entityNames.Contains(entityName, StringComparer.Ordinal))
            {
                entityNames.Add(entityName);
            }

            var updated = existing.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            updated["entity_names"] = entityNames;
            updated["count"] = entityNames.Count;
            updates[docId] = updated;
        }

        await fullEntitiesStore.UpsertAsync(updates, cancellationToken);
    }

    private async Task UpsertFullRelationIndexesAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        await UpsertFullRelationIndexesAsync(
            edges,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            cancellationToken);
    }

    private async Task UpsertFullRelationIndexesAsync(
        IReadOnlyList<RewiredEdge> edges,
        IReadOnlyDictionary<string, IReadOnlyList<string>> relationChunkOverrides,
        CancellationToken cancellationToken)
    {
        var updates = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            var oldKey = GraphSourceReferenceParser.MakeRelationKey(edge.OriginalSourceId, edge.OriginalTargetId);
            var newKey = GraphSourceReferenceParser.MakeRelationKey(edge.SourceId, edge.TargetId);
            var chunkIds = relationChunkOverrides.TryGetValue(newKey, out var overrideChunkIds)
                ? overrideChunkIds
                : ExtractChunkIds(edge.Properties);
            var docIds = await ResolveFullDocIdsAsync(chunkIds, cancellationToken);
            if (docIds.Count == 0)
            {
                logger.LogDebug(
                    "No full_doc_id resolved for relation {SourceId}->{TargetId}; skipping full_relations index update.",
                    edge.SourceId,
                    edge.TargetId);
                continue;
            }

            foreach (var docId in docIds)
            {
                var existing = updates.TryGetValue(docId, out var pending)
                    ? pending
                    : await fullRelationsStore.GetByIdAsync(docId, cancellationToken)
                        ?? new Dictionary<string, object>(StringComparer.Ordinal);

                var relationKeys = ReadRelationPairKeys(existing, "relation_pairs").ToList();
                relationKeys.RemoveAll(key => string.Equals(key, oldKey, StringComparison.Ordinal));
                if (!relationKeys.Contains(newKey, StringComparer.Ordinal))
                {
                    relationKeys.Add(newKey);
                }

                var updated = existing.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
                updated["relation_pairs"] = relationKeys
                    .Select(key => key.Split(GraphSourceReferenceParser.GraphFieldSep, StringSplitOptions.None))
                    .ToList();
                updated["count"] = relationKeys.Count;
                updates[docId] = updated;
            }
        }

        if (updates.Count == 0)
        {
            return;
        }

        await fullRelationsStore.UpsertAsync(updates, cancellationToken);
    }

    private async Task RemoveFullEntityIndexAsync(
        string entityName,
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken)
    {
        var docIds = await ResolveFullDocIdsAsync(chunkIds, cancellationToken);
        if (docIds.Count == 0)
        {
            return;
        }

        var updates = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (var docId in docIds)
        {
            var existing = await fullEntitiesStore.GetByIdAsync(docId, cancellationToken);
            if (existing is null)
            {
                continue;
            }

            var entityNames = ReadStringList(existing, "entity_names").ToList();
            var removed = entityNames.RemoveAll(name => string.Equals(name, entityName, StringComparison.Ordinal));
            if (removed == 0)
            {
                continue;
            }

            var updated = existing.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            updated["entity_names"] = entityNames;
            updated["count"] = entityNames.Count;
            updates[docId] = updated;
        }

        if (updates.Count == 0)
        {
            return;
        }

        await fullEntitiesStore.UpsertAsync(updates, cancellationToken);
    }

    private async Task RemoveFullRelationIndexesAsync(
        string relationKey,
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken)
    {
        var docIds = await ResolveFullDocIdsAsync(chunkIds, cancellationToken);
        if (docIds.Count == 0)
        {
            return;
        }

        var updates = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (var docId in docIds)
        {
            var existing = await fullRelationsStore.GetByIdAsync(docId, cancellationToken);
            if (existing is null)
            {
                continue;
            }

            var relationKeys = ReadRelationPairKeys(existing, "relation_pairs").ToList();
            var removed = relationKeys.RemoveAll(key => string.Equals(key, relationKey, StringComparison.Ordinal));
            if (removed == 0)
            {
                continue;
            }

            var updated = existing.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            updated["relation_pairs"] = relationKeys
                .Select(key => key.Split(GraphSourceReferenceParser.GraphFieldSep, StringSplitOptions.None))
                .ToList();
            updated["count"] = relationKeys.Count;
            updates[docId] = updated;
        }

        if (updates.Count == 0)
        {
            return;
        }

        await fullRelationsStore.UpsertAsync(updates, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> CollectEntityChunkIdsAsync(
        string entityName,
        Dictionary<string, object> entityData,
        CancellationToken cancellationToken)
    {
        var tracking = await entityChunksStore.GetByIdAsync(entityName, cancellationToken);
        if (tracking is not null)
        {
            var trackingChunkIds = ReadStringList(tracking, "chunk_ids");
            if (trackingChunkIds.Count > 0)
            {
                return trackingChunkIds;
            }
        }

        return ExtractChunkIds(entityData);
    }

    private async Task<IReadOnlyList<string>> CollectRelationChunkIdsAsync(
        RewiredEdge edge,
        CancellationToken cancellationToken)
    {
        var tracking = await relationChunksStore.GetByIdAsync(
            GraphSourceReferenceParser.MakeRelationKey(edge.OriginalSourceId, edge.OriginalTargetId),
            cancellationToken);
        if (tracking is not null)
        {
            var trackingChunkIds = ReadStringList(tracking, "chunk_ids");
            if (trackingChunkIds.Count > 0)
            {
                return trackingChunkIds;
            }
        }

        return ExtractChunkIds(edge.Properties);
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> BuildRelationChunkOverridesByNewKeyAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        var overrides = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            overrides[GraphSourceReferenceParser.MakeRelationKey(edge.SourceId, edge.TargetId)] =
                await CollectRelationChunkIdsAsync(edge, cancellationToken);
        }

        return overrides;
    }

    private static void AddUnique(List<string> target, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (!target.Contains(value, StringComparer.Ordinal))
            {
                target.Add(value);
            }
        }
    }

    private static NormalizedMergeRequest NormalizeMergeRequest(GraphEntityMergeRequest request)
    {
        var targetEntity = request.TargetEntity.Trim();
        if (string.IsNullOrWhiteSpace(targetEntity))
        {
            return NormalizedMergeRequest.Invalid(GraphCurationOperationResult.Failure(
                "Target entity is required.",
                "validation",
                "validation_error"));
        }

        var sourceEntities = request.SourceEntities
            .Select(entity => entity.Trim())
            .Where(entity => entity.Length > 0)
            .ToList();
        if (sourceEntities.Count == 0)
        {
            return NormalizedMergeRequest.Invalid(GraphCurationOperationResult.Failure(
                "At least one source entity is required.",
                "validation",
                "validation_error"));
        }

        if (sourceEntities.Distinct(StringComparer.Ordinal).Count() != sourceEntities.Count)
        {
            return NormalizedMergeRequest.Invalid(GraphCurationOperationResult.Failure(
                "Source entities must be distinct.",
                "validation",
                "validation_error"));
        }

        if (sourceEntities.Contains(targetEntity, StringComparer.Ordinal))
        {
            return NormalizedMergeRequest.Invalid(GraphCurationOperationResult.Failure(
                "Source entities cannot include the target entity.",
                "validation",
                "validation_error"));
        }

        return new NormalizedMergeRequest(sourceEntities, targetEntity, null);
    }

    private async Task<MergePlanResult> BuildMergePlanAsync(
        IReadOnlyList<string> sourceEntities,
        string targetEntity,
        CancellationToken cancellationToken)
    {
        var targetNode = await graphStore.GetNodeAsync(targetEntity, cancellationToken);
        if (targetNode is null)
        {
            return MergePlanResult.Invalid(GraphCurationOperationResult.Failure(
                $"Target entity '{targetEntity}' was not found.",
                "graph",
                "not_found",
                new GraphCurationOperationSummary(
                    Merged: false,
                    MergeStatus: "not_found",
                    MergeError: "target_not_found",
                    OperationStatus: "not_found",
                    TargetEntity: targetEntity,
                    FinalEntity: targetEntity,
                    Renamed: false)));
        }

        var sourceNodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var sourceEntity in sourceEntities)
        {
            var sourceNode = await graphStore.GetNodeAsync(sourceEntity, cancellationToken);
            if (sourceNode is null)
            {
                return MergePlanResult.Invalid(GraphCurationOperationResult.Failure(
                    $"Source entity '{sourceEntity}' was not found.",
                    "graph",
                    "not_found",
                    new GraphCurationOperationSummary(
                        Merged: false,
                        MergeStatus: "not_found",
                        MergeError: "source_not_found",
                        OperationStatus: "not_found",
                        TargetEntity: targetEntity,
                        FinalEntity: targetEntity,
                        Renamed: false)));
            }

            sourceNodes[sourceEntity] = sourceNode;
        }

        var sourceSet = sourceEntities.ToHashSet(StringComparer.Ordinal);
        var sourceEntityChunkIds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (sourceEntity, sourceNode) in sourceNodes)
        {
            sourceEntityChunkIds[sourceEntity] = await CollectEntityChunkIdsAsync(
                sourceEntity,
                sourceNode.Properties,
                cancellationToken);
        }

        var targetChunkIds = new List<string>();
        AddUnique(targetChunkIds, await CollectEntityChunkIdsAsync(
            targetEntity,
            targetNode.Properties,
            cancellationToken));
        foreach (var chunkIds in sourceEntityChunkIds.Values)
        {
            AddUnique(targetChunkIds, chunkIds);
        }

        var targetData = MergeEntityData(targetEntity, targetNode.Properties, sourceNodes.Values.Select(node => node.Properties));
        var oldRelationEdges = new Dictionary<string, RewiredEdge>(StringComparer.Ordinal);
        var transferredEdgesByNewKey = new Dictionary<string, RewiredEdge>(StringComparer.Ordinal);
        var fullRelationReplacements = new List<RewiredEdge>();
        var skippedRelationEdges = new List<RewiredEdge>();

        foreach (var sourceEntity in sourceEntities)
        {
            var edgePairs = await graphStore.GetNodeEdgesAsync(sourceEntity, cancellationToken);
            foreach (var edgePair in edgePairs)
            {
                var oldPair = GraphCurationVectorIds.NormalizePair(edgePair.SourceId, edgePair.TargetId);
                var oldKey = GraphSourceReferenceParser.MakeRelationKey(oldPair.Source, oldPair.Target);
                if (oldRelationEdges.ContainsKey(oldKey))
                {
                    continue;
                }

                var currentEdge = await graphStore.GetEdgeAsync(oldPair.Source, oldPair.Target, cancellationToken);
                if (currentEdge is null)
                {
                    logger.LogWarning(
                        "Connected edge {SourceId}->{TargetId} was listed but could not be loaded during entity merge.",
                        edgePair.SourceId,
                        edgePair.TargetId);
                    continue;
                }

                var oldEdge = new RewiredEdge(
                    oldPair.Source,
                    oldPair.Target,
                    oldPair.Source,
                    oldPair.Target,
                    currentEdge.Properties.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.Ordinal));
                oldRelationEdges[oldKey] = oldEdge;
                var oldRelationChunkIds = await CollectRelationChunkIdsAsync(oldEdge, cancellationToken);

                var otherEntity = string.Equals(oldPair.Source, sourceEntity, StringComparison.Ordinal)
                    ? oldPair.Target
                    : oldPair.Source;
                if (string.Equals(otherEntity, targetEntity, StringComparison.Ordinal) ||
                    sourceSet.Contains(otherEntity))
                {
                    skippedRelationEdges.Add(oldEdge);
                    continue;
                }

                var newPair = GraphCurationVectorIds.NormalizePair(targetEntity, otherEntity);
                var newKey = GraphSourceReferenceParser.MakeRelationKey(newPair.Source, newPair.Target);
                Dictionary<string, object> newProperties;
                var newRelationChunkIds = new List<string>();
                if (transferredEdgesByNewKey.TryGetValue(newKey, out var plannedEdge))
                {
                    newProperties = MergeRelationData(plannedEdge.Properties, oldEdge.Properties);
                    AddUnique(newRelationChunkIds, ExtractChunkIds(plannedEdge.Properties));
                }
                else
                {
                    var existingEdge = await graphStore.GetEdgeAsync(newPair.Source, newPair.Target, cancellationToken);
                    if (existingEdge is null)
                    {
                        newProperties = oldEdge.Properties.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                    }
                    else
                    {
                        var existingRewiredEdge = new RewiredEdge(
                            newPair.Source,
                            newPair.Target,
                            newPair.Source,
                            newPair.Target,
                            existingEdge.Properties.ToDictionary(
                                pair => pair.Key,
                                pair => pair.Value,
                                StringComparer.Ordinal));
                        AddUnique(newRelationChunkIds, await CollectRelationChunkIdsAsync(existingRewiredEdge, cancellationToken));
                        newProperties = MergeRelationData(existingEdge.Properties, oldEdge.Properties);
                    }
                }

                AddUnique(newRelationChunkIds, oldRelationChunkIds);
                if (newRelationChunkIds.Count == 0)
                {
                    AddUnique(newRelationChunkIds, ExtractChunkIds(newProperties));
                }

                newProperties["source_id"] = GraphSourceReferenceParser.Join(newRelationChunkIds);
                var transferredEdge = new RewiredEdge(
                    oldPair.Source,
                    oldPair.Target,
                    newPair.Source,
                    newPair.Target,
                    newProperties);
                transferredEdgesByNewKey[newKey] = transferredEdge;
                fullRelationReplacements.Add(transferredEdge);
            }
        }

        var requiredLockKeys = BuildEntityMergeLockKeys(
            sourceEntities,
            targetEntity,
            oldRelationEdges.Values,
            transferredEdgesByNewKey.Values);
        var plan = new MergePlan(
            targetEntity,
            sourceEntities,
            targetData,
            targetChunkIds,
            sourceEntityChunkIds,
            oldRelationEdges.Values.ToList(),
            transferredEdgesByNewKey.Values.ToList(),
            fullRelationReplacements,
            skippedRelationEdges);

        return new MergePlanResult(plan, requiredLockKeys, null);
    }

    private async Task<GraphCurationOperationResult> ApplyMergePlanAsync(
        MergePlan plan,
        VectorDocument entityVector,
        IReadOnlyList<VectorDocument> relationVectors,
        CancellationToken cancellationToken)
    {
        await graphStore.UpsertNodeAsync(plan.TargetEntity, plan.TargetData, cancellationToken);
        await UpsertRewiredEdgesAsync(plan.NewRelationEdges, cancellationToken);
        await graphStore.RemoveEdgesAsync(
            plan.OldRelationEdges
                .Select(edge => (edge.OriginalSourceId, edge.OriginalTargetId))
                .Distinct()
                .ToList(),
            cancellationToken);

        foreach (var sourceEntity in plan.SourceEntities)
        {
            await graphStore.DeleteNodeAsync(sourceEntity, cancellationToken);
        }

        foreach (var sourceEntity in plan.SourceEntities)
        {
            await UpsertFullEntityIndexAsync(
                plan.TargetEntity,
                plan.SourceEntityChunkIds[sourceEntity],
                cancellationToken,
                sourceEntity);
        }

        await UpsertFullEntityIndexAsync(plan.TargetEntity, plan.TargetChunkIds, cancellationToken);
        await UpsertFullRelationIndexesAsync(plan.FullRelationReplacements, cancellationToken);
        foreach (var skippedEdge in plan.SkippedRelationEdges)
        {
            var chunkIds = await CollectRelationChunkIdsAsync(skippedEdge, cancellationToken);
            await RemoveFullRelationIndexesAsync(
                GraphSourceReferenceParser.MakeRelationKey(
                    skippedEdge.OriginalSourceId,
                    skippedEdge.OriginalTargetId),
                chunkIds,
                cancellationToken);
        }

        await DeleteOldRelationTrackingAsync(plan.OldRelationEdges, cancellationToken);
        await UpsertRelationTrackingAsync(plan.NewRelationEdges, cancellationToken);
        await entityChunksStore.DeleteAsync(plan.SourceEntities, cancellationToken);
        await UpsertEntityTrackingAsync(plan.TargetEntity, plan.TargetChunkIds, cancellationToken);
        await DeleteOldRelationVectorsAsync(plan.OldRelationEdges, cancellationToken);
        await vectorStore.DeleteAsync(
            EntitiesCollection,
            plan.SourceEntities.Select(GraphCurationVectorIds.Entity).ToList(),
            cancellationToken);
        await vectorStore.UpsertAsync(EntitiesCollection, [entityVector], cancellationToken);
        await UpsertRelationVectorsAsync(relationVectors, cancellationToken);
        await bumpQueryRevisionAsync();

        var data = plan.TargetData.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        data["transferred_relations"] = plan.NewRelationEdges.Count;
        data["deleted_entities"] = plan.SourceEntities.ToList();

        return GraphCurationOperationResult.Success(
            $"Merged {plan.SourceEntities.Count} entity/entities into '{plan.TargetEntity}'.",
            data,
            new GraphCurationOperationSummary(
                Merged: true,
                MergeStatus: "merged",
                MergeError: null,
                OperationStatus: "merged",
                TargetEntity: plan.TargetEntity,
                FinalEntity: plan.TargetEntity,
                Renamed: false));
    }

    private static Dictionary<string, object> MergeEntityData(
        string targetEntity,
        Dictionary<string, object> targetData,
        IEnumerable<Dictionary<string, object>> sourceEntities)
    {
        var merged = targetData.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        foreach (var sourceData in sourceEntities)
        {
            merged["description"] = JoinUnique(GetString(merged, "description"), GetString(sourceData, "description"));
            merged["source_id"] = JoinUnique(GetString(merged, "source_id"), GetString(sourceData, "source_id"));
            merged["file_path"] = JoinUnique(GetString(merged, "file_path"), GetString(sourceData, "file_path"));

            if (string.IsNullOrWhiteSpace(GetString(merged, "entity_type")))
            {
                merged["entity_type"] = GetString(sourceData, "entity_type") ?? string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(GetString(merged, "entity_id")))
        {
            merged["entity_id"] = targetEntity;
        }

        if (string.IsNullOrWhiteSpace(GetString(merged, "entity_name")))
        {
            merged["entity_name"] = targetEntity;
        }

        merged["description"] = GetString(merged, "description") ?? string.Empty;
        merged["source_id"] = GetString(merged, "source_id") ?? string.Empty;
        merged["file_path"] = GetString(merged, "file_path") ?? string.Empty;
        merged["entity_type"] = GetString(merged, "entity_type") ?? string.Empty;
        return merged;
    }

    private static Dictionary<string, object> MergeRelationData(
        Dictionary<string, object> target,
        Dictionary<string, object> source)
    {
        var merged = target.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        merged["description"] = JoinUnique(GetString(target, "description"), GetString(source, "description"));
        merged["keywords"] = JoinUnique(GetString(target, "keywords"), GetString(source, "keywords"));
        merged["source_id"] = JoinUnique(GetString(target, "source_id"), GetString(source, "source_id"));
        merged["file_path"] = JoinUnique(GetString(target, "file_path"), GetString(source, "file_path"));
        merged["weight"] = Math.Max(GetDouble(target, "weight"), GetDouble(source, "weight"));
        merged["created_at"] = GetString(target, "created_at")
            ?? GetString(source, "created_at")
            ?? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return BuildRelationData(merged);
    }

    private static string JoinUnique(string? left, string? right)
    {
        var values = new List<string>();
        AddSplitValues(values, left);
        AddSplitValues(values, right);
        return GraphSourceReferenceParser.Join(values);
    }

    private static void AddSplitValues(List<string> values, string? source)
    {
        foreach (var value in GraphSourceReferenceParser.Split(source))
        {
            if (!values.Contains(value, StringComparer.Ordinal))
            {
                values.Add(value);
            }
        }
    }

    private static Dictionary<string, object> BuildEntityData(
        string entityName,
        Dictionary<string, object> sourceData)
    {
        var entityData = sourceData.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        entityData["entity_id"] = entityName;
        entityData["entity_name"] = entityName;
        entityData["entity_type"] = GetString(sourceData, "entity_type") ?? string.Empty;
        entityData["description"] = GetString(sourceData, "description") ?? string.Empty;
        entityData["source_id"] = GetString(sourceData, "source_id") ?? string.Empty;
        entityData["file_path"] = GetString(sourceData, "file_path") ?? string.Empty;
        entityData["created_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        return entityData;
    }

    private static Dictionary<string, object> BuildRelationData(Dictionary<string, object> sourceData)
    {
        var relationData = sourceData.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        relationData["description"] = GetString(sourceData, "description") ?? string.Empty;
        relationData["keywords"] = GetString(sourceData, "keywords") ?? string.Empty;
        relationData["source_id"] = GetString(sourceData, "source_id") ?? string.Empty;
        relationData["file_path"] = GetString(sourceData, "file_path") ?? string.Empty;
        relationData["weight"] = sourceData.TryGetValue("weight", out var weight) ? weight : 0.0;
        relationData["created_at"] = GetString(sourceData, "created_at")
            ?? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        return relationData;
    }

    private static List<string> ExtractChunkIds(Dictionary<string, object> entityData)
    {
        var sourceId = GetString(entityData, "source_id");
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return [];
        }

        return sourceId
            .Split(SourceSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> ResolveFullDocIdsAsync(
        Dictionary<string, object> sourceData,
        CancellationToken cancellationToken)
    {
        return await ResolveFullDocIdsAsync(ExtractChunkIds(sourceData), cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ResolveFullDocIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken)
    {
        var docIds = new List<string>();
        foreach (var chunkId in chunkIds.Distinct(StringComparer.Ordinal))
        {
            var chunk = await textChunksStore.GetByIdAsync(chunkId, cancellationToken);
            var docId = GetString(chunk, "full_doc_id");
            if (!string.IsNullOrWhiteSpace(docId) && !docIds.Contains(docId, StringComparer.Ordinal))
            {
                docIds.Add(docId);
            }
        }

        return docIds;
    }

    private static string? GetString(Dictionary<string, object>? data, string key)
    {
        if (data is null || !data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement json => json.ToString(),
            _ => value.ToString()
        };
    }

    private static IReadOnlyList<string> ReadStringList(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        return value switch
        {
            JsonElement json => ReadJsonStringList(json),
            IEnumerable<string> strings => strings
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            IEnumerable<object> objects => objects
                .Select(item => item?.ToString()?.Trim() ?? string.Empty)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            string text when !string.IsNullOrWhiteSpace(text) => [text.Trim()],
            _ => []
        };
    }

    private static IReadOnlyList<string> ReadJsonStringList(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray()
                .Select(ReadJsonScalar)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? [] : [value.GetString()!.Trim()],
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => [value.ToString()],
            _ => []
        };
    }

    private static string ReadJsonScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => string.Empty
        };
    }

    private static IReadOnlyList<string> ReadRelationPairKeys(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        var keys = value switch
        {
            JsonElement json => ReadJsonRelationPairKeys(json),
            IEnumerable<string[]> arrays => arrays
                .Where(pair => pair.Length >= 2)
                .Select(pair => GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1])),
            IEnumerable<object> objects => objects.SelectMany(ReadObjectRelationPairKeys),
            _ => []
        };

        return keys.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> ReadObjectRelationPairKeys(object? value)
    {
        switch (value)
        {
            case JsonElement json:
                return ReadJsonRelationPairKeys(json);
            case IEnumerable<string> strings:
            {
                var pair = strings.ToList();
                return pair.Count >= 2 ? [GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1])] : [];
            }
            case IEnumerable<object> objects:
            {
                var pair = objects.Select(item => item?.ToString() ?? string.Empty)
                    .Where(item => item.Length > 0)
                    .ToList();
                return pair.Count >= 2 ? [GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1])] : [];
            }
            case string text:
            {
                var pair = GraphSourceReferenceParser.Split(text);
                return pair.Count >= 2 ? [GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1])] : [];
            }
            default:
                return [];
        }
    }

    private static IReadOnlyList<string> ReadJsonRelationPairKeys(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var keys = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                var pair = item.EnumerateArray()
                    .Select(ReadJsonScalar)
                    .Where(text => text.Length > 0)
                    .ToList();
                if (pair.Count >= 2)
                {
                    keys.Add(GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1]));
                }
            }
            else if (item.ValueKind == JsonValueKind.String)
            {
                var pair = GraphSourceReferenceParser.Split(item.GetString());
                if (pair.Count >= 2)
                {
                    keys.Add(GraphSourceReferenceParser.MakeRelationKey(pair[0], pair[1]));
                }
            }
        }

        return keys;
    }

    private async Task<List<VectorDocument>> CreateRelationVectorDocumentsAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        var documents = new List<VectorDocument>();
        foreach (var edge in edges)
        {
            documents.Add(await CreateRelationVectorDocumentAsync(edge, cancellationToken));
        }

        return documents;
    }

    private async Task<VectorDocument> CreateRelationVectorDocumentAsync(
        RewiredEdge edge,
        CancellationToken cancellationToken)
    {
        return await CreateRelationVectorDocumentAsync(
            edge.SourceId,
            edge.TargetId,
            edge.Properties,
            cancellationToken);
    }

    private async Task<VectorDocument> CreateRelationVectorDocumentAsync(
        string sourceId,
        string targetId,
        Dictionary<string, object> properties,
        CancellationToken cancellationToken)
    {
        var normalizedPair = GraphCurationVectorIds.NormalizePair(sourceId, targetId);
        var description = GetString(properties, "description") ?? string.Empty;
        var keywords = GetString(properties, "keywords") ?? string.Empty;
        var content = $"{normalizedPair.Source}\n{normalizedPair.Target}\n{keywords}\n{description}";
        var vectorId = GraphCurationVectorIds.Relation(normalizedPair.Source, normalizedPair.Target);
        var embedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

        return new VectorDocument
        {
            Id = vectorId,
            Content = content,
            Vector = embedding,
            Metadata = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = vectorId,
                ["content"] = content,
                ["src_id"] = normalizedPair.Source,
                ["tgt_id"] = normalizedPair.Target,
                ["source_id"] = GetString(properties, "source_id") ?? string.Empty,
                ["description"] = description,
                ["keywords"] = keywords,
                ["weight"] = GetDouble(properties, "weight"),
                ["file_path"] = GetString(properties, "file_path") ?? string.Empty
            }
        };
    }

    private async Task<List<RewiredEdge>> GetConnectedEdgesAsync(
        string currentName,
        string finalName,
        CancellationToken cancellationToken)
    {
        var edgePairs = await graphStore.GetNodeEdgesAsync(currentName, cancellationToken);
        var rewiredEdges = new List<RewiredEdge>();

        foreach (var edgePair in edgePairs)
        {
            var edge = await graphStore.GetEdgeAsync(edgePair.SourceId, edgePair.TargetId, cancellationToken);
            if (edge is null)
            {
                logger.LogWarning(
                    "Connected edge {SourceId}->{TargetId} was listed but could not be loaded during entity rename.",
                    edgePair.SourceId,
                    edgePair.TargetId);
                continue;
            }

            var rewiredSourceId = string.Equals(edgePair.SourceId, currentName, StringComparison.Ordinal)
                ? finalName
                : edgePair.SourceId;
            var rewiredTargetId = string.Equals(edgePair.TargetId, currentName, StringComparison.Ordinal)
                ? finalName
                : edgePair.TargetId;

            rewiredEdges.Add(new RewiredEdge(
                edgePair.SourceId,
                edgePair.TargetId,
                rewiredSourceId,
                rewiredTargetId,
                edge.Properties.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal)));
        }

        return rewiredEdges;
    }

    private async Task UpsertRewiredEdgesAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        foreach (var edge in edges)
        {
            await graphStore.UpsertEdgeAsync(
                edge.SourceId,
                edge.TargetId,
                edge.Properties,
                cancellationToken);
        }
    }

    private async Task DeleteOldRelationVectorsAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        var ids = edges
            .SelectMany(edge => GraphCurationVectorIds.RelationIds(edge.OriginalSourceId, edge.OriginalTargetId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return;
        }

        await vectorStore.DeleteAsync(RelationshipsCollection, ids, cancellationToken);
    }

    private async Task UpsertRelationVectorsAsync(
        IReadOnlyList<VectorDocument> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return;
        }

        await vectorStore.UpsertAsync(RelationshipsCollection, documents, cancellationToken);
    }

    private async Task DeleteOldRelationTrackingAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        var keys = edges
            .Select(edge => GraphSourceReferenceParser.MakeRelationKey(edge.OriginalSourceId, edge.OriginalTargetId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (keys.Count == 0)
        {
            return;
        }

        await relationChunksStore.DeleteAsync(keys, cancellationToken);
    }

    private async Task UpsertRelationTrackingAsync(
        IReadOnlyList<RewiredEdge> edges,
        CancellationToken cancellationToken)
    {
        await UpsertRelationTrackingAsync(
            edges,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            cancellationToken);
    }

    private async Task UpsertRelationTrackingAsync(
        IReadOnlyList<RewiredEdge> edges,
        IReadOnlyDictionary<string, IReadOnlyList<string>> relationChunkOverrides,
        CancellationToken cancellationToken)
    {
        var data = edges.ToDictionary(
            edge => GraphSourceReferenceParser.MakeRelationKey(edge.SourceId, edge.TargetId),
            edge =>
            {
                var relationKey = GraphSourceReferenceParser.MakeRelationKey(edge.SourceId, edge.TargetId);
                var chunkIds = relationChunkOverrides.TryGetValue(relationKey, out var overrideChunkIds)
                    ? overrideChunkIds.Distinct(StringComparer.Ordinal).ToList()
                    : ExtractChunkIds(edge.Properties);
                return new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["chunk_ids"] = chunkIds,
                    ["count"] = chunkIds.Count
                };
            },
            StringComparer.Ordinal);

        if (data.Count == 0)
        {
            return;
        }

        await relationChunksStore.UpsertAsync(data, cancellationToken);
    }

    private static double GetDouble(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            double typed => typed,
            float typed => typed,
            decimal typed => (double)typed,
            int typed => typed,
            long typed => typed,
            _ when double.TryParse(
                value.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => 0
        };
    }

    private async Task<T> ExecuteWithEntityLocksAsync<T>(
        IEnumerable<string> entityNames,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return await ExecuteWithGraphMutationLocksAsync(
            entityNames.Select(EntityLockKey),
            operation,
            cancellationToken);
    }

    private async Task<T> ExecuteWithRelationAndEndpointLocksAsync<T>(
        string sourceEntity,
        string targetEntity,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return await ExecuteWithGraphMutationLocksAsync(
            [
                EntityLockKey(sourceEntity),
                EntityLockKey(targetEntity),
                RelationLockKey(sourceEntity, targetEntity)
            ],
            operation,
            cancellationToken);
    }

    private static HashSet<string> BuildEntityRenameLockKeys(
        string currentName,
        string finalName,
        IReadOnlyList<RewiredEdge> connectedEdges)
    {
        var lockKeys = new HashSet<string>(
            [EntityLockKey(currentName), EntityLockKey(finalName)],
            StringComparer.Ordinal);

        foreach (var edge in connectedEdges)
        {
            lockKeys.Add(RelationLockKey(edge.OriginalSourceId, edge.OriginalTargetId));
            lockKeys.Add(RelationLockKey(edge.SourceId, edge.TargetId));
        }

        return lockKeys;
    }

    private static HashSet<string> BuildEntityMergeInitialLockKeys(
        IReadOnlyList<string> sourceEntities,
        string targetEntity)
    {
        var lockKeys = new HashSet<string>([EntityLockKey(targetEntity)], StringComparer.Ordinal);
        foreach (var sourceEntity in sourceEntities)
        {
            lockKeys.Add(EntityLockKey(sourceEntity));
        }

        return lockKeys;
    }

    private static HashSet<string> BuildEntityMergeLockKeys(
        IReadOnlyList<string> sourceEntities,
        string targetEntity,
        IEnumerable<RewiredEdge> oldRelationEdges,
        IEnumerable<RewiredEdge> newRelationEdges)
    {
        var lockKeys = BuildEntityMergeInitialLockKeys(sourceEntities, targetEntity);
        foreach (var edge in oldRelationEdges)
        {
            lockKeys.Add(RelationLockKey(edge.OriginalSourceId, edge.OriginalTargetId));
        }

        foreach (var edge in newRelationEdges)
        {
            lockKeys.Add(RelationLockKey(edge.SourceId, edge.TargetId));
        }

        return lockKeys;
    }

    private static HashSet<string> BuildEntityDeleteLockKeys(
        string entityName,
        IReadOnlyList<RewiredEdge> connectedEdges)
    {
        var lockKeys = new HashSet<string>([EntityLockKey(entityName)], StringComparer.Ordinal);
        foreach (var edge in connectedEdges)
        {
            lockKeys.Add(RelationLockKey(edge.OriginalSourceId, edge.OriginalTargetId));
        }

        return lockKeys;
    }

    private async Task<T> ExecuteWithGraphMutationLocksAsync<T>(
        IEnumerable<string> lockKeys,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var locks = lockKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(key => graphMutationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1)))
            .ToList();

        var acquiredLocks = 0;

        try
        {
            foreach (var semaphore in locks)
            {
                await semaphore.WaitAsync(cancellationToken);
                acquiredLocks++;
            }

            return await operation();
        }
        finally
        {
            for (var i = acquiredLocks - 1; i >= 0; i--)
            {
                locks[i].Release();
            }
        }
    }

    private static string EntityLockKey(string entityName) =>
        "entity:" + entityName.Trim();

    private static string RelationLockKey(string sourceEntity, string targetEntity) =>
        "relation:" + GraphSourceReferenceParser.MakeRelationKey(sourceEntity, targetEntity);

    private sealed record EntityRenameLockAttempt(
        GraphCurationOperationResult? Result,
        HashSet<string> RequiredLockKeys)
    {
        public static EntityRenameLockAttempt Completed(GraphCurationOperationResult result) =>
            new(result, []);

        public static EntityRenameLockAttempt Retry(HashSet<string> requiredLockKeys) =>
            new(null, requiredLockKeys);
    }

    private sealed record NormalizedMergeRequest(
        IReadOnlyList<string>? SourceEntities,
        string? TargetEntity,
        GraphCurationOperationResult? Failure)
    {
        public static NormalizedMergeRequest Invalid(GraphCurationOperationResult failure) =>
            new(null, null, failure);
    }

    private sealed record MergePlanResult(
        MergePlan? Plan,
        HashSet<string> RequiredLockKeys,
        GraphCurationOperationResult? Failure)
    {
        public static MergePlanResult Invalid(GraphCurationOperationResult failure) =>
            new(null, [], failure);
    }

    private sealed record MergePlan(
        string TargetEntity,
        IReadOnlyList<string> SourceEntities,
        Dictionary<string, object> TargetData,
        IReadOnlyList<string> TargetChunkIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> SourceEntityChunkIds,
        IReadOnlyList<RewiredEdge> OldRelationEdges,
        IReadOnlyList<RewiredEdge> NewRelationEdges,
        IReadOnlyList<RewiredEdge> FullRelationReplacements,
        IReadOnlyList<RewiredEdge> SkippedRelationEdges);

    private sealed record RewiredEdge(
        string OriginalSourceId,
        string OriginalTargetId,
        string SourceId,
        string TargetId,
        Dictionary<string, object> Properties);
}
