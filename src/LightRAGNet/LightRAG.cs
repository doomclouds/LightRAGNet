using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Models;
using LightRAGNet.Services.DocumentDeletion;
using LightRAGNet.Services.DocumentLifecycle;
using LightRAGNet.Services.DocumentProcessing;
using LightRAGNet.Services.KnowledgeGraphMerge;
using LightRAGNet.Services.Query;
using LightRAGNet.Services.QueryCache;
using LightRAGNet.Services.RetrievalContext;
using LightRAGNet.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace LightRAGNet;

/// <summary>
/// LightRAG main class
/// Reference: Python version lightrag.py
/// </summary>
public class LightRAG(
    ILLMService llmService,
    IVectorStore vectorStore,
    DocumentProcessingService documentProcessingService,
    KnowledgeGraphMergeService knowledgeGraphMergeService,
    RetrievalContextService retrievalContextService,
    NaiveQueryService naiveQueryService,
    LightRagLlmCacheService llmCacheService,
    ITokenizer tokenizer,
    [FromKeyedServices(KVContracts.TextChunks)]
    IKVStore textChunksStore,
    [FromKeyedServices(KVContracts.FullDocs)]
    IKVStore fullDocsStore,
    [FromKeyedServices(KVContracts.FullEntities)]
    IKVStore fullEntitiesStore,
    [FromKeyedServices(KVContracts.FullRelations)]
    IKVStore fullRelationsStore,
    [FromKeyedServices(KVContracts.EntityChunks)]
    IKVStore entityChunksStore,
    [FromKeyedServices(KVContracts.RelationChunks)]
    IKVStore relationChunksStore,
    [FromKeyedServices(KVContracts.LLMCache)]
    IKVStore llmCacheStore,
    DocumentLifecycleService documentLifecycleService,
    DocumentDeletionService documentDeletionService,
    ILogger<LightRAG> logger)
{
    /// <summary>
    /// Task state changed event
    /// </summary>
    public event EventHandler<TaskState>? TaskStateChanged;

    /// <summary>
    /// Task state buffer (for receiving state updates from various stages)
    /// </summary>
    private readonly BufferBlock<TaskState> _taskStateBuffer = new();

    /// <summary>
    /// Background task for processing state updates and dispatching events
    /// </summary>
    private Task? _stateProcessorTask;
    
    /// <summary>
    /// Initialize state processor
    /// </summary>
    private void InitializeStateProcessor()
    {
        _stateProcessorTask = Task.Run(async () =>
        {
            while (await _taskStateBuffer.OutputAvailableAsync())
            {
                try
                {
                    var state = await _taskStateBuffer.ReceiveAsync();
                    TaskStateChanged?.Invoke(this, state);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing task state update");
                }
            }
        });
    }

    /// <summary>
    /// Post task state update (for direct posting from within LightRAG)
    /// </summary>
    private void PostTaskState(TaskState state)
    {
        _taskStateBuffer.Post(state);
    }
    /// <summary>
    /// Insert document
    /// Reference: Python version ainsert function
    /// </summary>
    public async Task<string> InsertAsync(
        string content,
        string? docId = null,
        string? filePath = null,
        CancellationToken cancellationToken = default)
    {
        // Initialize state processor (if not already initialized)
        if (_stateProcessorTask == null || _stateProcessorTask.IsCompleted)
        {
            InitializeStateProcessor();
        }

        // 1. Prepare lifecycle status and duplicate gate
        var ingestion = await documentLifecycleService.PrepareIngestionAsync(
            content,
            docId,
            filePath,
            cancellationToken: cancellationToken);

        docId = ingestion.DocId;
        filePath = ingestion.StatusRecord.FilePath;

        if (ingestion.IsDuplicate && ingestion.StatusRecord.Status == DocumentLifecycleStatus.Processed)
        {
            logger.LogWarning("Document {DocId} already exists", docId);
            PostTaskState(new TaskState
            {
                Stage = TaskStage.Completed,
                Current = 1,
                Total = 1,
                Description = "Document already exists, skipping insertion",
                DocId = docId
            });
            return docId;
        }

        var failureStage = "chunking";

        try
        {
            await documentLifecycleService.StartProcessingAsync(
                ingestion.Workspace,
                docId,
                cancellationToken);

            // 2. Document chunking (batch processing operation)
            PostTaskState(new TaskState
            {
                Stage = TaskStage.DocumentChunking,
                Current = 0,
                Total = 0,
                Description = "Chunking document",
                DocId = docId
            });

            var chunks = documentProcessingService.ChunkDocument(
                content,
                docId,
                filePath);

            await documentLifecycleService.RecordChunksAsync(
                ingestion.Workspace,
                docId,
                chunks,
                cancellationToken);

            PostTaskState(new TaskState
            {
                Stage = TaskStage.DocumentChunking,
                Current = 0,
                Total = 0,
                Description = $"Document chunked into {chunks.Count} chunks",
                DocId = docId
            });

            logger.LogInformation(
                "Document {DocId} split into {ChunkCount} chunks",
                docId,
                chunks.Count);

            // 3. Process each chunk in parallel
            failureStage = "process_chunks";
            PostTaskState(new TaskState
            {
                Stage = TaskStage.ProcessingChunks,
                Current = 0,
                Total = chunks.Count,
                Description = "Processing document chunks (vectorization and entity extraction)",
                DocId = docId
            });

            var processedCount = 0;
            var chunkTasks = chunks.Select(async chunk =>
            {
                try
                {
                    var result = await documentProcessingService.ProcessChunkAsync(chunk, cancellationToken);

                    var current = Interlocked.Increment(ref processedCount);
                    PostTaskState(new TaskState
                    {
                        Stage = TaskStage.ProcessingChunks,
                        Current = current,
                        Total = chunks.Count,
                        Description = $"Processing document chunk {current}/{chunks.Count}",
                        DocId = docId
                    });
                    return result;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing chunk {ChunkId}, order {Order}, tokens {Tokens}",
                        chunk.Id, chunk.ChunkOrderIndex, chunk.Tokens);
                    throw;
                }
            });
            var chunkResults = await Task.WhenAll(chunkTasks);

            // 4. Store text chunks (batch operation)
            failureStage = "store_text_chunks";
            PostTaskState(new TaskState
            {
                Stage = TaskStage.StoringTextChunks,
                Current = 0,
                Total = 0,
                Description = $"Storing text chunks: {chunks.Count} chunks",
                DocId = docId
            });

            var chunkData = chunks.ToDictionary(
                c => c.Id,
                c => new Dictionary<string, object>
                {
                    ["content"] = c.Content,
                    ["tokens"] = c.Tokens,
                    ["chunk_order_index"] = c.ChunkOrderIndex,
                    ["full_doc_id"] = c.FullDocId,
                    ["file_path"] = c.FilePath
                });

            await textChunksStore.UpsertAsync(chunkData, cancellationToken);

            PostTaskState(new TaskState
            {
                Stage = TaskStage.StoringTextChunks,
                Current = 0,
                Total = 0,
                Description = $"Text chunks stored: {chunks.Count} chunks",
                DocId = docId
            });

            // 5. Store document chunk vectors (batch operation)
            failureStage = "store_chunk_vectors";
            PostTaskState(new TaskState
            {
                Stage = TaskStage.StoringChunkVectors,
                Current = 0,
                Total = 0,
                Description = $"Storing document chunk vectors: {chunks.Count} chunks",
                DocId = docId
            });

            // Reference Python version: chunks_vdb meta_fields are {"full_doc_id", "content", "file_path"}
            // Plus automatically added id, workspace_id, created_at
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var workspaceId = ingestion.Workspace;

            var vectorDocs = chunkResults.Select(cr =>
            {
                var chunk = chunks.First(c => c.Id == cr.ChunkId);
                return new VectorDocument
                {
                    Id = cr.ChunkId,
                    Vector = cr.Embedding,
                    Content = chunk.Content,
                    Metadata = new Dictionary<string, object>
                    {
                        ["id"] = cr.ChunkId, // chunk ID
                        ["workspace_id"] = workspaceId, // Workspace ID
                        ["created_at"] = currentTime, // Unix timestamp
                        ["content"] = chunk.Content, // Document content
                        ["full_doc_id"] = docId, // Full document ID
                        ["file_path"] = filePath // File path
                    }
                };
            }).ToList();

            await vectorStore.UpsertAsync("chunks", vectorDocs, cancellationToken);

            PostTaskState(new TaskState
            {
                Stage = TaskStage.StoringChunkVectors,
                Current = 0,
                Total = 0,
                Description = $"Document chunk vectors stored: {chunks.Count} chunks",
                DocId = docId
            });

            // 6. Merge entities and relationships (link data flow)
            failureStage = "merge_graph";
            await knowledgeGraphMergeService.MergeEntitiesAndRelationsAsync(
                chunkResults.ToList(), docId, _taskStateBuffer, cancellationToken: cancellationToken);

            // 7. Store full document (single operation, but also a batch storage operation)
            failureStage = "store_full_document";
            PostTaskState(new TaskState
            {
                Stage = TaskStage.StoringFullDocument,
                Current = 0,
                Total = 0,
                Description = "Storing full document",
                DocId = docId
            });

            await fullDocsStore.UpsertAsync(new Dictionary<string, Dictionary<string, object>>
            {
                [docId] = new()
                {
                    ["content"] = content,
                    ["file_path"] = filePath,
                    ["chunks_count"] = chunks.Count,
                    ["chunks_list"] = chunks.Select(c => c.Id).ToList()
                }
            }, cancellationToken);

            PostTaskState(new TaskState
            {
                Stage = TaskStage.StoringFullDocument,
                Current = 0,
                Total = 0,
                Description = "Full document stored",
                DocId = docId
            });

            // 8. Persist
            failureStage = "persist";
            PostTaskState(new TaskState
            {
                Stage = TaskStage.Persisting,
                Current = 0,
                Total = 7,
                Description = "Persisting data",
                DocId = docId
            });

            var persistTasks = new[]
            {
                textChunksStore.IndexDoneCallbackAsync(cancellationToken),
                fullDocsStore.IndexDoneCallbackAsync(cancellationToken),
                fullEntitiesStore.IndexDoneCallbackAsync(cancellationToken),
                fullRelationsStore.IndexDoneCallbackAsync(cancellationToken),
                entityChunksStore.IndexDoneCallbackAsync(cancellationToken),
                relationChunksStore.IndexDoneCallbackAsync(cancellationToken),
            };

            for (var i = 0; i < persistTasks.Length; i++)
            {
                await persistTasks[i];
                PostTaskState(new TaskState
                {
                    Stage = TaskStage.Persisting,
                    Current = i + 1,
                    Total = persistTasks.Length,
                    Description = $"Persisting data {i + 1}/{persistTasks.Length}",
                    DocId = docId
                });
            }

            await documentLifecycleService.MarkProcessedAsync(
                ingestion.Workspace,
                docId,
                cancellationToken);

            await llmCacheService.BumpWorkspaceQueryRevisionAsync(
                ingestion.Workspace,
                CancellationToken.None);

            logger.LogInformation("Document {DocId} inserted successfully", docId);

            PostTaskState(new TaskState
            {
                Stage = TaskStage.Completed,
                Current = 1,
                Total = 1,
                Description = "Document insertion completed",
                DocId = docId
            });

            return docId;
        }
        catch (Exception ex)
        {
            try
            {
                await documentLifecycleService.MarkFailedAsync(
                    ingestion.Workspace,
                    docId,
                    failureStage,
                    ex.Message,
                    cancellationToken);
            }
            catch (Exception markFailedException)
            {
                logger.LogError(
                    markFailedException,
                    "Failed to mark document {DocId} lifecycle failure at stage {Stage}",
                    docId,
                    failureStage);
            }

            throw;
        }
    }

    public async Task<DocumentDeletionResult> DeleteDocumentAsync(
        string docId,
        bool deleteLlmCache = false,
        CancellationToken cancellationToken = default)
    {
        if (_stateProcessorTask == null || _stateProcessorTask.IsCompleted)
        {
            InitializeStateProcessor();
        }

        var workspace = documentLifecycleService.GetDefaultWorkspace();
        var plan = await documentLifecycleService.CreateDeletionPlanAsync(
            workspace,
            docId,
            deleteLlmCache,
            cancellationToken);

        if (!plan.Found)
        {
            var fallbackFullDoc = await fullDocsStore.GetByIdAsync(docId, cancellationToken);
            if (fallbackFullDoc is null)
            {
                return new DocumentDeletionResult(
                    docId,
                    plan.Workspace,
                    Found: false,
                    Succeeded: false,
                    Stage: string.Empty,
                    Message: "Document not found.");
            }

            plan = new DocumentDeletionPlan
            {
                DocId = docId,
                Workspace = plan.Workspace,
                Found = true,
                ChunkIds = ReadStringList(fallbackFullDoc, "chunks_list"),
                DeleteFullDocument = true,
                DeleteTextChunks = true,
                DeleteChunkVectors = true,
                DeleteDocumentGraphMetadata = true,
                DeleteLlmCache = deleteLlmCache
            };
        }

        PostTaskState(new TaskState
        {
            Stage = TaskStage.DeletingDocument,
            Current = 0,
            Total = 0,
            Description = "Deleting document",
            DocId = docId
        });

        var result = await documentDeletionService.DeleteAsync(
            new DocumentDeletionRequest(
                plan.Workspace,
                docId,
                plan.ChunkIds,
                plan.DeleteLlmCache),
            cancellationToken);

        if (result.Succeeded && plan.Found)
        {
            await llmCacheService.BumpWorkspaceQueryRevisionAsync(
                plan.Workspace,
                CancellationToken.None);
        }

        return result;
    }

    private static List<string> ReadStringList(Dictionary<string, object> data, string key)
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
                .ToList(),
            IEnumerable<object> objects => objects
                .Select(item => item?.ToString()?.Trim() ?? string.Empty)
                .Where(item => item.Length > 0)
                .ToList(),
            string text when !string.IsNullOrWhiteSpace(text) => [text.Trim()],
            _ => []
        };
    }

    private static List<string> ReadJsonStringList(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray()
                .Select(ReadJsonScalar)
                .Where(item => item.Length > 0)
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

    /// <summary>
    /// Query
    /// Reference: Python version kg_query function
    /// </summary>
    public async Task<QueryResult> QueryAsync(
        string query,
        QueryParam? queryParam = null,
        CancellationToken cancellationToken = default)
    {
        queryParam ??= new QueryParam();

        if (string.IsNullOrWhiteSpace(query))
        {
            return new QueryResult
            {
                Content = "Sorry, I'm not able to provide an answer to that question.[no-context]"
            };
        }

        if (queryParam.Mode == QueryMode.Bypass)
        {
            return await QueryBypassAsync(query, queryParam, cancellationToken);
        }

        if (queryParam.Mode == QueryMode.Naive)
        {
            return await QueryNaiveAsync(query, queryParam, cancellationToken);
        }

        // 1. Extract keywords
        var keywordResult = await GetQueryKeywordsAsync(query, queryParam, cancellationToken);
        var keywords = keywordResult.Keywords;

        if (IsKnowledgeGraphQueryMode(queryParam.Mode))
        {
            var keywordDecision = QueryKeywordPolicy.NormalizeForKg(query, keywords, queryParam.Mode);

            if (keywordDecision.ShouldFail)
            {
                return new QueryResult
                {
                    Content = "Sorry, I'm not able to provide an answer to that question.[no-context]"
                };
            }

            keywords = keywordDecision.Keywords;
        }

        if (keywordResult.ShouldSaveKeywordCache)
        {
            await llmCacheService.SaveKeywordsAsync(
                keywordResult.Workspace!,
                queryParam.Mode,
                query,
                keywords,
                cancellationToken);
        }

        logger.LogDebug(
            "High-level keywords: {HLKeywords}, Low-level keywords: {LLKeywords}",
            string.Join(", ", keywords.HighLevelKeywords),
            string.Join(", ", keywords.LowLevelKeywords));

        // 2. Build query context
        var contextResult = await retrievalContextService.BuildQueryContextAsync(
            query,
            keywords,
            queryParam,
            cancellationToken);

        if (contextResult == null)
        {
            logger.LogInformation("No query context could be built");
            return new QueryResult
            {
                Content = "Sorry, I'm not able to provide an answer to that question.[no-context]"
            };
        }

        // 3. If only context is needed, return directly
        if (queryParam is { OnlyNeedContext: true, OnlyNeedPrompt: false })
        {
            return new QueryResult
            {
                Content = contextResult.Context,
                RawData = contextResult.RawData
            };
        }

        // 4. Build system prompt
        var systemPrompt = BuildRAGResponsePrompt(contextResult, queryParam);
        
        // Calculate and output token count log (reference Python version kg_query function)
        var systemPromptTokens = tokenizer.CountTokens(systemPrompt);
        var contextTokens = tokenizer.CountTokens(contextResult.Context);
        var queryTokens = tokenizer.CountTokens(query);
        var totalPromptTokens = tokenizer.CountTokens(query + systemPrompt);
        
        // Output token count log
        logger.LogInformation(
            "[QueryAsync] Token statistics - System prompt: {SystemPromptTokens:N0} tokens (includes context: {ContextTokens:N0} tokens), Query: {QueryTokens:N0} tokens, Total: {TotalPromptTokens:N0} tokens",
            systemPromptTokens,
            contextTokens,
            queryTokens,
            totalPromptTokens);

        // 5. If only prompt is needed, return directly
        if (queryParam.OnlyNeedPrompt)
        {
            var promptContent = $"{systemPrompt}\n\n---User Query---\n{query}";
            return new QueryResult
            {
                Content = promptContent,
                RawData = contextResult.RawData
            };
        }

        // 6. Call LLM to generate answer
        if (queryParam.Stream)
        {
            var responseIterator = llmService.GenerateStreamAsync(
                query,
                systemPrompt,
                queryParam.ConversationHistory,
                temperature: 0.3f,
                cancellationToken: cancellationToken);

            return new QueryResult
            {
                ResponseIterator = responseIterator,
                RawData = contextResult.RawData,
                IsStreaming = true
            };
        }

        var cacheContext = await CreateQueryAnswerCacheContextAsync(
            queryParam,
            useWorkspaceRevision: true,
            cancellationToken);
        var response = await GenerateQueryAnswerAsync(
            query,
            queryParam,
            keywords,
            systemPrompt,
            cacheContext,
            cancellationToken);

        return new QueryResult
        {
            Content = response,
            RawData = contextResult.RawData
        };
    }

    private async Task<QueryResult> QueryBypassAsync(
        string query,
        QueryParam queryParam,
        CancellationToken cancellationToken)
    {
        var rawData = BuildBypassRawData();

        if (queryParam is { OnlyNeedContext: true, OnlyNeedPrompt: false })
        {
            return new QueryResult
            {
                Content = string.Empty,
                RawData = rawData
            };
        }

        if (queryParam.OnlyNeedPrompt)
        {
            return new QueryResult
            {
                Content = query,
                RawData = rawData
            };
        }

        if (queryParam.Stream)
        {
            var responseIterator = llmService.GenerateStreamAsync(
                query,
                systemPrompt: null,
                historyMessages: queryParam.ConversationHistory,
                temperature: 0.3f,
                cancellationToken: cancellationToken);

            return new QueryResult
            {
                ResponseIterator = responseIterator,
                RawData = rawData,
                IsStreaming = true
            };
        }

        var cacheContext = await CreateQueryAnswerCacheContextAsync(
            queryParam,
            useWorkspaceRevision: false,
            cancellationToken);
        var response = await GenerateQueryAnswerAsync(
            query,
            queryParam,
            new KeywordsResult(),
            systemPrompt: null,
            cacheContext,
            cancellationToken);

        return new QueryResult
        {
            Content = response,
            RawData = rawData
        };
    }

    private async Task<QueryResult> QueryNaiveAsync(
        string query,
        QueryParam queryParam,
        CancellationToken cancellationToken)
    {
        var contextResult = await naiveQueryService.BuildContextAsync(query, queryParam, cancellationToken);
        if (contextResult == null)
        {
            logger.LogInformation("No naive query context could be built");
            return new QueryResult
            {
                Content = "Sorry, I'm not able to provide an answer to that question.[no-context]"
            };
        }

        if (queryParam is { OnlyNeedContext: true, OnlyNeedPrompt: false })
        {
            return new QueryResult
            {
                Content = contextResult.Context,
                RawData = contextResult.RawData
            };
        }

        var systemPrompt = NaiveQueryPromptBuilder.BuildResponsePrompt(contextResult, queryParam);

        var systemPromptTokens = tokenizer.CountTokens(systemPrompt);
        var contextTokens = tokenizer.CountTokens(contextResult.Context);
        var queryTokens = tokenizer.CountTokens(query);
        var totalPromptTokens = tokenizer.CountTokens(query + systemPrompt);

        logger.LogInformation(
            "[QueryAsync] Naive token statistics - System prompt: {SystemPromptTokens:N0} tokens (includes context: {ContextTokens:N0} tokens), Query: {QueryTokens:N0} tokens, Total: {TotalPromptTokens:N0} tokens",
            systemPromptTokens,
            contextTokens,
            queryTokens,
            totalPromptTokens);

        if (queryParam.OnlyNeedPrompt)
        {
            var promptContent = $"{systemPrompt}\n\n---User Query---\n{query}";
            return new QueryResult
            {
                Content = promptContent,
                RawData = contextResult.RawData
            };
        }

        if (queryParam.Stream)
        {
            var responseIterator = llmService.GenerateStreamAsync(
                query,
                systemPrompt,
                queryParam.ConversationHistory,
                temperature: 0.3f,
                cancellationToken: cancellationToken);

            return new QueryResult
            {
                ResponseIterator = responseIterator,
                RawData = contextResult.RawData,
                IsStreaming = true
            };
        }

        var cacheContext = await CreateQueryAnswerCacheContextAsync(
            queryParam,
            useWorkspaceRevision: true,
            cancellationToken);
        var response = await GenerateQueryAnswerAsync(
            query,
            queryParam,
            new KeywordsResult(),
            systemPrompt,
            cacheContext,
            cancellationToken);

        return new QueryResult
        {
            Content = response,
            RawData = contextResult.RawData
        };
    }

    private static Dictionary<string, object> BuildBypassRawData()
    {
        return new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>(),
            ["metadata"] = new Dictionary<string, object>
            {
                ["query_mode"] = QueryMode.Bypass.ToString()
            }
        };
    }

    private static bool IsKnowledgeGraphQueryMode(QueryMode mode)
    {
        return mode is QueryMode.Local or QueryMode.Global or QueryMode.Hybrid or QueryMode.Mix;
    }

    private static bool IsQueryAnswerCacheEligible(QueryParam queryParam)
    {
        return !queryParam.Stream
            && !queryParam.OnlyNeedContext
            && !queryParam.OnlyNeedPrompt
            && queryParam.ConversationHistory.Count == 0;
    }

    private async Task<QueryAnswerCacheContext?> CreateQueryAnswerCacheContextAsync(
        QueryParam queryParam,
        bool useWorkspaceRevision,
        CancellationToken cancellationToken)
    {
        if (!IsQueryAnswerCacheEligible(queryParam))
        {
            return null;
        }

        var workspace = documentLifecycleService.GetDefaultWorkspace();
        var revision = 0L;
        if (useWorkspaceRevision)
        {
            var result = await llmCacheService.TryGetWorkspaceQueryRevisionAsync(workspace, cancellationToken);
            if (!result.Succeeded)
            {
                return null;
            }

            revision = result.Revision;
        }

        return new QueryAnswerCacheContext(workspace, revision);
    }

    private async Task<string> GenerateQueryAnswerAsync(
        string query,
        QueryParam queryParam,
        KeywordsResult keywords,
        string? systemPrompt,
        QueryAnswerCacheContext? cacheContext,
        CancellationToken cancellationToken)
    {
        if (cacheContext is not null)
        {
            var cachedResponse = await llmCacheService.TryGetQueryResponseAsync(
                cacheContext.Workspace,
                cacheContext.Revision,
                query,
                queryParam,
                keywords,
                cancellationToken);

            if (cachedResponse is not null)
            {
                return cachedResponse;
            }
        }

        var response = await llmService.GenerateAsync(
            query,
            systemPrompt,
            queryParam.ConversationHistory,
            temperature: 0.3f,
            cancellationToken: cancellationToken);

        if (cacheContext is not null)
        {
            await llmCacheService.SaveQueryResponseAsync(
                cacheContext.Workspace,
                cacheContext.Revision,
                query,
                queryParam,
                keywords,
                response,
                cancellationToken);
        }

        return response;
    }

    private async Task<QueryKeywordResult> GetQueryKeywordsAsync(
        string query,
        QueryParam queryParam,
        CancellationToken cancellationToken)
    {
        if (queryParam.HighLevelKeywords.Count > 0 || queryParam.LowLevelKeywords.Count > 0)
        {
            return new QueryKeywordResult(
                new KeywordsResult
                {
                    HighLevelKeywords = queryParam.HighLevelKeywords,
                    LowLevelKeywords = queryParam.LowLevelKeywords
                },
                Workspace: null,
                ShouldSaveKeywordCache: false);
        }

        if (!IsKnowledgeGraphQueryMode(queryParam.Mode))
        {
            return new QueryKeywordResult(
                await llmService.ExtractKeywordsAsync(query, cancellationToken: cancellationToken),
                Workspace: null,
                ShouldSaveKeywordCache: false);
        }

        var workspace = documentLifecycleService.GetDefaultWorkspace();
        var cachedKeywords = await llmCacheService.TryGetKeywordsAsync(
            workspace,
            queryParam.Mode,
            query,
            cancellationToken);

        if (cachedKeywords is not null)
        {
            return new QueryKeywordResult(
                cachedKeywords,
                workspace,
                ShouldSaveKeywordCache: false);
        }

        var keywords = await llmService.ExtractKeywordsAsync(query, cancellationToken: cancellationToken);
        return new QueryKeywordResult(
            keywords,
            workspace,
            ShouldSaveKeywordCache: true);
    }

    private sealed record QueryKeywordResult(
        KeywordsResult Keywords,
        string? Workspace,
        bool ShouldSaveKeywordCache);

    private sealed record QueryAnswerCacheContext(
        string Workspace,
        long Revision);

    private static string BuildRAGResponsePrompt(QueryContextResult contextResult, QueryParam queryParam)
    {
        var responseType = string.IsNullOrEmpty(queryParam.ResponseType)
            ? "Multiple Paragraphs"
            : queryParam.ResponseType;
        var userPrompt = queryParam.UserPrompt ?? "";

        return $"""
                ---Role---

                You are an expert AI assistant specializing in synthesizing information from a provided knowledge base. Your primary function is to answer user queries accurately by ONLY using the information within the provided **Context**.

                ---Goal---

                Generate a comprehensive, well-structured answer to the user query.
                The answer must integrate relevant facts from the Knowledge Graph and Document Chunks found in the **Context**.
                Consider the conversation history if provided to maintain conversational flow and avoid repeating information.

                ---Instructions---

                1. Step-by-Step Instruction:
                  - Carefully determine the user's query intent in the context of the conversation history to fully understand the user's information need.
                  - Scrutinize both `Knowledge Graph Data` (Entity and Relationship) and `Document Chunks` in the **Context**. The Knowledge Graph Data uses concise text format: entities as "Name (Type): Description" and relationships as "Source -> Target: Keywords - Description". Document Chunks show file names in brackets [FileName] followed by content.
                  - Identify and extract all pieces of information that are directly relevant to answering the user query.
                  - Weave the extracted facts into a coherent and logical response. Your own knowledge must ONLY be used to formulate fluent sentences and connect ideas, NOT to introduce any external information.
                  - CRITICAL: DO NOT include any citation markers, reference numbers, or links like [1], [2], [filename](url), or any other citation format anywhere in the main body of your response. Write the response naturally without any citation markers in the text.
                  - CRITICAL: References can ONLY be placed at the very end of your response under the `### References` heading. Do NOT add references, citations, or links anywhere else in the response.
                  - Track which document chunks (identified by file names in brackets) directly support the facts presented in the response. Generate a references section ONLY at the end of the response under `### References` heading, listing only the file names of documents that directly support the facts. Match the file names from Document Chunks with those in the Reference Document List.
                  - Do not generate anything after the reference section.

                2. Content & Grounding:
                  - Strictly adhere to the provided context from the **Context**; DO NOT invent, assume, or infer any information not explicitly stated.
                  - If the answer cannot be found in the **Context**, state that you do not have enough information to answer. Do not attempt to guess.

                3. Formatting & Language:
                  - The response MUST be in the same language as the user query.
                  - CRITICAL: ALL parts of the response, including section headings, MUST be in the same language as the user query. The References section heading MUST use the most accurate translation of "References" in the user's query language, NOT the English word "References" unless the query is in English.
                  - The response MUST utilize Markdown formatting for enhanced clarity and structure (e.g., headings, bold text, bullet points).
                  - The response should be presented in {responseType}.

                4. References Section Format (STRICTLY ENFORCED):
                  - CRITICAL: References can ONLY appear at the very end of your response under the `### References` heading. Do NOT add any references, citations, or links anywhere else in the response body.
                  - The References section MUST be under heading: `### References` and MUST be the last section of your response.
                  - Reference list entries should adhere to the format: `* [FileName](URL)` where FileName is the file name shown in Document Chunks (in brackets) and URL is the file path from the Reference Document List. This format creates clickable Markdown links.
                  - Match the file names from Document Chunks (shown as [FileName] in the chunks) with the corresponding file paths in the Reference Document List.
                  - The file name in the citation must match exactly the file name shown in Document Chunks and must retain its original language.
                  - Output each citation on an individual line
                  - Provide maximum of 5 most relevant citations.
                  - Do not generate footnotes section or any comment, summary, or explanation after the references.
                  - WARNING: If you add any citations, references, or links anywhere other than under the `### References` heading at the end, your response will be considered incorrect.

                5. Additional Instructions: {userPrompt}

                ---Context---

                {contextResult.Context}
                """;
    }
}

public class QueryContextResult
{
    public string Context { get; set; } = string.Empty;
    public Dictionary<string, object> RawData { get; set; } = new();
}
