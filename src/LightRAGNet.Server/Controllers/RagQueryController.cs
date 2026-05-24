using System.Buffers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Server.Services;
using LightRAGNet.Server.Services.DocumentPreview;
using LightRAGNet.Share.Models;
using Microsoft.AspNetCore.Mvc;

namespace LightRAGNet.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RagQueryController(
    LightRAG lightRAG,
    DocumentReferencePreviewResolver referencePreviewResolver,
    ILogger<RagQueryController> logger) : ControllerBase
{
    /// <summary>
    /// Query RAG system with streaming response
    /// </summary>
    /// <param name="request">Query request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Streaming response</returns>
    [HttpPost("query")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> QueryAsync(
        [FromBody] RagQueryRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.BadRequest(new { error = "Query cannot be empty" });
        }

        try
        {
            var queryParam = RagQueryRequestMapper.ToQueryParam(request);

            var queryResult = await lightRAG.QueryAsync(
                request.Query,
                queryParam,
                cancellationToken);

            var events = WrapQueryResultAsEventsAsync(request, queryResult, HttpContext.Request, cancellationToken);
            return new RagQuerySseResult(events, logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing query: {Query}", request.Query);
            var errorEvent = new ErrorEvent { Error = "Error processing query", Message = ex.Message };
            var events = new[] { errorEvent }.ToAsyncEnumerable();
            return new RagQuerySseResult(events, logger);
        }
    }

    [HttpPost("data")]
    [ProducesResponseType(typeof(RagQueryDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RagQueryDataResponse>> QueryDataAsync(
        [FromBody] RagQueryRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "Query cannot be empty" });
        }

        try
        {
            var dataRequest = RagQueryRequestMapper.ForceRetrievalDataRequest(request);
            dataRequest.Stream = false;
            dataRequest.IncludeReferences = true;
            dataRequest.OnlyNeedContext = true;
            dataRequest.OnlyNeedPrompt = false;
            var queryParam = RagQueryRequestMapper.ToQueryParam(dataRequest);
            var queryResult = await lightRAG.QueryAsync(
                dataRequest.Query,
                queryParam,
                cancellationToken);

            var (data, metadata) = SplitRawData(queryResult.RawData);
            var message = data.Count == 0 && metadata.Count == 0
                ? "No retrieval data was returned."
                : "Retrieval data returned.";

            return Ok(new RagQueryDataResponse
            {
                Status = "success",
                Message = message,
                Data = data,
                Metadata = metadata
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving query data: {Query}", request.Query);
            return StatusCode(StatusCodes.Status500InternalServerError, new RagQueryDataResponse
            {
                Status = "failure",
                Message = "Error retrieving query data."
            });
        }
    }

    internal static (Dictionary<string, object> Data, Dictionary<string, object> Metadata) SplitRawData(
        Dictionary<string, object>? rawData)
    {
        if (rawData is null)
        {
            return ([], []);
        }

        var data = rawData.TryGetValue("data", out var dataValue) &&
            dataValue is Dictionary<string, object> dataDictionary
                ? dataDictionary
                : [];

        var metadata = rawData.TryGetValue("metadata", out var metadataValue) &&
            metadataValue is Dictionary<string, object> metadataDictionary
                ? metadataDictionary
                : [];

        return (data, metadata);
    }

    private async IAsyncEnumerable<RagQueryEvent> WrapQueryResultAsEventsAsync(
        RagQueryRequest request,
        QueryResult queryResult,
        HttpRequest httpRequest,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (queryResult is { IsStreaming: true, ResponseIterator: not null })
        {
            await foreach (var chunk in queryResult.ResponseIterator.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk))
                {
                    yield return new TextChunkEvent { Chunk = chunk };
                }
            }
        }
        else if (!string.IsNullOrEmpty(queryResult.Content))
        {
            yield return new TextChunkEvent { Chunk = queryResult.Content };
        }

        IReadOnlyList<RagQueryReferenceDto> references = request.IncludeReferences
            ? await referencePreviewResolver.ResolveAsync(queryResult.ReferenceList, httpRequest, cancellationToken).ConfigureAwait(false)
            : [];

        yield return RagQueryRequestMapper.ToMetadataEvent(request, queryResult, references);
        yield return new DoneEvent();
    }
}

/// <summary>
/// Server-Sent Events result for RAG query
/// </summary>
public sealed class RagQuerySseResult : IResult, IDisposable
{
    private readonly IAsyncEnumerable<RagQueryEvent> _events;
    private readonly ILogger<RagQueryController> _logger;
    private Utf8JsonWriter? _jsonWriter;

    internal RagQuerySseResult(IAsyncEnumerable<RagQueryEvent> events, ILogger<RagQueryController> logger)
    {
        _events = events;
        _logger = logger;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache,no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        var body = httpContext.Response.Body;
        var cancellationToken = httpContext.RequestAborted;

        var requestPath = httpContext.Request.Path.Value ?? "unknown";
        _logger.LogDebug("RAG query streaming started: {Path}", requestPath);

        try
        {
            await SseFormatter.WriteAsync(
                WrapEventsAsSseItemsAsync(_events, cancellationToken),
                body,
                SerializeEvent,
                cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("RAG query streaming completed: {Path}", requestPath);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("RAG query streaming cancelled: {Path}", requestPath);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RAG query streaming error: {Path}", requestPath);
            
            try
            {
                var errorEvent = new ErrorEvent
                {
                    Error = "StreamingError",
                    Message = ex.Message
                };
                await SseFormatter.WriteAsync(
                    WrapEventsAsSseItemsAsync([errorEvent]),
                    body,
                    SerializeEvent,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception sendErrorEx)
            {
                _logger.LogError(sendErrorEx, "Failed to send error event: {Path}", requestPath);
            }
        }

        await body.FlushAsync(httpContext.RequestAborted).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<SseItem<RagQueryEvent>> WrapEventsAsSseItemsAsync(
        IAsyncEnumerable<RagQueryEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return new SseItem<RagQueryEvent>(evt);
        }
    }

    private static async IAsyncEnumerable<SseItem<RagQueryEvent>> WrapEventsAsSseItemsAsync(
        IEnumerable<RagQueryEvent> events)
    {
        foreach (var evt in events)
        {
            yield return new SseItem<RagQueryEvent>(evt);
        }
        
        await Task.CompletedTask;
    }

    private void SerializeEvent(SseItem<RagQueryEvent> item, IBufferWriter<byte> writer)
    {
        if (_jsonWriter == null)
        {
            _jsonWriter = new Utf8JsonWriter(writer);
        }
        else
        {
            _jsonWriter.Reset(writer);
        }
        JsonSerializer.Serialize(_jsonWriter, item.Data, LightRAGJsonOptions.HumanReadable);
    }

    public void Dispose()
    {
        _jsonWriter?.Dispose();
    }
}
