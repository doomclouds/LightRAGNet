using System.Buffers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;
using Microsoft.AspNetCore.Mvc;

namespace LightRAGNet.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RagQueryController(
    LightRAG lightRAG,
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
        [FromBody] QueryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.BadRequest(new { error = "Query cannot be empty" });
        }

        try
        {
            var queryParam = new QueryParam
            {
                Stream = true, // Enable streaming
                ConversationHistory = [] // No conversation history, only current query
            };

            var queryResult = await lightRAG.QueryAsync(
                request.Query,
                queryParam,
                cancellationToken);

            if (queryResult is { IsStreaming: true, ResponseIterator: not null })
            {
                var events = WrapChunksAsEventsAsync(queryResult.ResponseIterator, cancellationToken);
                return new RagQuerySseResult(events, logger);
            }
            else
            {
                // Fallback to non-streaming response
                var content = queryResult.Content ?? "";
                var events = new[] { new TextChunkEvent { Chunk = content } }.ToAsyncEnumerable();
                return new RagQuerySseResult(events, logger);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing query: {Query}", request.Query);
            var errorEvent = new ErrorEvent { Error = "Error processing query", Message = ex.Message };
            var events = new[] { errorEvent }.ToAsyncEnumerable();
            return new RagQuerySseResult(events, logger);
        }
    }

    private static async IAsyncEnumerable<RagQueryEvent> WrapChunksAsEventsAsync(
        IAsyncEnumerable<string> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(chunk))
            {
                yield return new TextChunkEvent { Chunk = chunk };
            }
        }
        
        yield return new DoneEvent();
    }
}

/// <summary>
/// Query request model
/// </summary>
public class QueryRequest
{
    /// <summary>
    /// User query text
    /// </summary>
    public string Query { get; set; } = string.Empty;
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
        JsonSerializer.Serialize(_jsonWriter, item.Data);
    }

    public void Dispose()
    {
        _jsonWriter?.Dispose();
    }
}
