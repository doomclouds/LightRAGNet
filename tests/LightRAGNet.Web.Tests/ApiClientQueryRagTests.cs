using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;
using LightRAGNet.Web;
using LightRAGNet.Web.Models;

namespace LightRAGNet.Web.Tests;

public sealed class ApiClientQueryRagTests
{
    [Fact]
    public async Task QueryRagAsync_SendsFullRagQueryRequestBody()
    {
        var handler = new CapturingHandler(_ => SseResponse(new DoneEvent()));
        var client = CreateClient(handler);
        var request = new RagQueryRequest
        {
            Query = "explain retrieval",
            Mode = QueryMode.Global,
            Stream = false,
            IncludeReferences = false,
            ResponseType = "Single Paragraph",
            TopK = 7,
            ChunkTopK = 3,
            EnableRerank = false,
            HighLevelKeywords = ["graph"],
            LowLevelKeywords = ["edge"],
            OnlyNeedContext = true,
            OnlyNeedPrompt = true
        };

        await client.QueryRagAsync(request, new RagQueryStreamHandlers());

        handler.RequestUri.Should().Be("http://localhost/api/RagQuery/query");
        handler.RequestMethod.Should().Be(HttpMethod.Post);
        handler.RequestBody.Should().NotBeNullOrWhiteSpace();

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;

        root.GetProperty("query").GetString().Should().Be("explain retrieval");
        root.GetProperty("mode").GetInt32().Should().Be((int)QueryMode.Global);
        root.GetProperty("stream").GetBoolean().Should().BeFalse();
        root.GetProperty("includeReferences").GetBoolean().Should().BeFalse();
        root.GetProperty("responseType").GetString().Should().Be("Single Paragraph");
        root.GetProperty("topK").GetInt32().Should().Be(7);
        root.GetProperty("chunkTopK").GetInt32().Should().Be(3);
        root.GetProperty("enableRerank").GetBoolean().Should().BeFalse();
        root.GetProperty("highLevelKeywords").EnumerateArray().Select(item => item.GetString()).Should().ContainSingle("graph");
        root.GetProperty("lowLevelKeywords").EnumerateArray().Select(item => item.GetString()).Should().ContainSingle("edge");
        root.GetProperty("onlyNeedContext").GetBoolean().Should().BeTrue();
        root.GetProperty("onlyNeedPrompt").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task QueryRagAsync_InvokesMetadataHandler_WhenMetadataEventArrives()
    {
        var metadata = new QueryMetadataEvent
        {
            Mode = QueryMode.Hybrid,
            Stream = false,
            IncludeReferences = false,
            ResponseType = "Bullets",
            CachePolicy = "Bypass",
            HighLevelKeywords = ["architecture"],
            LowLevelKeywords = ["service"],
            Diagnostics = new Dictionary<string, string> { ["source"] = "test" }
        };
        var client = CreateClient(new CapturingHandler(_ => SseResponse(metadata, new DoneEvent())));
        QueryMetadataEvent? received = null;

        await client.QueryRagAsync(
            new RagQueryRequest { Query = "metadata" },
            new RagQueryStreamHandlers { OnMetadataReceived = evt => { received = evt; return Task.CompletedTask; } });

        received.Should().NotBeNull();
        received!.Mode.Should().Be(QueryMode.Hybrid);
        received.Stream.Should().BeFalse();
        received.IncludeReferences.Should().BeFalse();
        received.ResponseType.Should().Be("Bullets");
        received.CachePolicy.Should().Be("Bypass");
        received.HighLevelKeywords.Should().ContainSingle("architecture");
        received.LowLevelKeywords.Should().ContainSingle("service");
        received.Diagnostics.Should().ContainKey("source").WhoseValue.Should().Be("test");
    }

    [Fact]
    public async Task QueryRagAsync_ThrowsRagQueryException_WhenErrorEventArrives()
    {
        var client = CreateClient(new CapturingHandler(_ => SseResponse(
            new ErrorEvent { Error = "query_failed", Message = "Backend failed" })));

        var act = () => client.QueryRagAsync(
            new RagQueryRequest { Query = "error" },
            new RagQueryStreamHandlers());

        var exception = await act.Should().ThrowAsync<RagQueryException>();
        exception.Which.Error.Should().Be("query_failed");
        exception.Which.Message.Should().Be("Backend failed");
    }

    [Fact]
    public async Task QueryRagAsync_DoesNotSwallowChunkHandlerExceptions()
    {
        var client = CreateClient(new CapturingHandler(_ => SseResponse(
            new TextChunkEvent { Chunk = "hello" },
            new DoneEvent())));
        var expected = new InvalidOperationException("chunk handler failed");

        var act = () => client.QueryRagAsync(
            new RagQueryRequest { Query = "chunk" },
            new RagQueryStreamHandlers { OnChunkReceived = _ => throw expected });

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task QueryRagAsync_DoesNotSwallowMetadataHandlerExceptions()
    {
        var client = CreateClient(new CapturingHandler(_ => SseResponse(
            new QueryMetadataEvent { Mode = QueryMode.Local },
            new DoneEvent())));
        var expected = new InvalidOperationException("metadata handler failed");

        var act = () => client.QueryRagAsync(
            new RagQueryRequest { Query = "metadata" },
            new RagQueryStreamHandlers { OnMetadataReceived = _ => throw expected });

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task QueryRagAsync_ThrowsOperationCanceled_WhenTokenCancelledDuringStream()
    {
        using var cts = new CancellationTokenSource();
        var received = new List<string>();
        var client = CreateClient(new CapturingHandler(_ => SseResponse(
            new TextChunkEvent { Chunk = "first" },
            new TextChunkEvent { Chunk = "second" },
            new DoneEvent())));

        var act = () => client.QueryRagAsync(
            new RagQueryRequest { Query = "cancel" },
            new RagQueryStreamHandlers
            {
                OnChunkReceived = chunk =>
                {
                    received.Add(chunk);
                    cts.Cancel();
                    return Task.CompletedTask;
                }
            },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        received.Should().Equal("first");
    }

    private static ApiClient CreateClient(HttpMessageHandler handler)
    {
        return new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
    }

    private static HttpResponseMessage SseResponse(params RagQueryEvent[] events)
    {
        var builder = new StringBuilder();
        foreach (var evt in events)
        {
            builder.Append("data: ");
            builder.Append(JsonSerializer.Serialize<RagQueryEvent>(evt));
            builder.Append("\n\n");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(builder.ToString(), Encoding.UTF8, "text/event-stream")
        };
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public HttpMethod? RequestMethod { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            RequestMethod = request.Method;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return responseFactory(request);
        }
    }
}
