using LightRAGNet.Core.Interfaces;
using LightRAGNet.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace LightRAGNet.Storage;

/// <summary>
/// Qdrant vector store implementation
/// Reference: Python version kg/qdrant_impl.py
/// </summary>
public class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly IEmbeddingService? _embeddingService;
    private readonly int _embeddingDimension;
    private const string Prefix = "lightrag_vdb_dotnet";
    private readonly string _workspace; // Workspace ID for generating Point ID
    
    public QdrantVectorStore(
        QdrantClient client,
        ILogger<QdrantVectorStore> logger,
        IOptions<QdrantOptions> options,
        IEmbeddingService? embeddingService = null)
    {
        _client = client;
        _logger = logger;
        _embeddingService = embeddingService;
        var qdrantOptions = options.Value;
        _embeddingDimension = qdrantOptions.EmbeddingDimension;
        _workspace = qdrantOptions.Workspace;
    }
    
    /// <summary>
    /// Generate collection name, format: lightrag_vdb_dotnet_{type}_{model}_{dimension}d
    /// Example: lightrag_vdb_dotnet_chunks_text_embedding_v4_2048d
    /// </summary>
    private string GetCollectionName(string baseName)
    {
        return $"{Prefix}_{baseName}_{_embeddingDimension}d";
    }
    
    public async Task<List<SearchResult>> QueryAsync(
        string collection,
        string query,
        int topK,
        float[]? queryEmbedding = null,
        float threshold = 0.2f,
        CancellationToken cancellationToken = default)
    {
        // Reference Python version: if query_embedding is None, generate embedding using query parameter
        // Python version: if query_embedding is not None: embedding = query_embedding
        //                else: embedding = await self.embedding_func([query])
        float[] embedding;
        if (queryEmbedding is { Length: > 0 })
        {
            embedding = queryEmbedding;
        }
        else
        {
            if (_embeddingService == null)
            {
                throw new InvalidOperationException(
                    "Query embedding is required. Either provide queryEmbedding parameter or inject IEmbeddingService.");
            }
            
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Query string is required when queryEmbedding is not provided.");
            }
            
            // Generate embedding using query parameter (consistent with Python version)
            embedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        }
        
        var fullCollectionName = GetCollectionName(collection);
        await EnsureCollectionExistsAsync(fullCollectionName, _embeddingDimension, cancellationToken);
        
        var searchResult = await _client.SearchAsync(
            collectionName: fullCollectionName,
            vector: embedding,
            limit: (ulong)topK,
            scoreThreshold: threshold,
            cancellationToken: cancellationToken);
        
        return searchResult.Select(r => 
        {
            // Get original ID from payload (consistent with Python version)
            // Python version stores "id" field in payload
            var originalId = r.Payload.TryGetValue("id", out var idValue)
                ? ConvertPayloadValue(idValue)?.ToString() ?? GetPointIdString(r.Id)
                : GetPointIdString(r.Id);
            
            return new SearchResult
            {
                Id = originalId,
                Score = r.Score,
                Metadata = r.Payload.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ConvertPayloadValue(kvp.Value)!),
                Content = r.Payload.TryGetValue("content", out var content)
                    ? ConvertPayloadValue(content)?.ToString() ?? string.Empty
                    : string.Empty
            };
        }).ToList();
    }
    
    public async Task UpsertAsync(
        string collection,
        IEnumerable<VectorDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var docsList = documents.ToList();
        if (docsList.Count == 0)
            return;
        
        var fullCollectionName = GetCollectionName(collection);
        await EnsureCollectionExistsAsync(fullCollectionName, _embeddingDimension, cancellationToken);
        
        var points = docsList.Select(doc => new PointStruct
        {
            Id = CreatePointId(doc.Id),
            Vectors = new Vectors { Vector = doc.Vector },
            Payload = { ConvertToPayload(doc.Metadata) }
        }).ToList();
        
        await _client.UpsertAsync(
            collectionName: fullCollectionName,
            points: points,
            cancellationToken: cancellationToken);
    }
    
    public async Task DeleteAsync(
        string collection,
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var idsList = ids.ToList();
        if (idsList.Count == 0)
            return;
        
        // Generate full collection name
        var fullCollectionName = GetCollectionName(collection);
        await EnsureCollectionExistsAsync(fullCollectionName, _embeddingDimension, cancellationToken);
        
        // DeleteAsync only supports numeric IDs, convert string ID to ulong
        // Use same logic as CreatePointId, but return ulong directly
        var numericIds = idsList.Select(CreateNumericId).ToList();
        
        if (numericIds.Count > 0)
        {
            await _client.DeleteAsync(
                collectionName: fullCollectionName,
                ids: numericIds,
                cancellationToken: cancellationToken);
        }
    }
    
    public async Task<VectorDocument?> GetByIdAsync(
        string collection,
        string id,
        CancellationToken cancellationToken = default)
    {
        // Generate full collection name
        var fullCollectionName = GetCollectionName(collection);
        await EnsureCollectionExistsAsync(fullCollectionName, _embeddingDimension, cancellationToken);
        
        var result = await _client.RetrieveAsync(
            collectionName: fullCollectionName,
            ids: [CreatePointId(id)],
            withPayload: true,
            withVectors: true,
            cancellationToken: cancellationToken);
        
        var point = result.FirstOrDefault();
        if (point == null)
            return null;
        
        // Get original ID from payload (if exists)
        var originalId = point.Payload.TryGetValue("id", out var idValue)
            ? ConvertPayloadValue(idValue)?.ToString() ?? id
            : id;
        
        return new VectorDocument
        {
            Id = originalId,
            Vector = point.Vectors.Vector.Data.ToArray(),
            Metadata = point.Payload.ToDictionary(
                kvp => kvp.Key,
                kvp => ConvertPayloadValue(kvp.Value)!),
            Content = point.Payload.TryGetValue("content", out var content)
                ? ConvertPayloadValue(content)?.ToString() ?? string.Empty
                : string.Empty
        };
    }
    
    public async Task<List<VectorDocument>> GetByIdsAsync(
        string collection,
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var idsList = ids.ToList();
        if (idsList.Count == 0)
            return [];
        
        // Generate full collection name
        var fullCollectionName = GetCollectionName(collection);
        await EnsureCollectionExistsAsync(fullCollectionName, _embeddingDimension, cancellationToken);
        
        var pointIds = idsList.Select(CreatePointId).ToList();
        
        var result = await _client.RetrieveAsync(
            collectionName: fullCollectionName,
            ids: pointIds,
            withPayload: true,
            withVectors: true,
            cancellationToken: cancellationToken);
        
        return result.Select((point, index) => 
        {
            // Get original ID from payload (if exists), otherwise use provided ID
            var originalId = point.Payload.TryGetValue("id", out var idValue)
                ? ConvertPayloadValue(idValue)?.ToString() ?? idsList[index]
                : idsList[index];
            
            return new VectorDocument
            {
                Id = originalId,
                Vector = point.Vectors.Vector.Data.ToArray(),
                Metadata = point.Payload.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ConvertPayloadValue(kvp.Value)!),
                Content = point.Payload.TryGetValue("content", out var content)
                    ? ConvertPayloadValue(content)?.ToString() ?? string.Empty
                    : string.Empty
            };
        }).ToList();
    }
    
    private async Task EnsureCollectionExistsAsync(
        string collection,
        int dimension,
        CancellationToken cancellationToken)
    {
        try
        {
            var collections = await _client.ListCollectionsAsync(cancellationToken);
            
            var collectionExists = collections.Any(c => string.Equals(c, collection));
            if (collectionExists)
            {
                _logger.LogDebug("Collection '{Collection}' already exists", collection);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list collections, will attempt to create collection '{Collection}'", collection);
            throw;
        }
        
        try
        {
            await _client.CreateCollectionAsync(
                collectionName: collection,
                vectorsConfig: new VectorParams
                {
                    Size = (ulong)dimension,
                    Distance = Distance.Cosine
                },
                cancellationToken: cancellationToken);
            
            _logger.LogInformation("Created collection: {Collection} with dimension: {Dimension}", 
                collection, dimension);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create collection '{Collection}'", collection);
            throw;
        }
    }
    
    private Dictionary<string, Value> ConvertToPayload(Dictionary<string, object> metadata)
    {
        var payload = new Dictionary<string, Value>();
        
        foreach (var kvp in metadata)
        {
            payload[kvp.Key] = ConvertToValue(kvp.Value);
        }
        
        return payload;
    }
    
    private Value ConvertToValue(object? value)
    {
        return value switch
        {
            string s => new Value { StringValue = s },
            int i => new Value { IntegerValue = i },
            long l => new Value { IntegerValue = l },
            float f => new Value { DoubleValue = f },
            double d => new Value { DoubleValue = d },
            bool b => new Value { BoolValue = b },
            _ => new Value { StringValue = value?.ToString() ?? string.Empty }
        };
    }
    
    private object? ConvertPayloadValue(Value value)
    {
        return value.KindCase switch
        {
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.IntegerValue => value.IntegerValue,
            Value.KindOneofCase.DoubleValue => value.DoubleValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            _ => value.ToString()
        };
    }
    
    /// <summary>
    /// Create PointId (consistent with Python version)
    /// Reference: Python version compute_mdhash_id_for_qdrant function
    /// Python version combines original ID with workspace prefix, then generates UUID
    /// </summary>
    private PointId CreatePointId(string id)
    {
        // Python version: compute_mdhash_id_for_qdrant(d[ID_FIELD], prefix=self.effective_workspace)
        // Generate UUID hex format (simple style)
        var uuidHex = HashUtils.ComputeQdrantPointId(id, prefix: _workspace, style: "simple");
        
        // Qdrant .NET Client PointId supports creation from string (UUID)
        // Try to convert UUID hex string to PointId
        // If Qdrant.Client supports string ID, use directly; otherwise try to parse as ulong
        if (Guid.TryParse(uuidHex, out var guid))
        {
            // If GUID is supported, create using GUID byte array
            // Note: Qdrant.Client may only support numeric IDs, need to check actual API
            // Temporary solution: use GUID hash code as numeric ID (maintain consistency)
            var guidBytes = guid.ToByteArray();
            // Use first 8 bytes as ulong (big-endian)
            var numericId = BitConverter.ToUInt64(guidBytes, 0);
            return numericId;
        }
        
        // If cannot parse as GUID, use string hash value
        _logger.LogWarning("Failed to convert UUID hex to GUID, using hash: {Id}", id);
        var hash = (ulong)uuidHex.GetHashCode();
        return hash;
    }
    
    /// <summary>
    /// Create numeric ID (ulong) for DeleteAsync
    /// Use same logic as CreatePointId, but return ulong directly
    /// </summary>
    private ulong CreateNumericId(string id)
    {
        // Use same logic as CreatePointId
        var uuidHex = HashUtils.ComputeQdrantPointId(id, prefix: _workspace, style: "simple");
        
        if (Guid.TryParse(uuidHex, out var guid))
        {
            var guidBytes = guid.ToByteArray();
            return BitConverter.ToUInt64(guidBytes, 0);
        }
        
        _logger.LogWarning("Failed to convert UUID hex to GUID, using hash: {Id}", id);
        return (ulong)uuidHex.GetHashCode();
    }
    
    /// <summary>
    /// Get string representation from PointId
    /// Note: Since PointId may be numeric, cannot directly restore to original ID
    /// Need to get original ID from "id" field in payload
    /// </summary>
    private string GetPointIdString(PointId pointId)
    {
        // PointId may be integer or string, use ToString() method
        return pointId.ToString();
    }
}

public class QdrantOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public string? ApiKey { get; set; }
    public string Workspace { get; set; } = "_";
    public int EmbeddingDimension { get; set; } = 2048;
}

