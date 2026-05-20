# Chat Query UI Adaptation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the Blazor Chat page so users can choose query mode, streaming versus cacheable output, references, and can see query metadata/errors returned by the RAG API.

**Architecture:** Add shared request/SSE contract types in `LightRAGNet.Share`, map them to `QueryParam` in `LightRAGNet.Server`, then adapt `ApiClient` and `RagChat.razor` to send options and render metadata. Keep core RAG ranking, cache semantics, and storage behavior unchanged.

**Tech Stack:** .NET 10, ASP.NET Core, Blazor Server, MudBlazor, xUnit, FluentAssertions, source-level regression tests.

---

## Spec Traceability

- Query mode selector: Task 5.
- Streaming/cacheable output switch: Tasks 1, 2, 5.
- References toggle and rendering: Tasks 1, 2, 4, 5.
- Metadata event contract: Tasks 1, 2, 3.
- Visible SSE/HTTP errors: Tasks 3, 5.
- Default behavior remains `Mix + streaming`: Tasks 1, 2, 5.
- No cache-management UI or persistent chat sessions: all tasks stay inside existing chat, API, and in-memory history boundaries.

## File Map

- Create: `src/LightRAGNet.Share/Models/RagQueryRequest.cs`
  - Shared request contract used by Web and Server.
- Modify: `src/LightRAGNet.Share/Models/RagQueryEvent.cs`
  - Add metadata and reference event DTOs without changing existing event names.
- Create: `src/LightRAGNet.Server/Services/RagQueryRequestMapper.cs`
  - Isolate `RagQueryRequest` to `QueryParam` mapping and metadata event creation.
- Modify: `src/LightRAGNet.Server/Controllers/RagQueryController.cs`
  - Accept shared request, use mapper, emit metadata before `done`.
- Create: `src/LightRAGNet.Web/Models/RagQueryStreamHandlers.cs`
  - Callback container for text, metadata, and error handling.
- Create: `src/LightRAGNet.Web/Models/RagQueryException.cs`
  - Typed client exception for SSE `ErrorEvent`.
- Modify: `src/LightRAGNet.Web/ApiClient.cs`
  - Send full request, handle metadata, surface errors, keep compatibility overload.
- Modify: `src/LightRAGNet.Web/Services/ChatHistoryService.cs`
  - Preserve richer `ChatMessageModel` objects in memory.
- Modify: `src/LightRAGNet.Web/Components/Pages/RagChat.razor`
  - Add compact query controls and assistant metadata rendering.
- Create: `tests/LightRAGNet.Tests/Web/RagQueryContractSourceTests.cs`
  - Source-level guard for shared request/events and Web client error behavior.
- Create: `tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs`
  - Unit tests for server-side request mapping and metadata packaging.
- Create: `tests/LightRAGNet.Tests/Web/RagChatSourceTests.cs`
  - Source-level guard for Chat UI controls and request construction.

## Execution Notes

- Use a feature branch or worktree before implementation:

```powershell
git checkout -b feature/chat-query-ui-adaptation
```

- Keep commits small. Each task below ends with a commit.
- Do not refactor core query services unless a compiler error proves the contract cannot be wired without it.
- Use `login:false` for PowerShell commands in Codex tool calls.

---

### Task 1: Shared Query Request And SSE Metadata Contracts

**Files:**
- Create: `src/LightRAGNet.Share/Models/RagQueryRequest.cs`
- Modify: `src/LightRAGNet.Share/Models/RagQueryEvent.cs`
- Test: `tests/LightRAGNet.Tests/Web/RagQueryContractSourceTests.cs`

- [ ] **Step 1: Write failing source contract tests**

Create `tests/LightRAGNet.Tests/Web/RagQueryContractSourceTests.cs`:

```csharp
using FluentAssertions;

namespace LightRAGNet.Tests.Web;

public sealed class RagQueryContractSourceTests
{
    [Fact]
    public void RagQueryRequest_ExposesChatQueryOptions()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Share", "Models", "RagQueryRequest.cs");

        source.Should().Contain("public sealed class RagQueryRequest");
        source.Should().Contain("public string Query { get; set; } = string.Empty;");
        source.Should().Contain("public QueryMode Mode { get; set; } = QueryMode.Mix;");
        source.Should().Contain("public bool Stream { get; set; } = true;");
        source.Should().Contain("public bool IncludeReferences { get; set; } = true;");
        source.Should().Contain("public string ResponseType { get; set; } = \"Multiple Paragraphs\";");
        source.Should().Contain("public int TopK { get; set; } = 40;");
        source.Should().Contain("public int ChunkTopK { get; set; } = 20;");
        source.Should().Contain("public bool EnableRerank { get; set; } = true;");
        source.Should().Contain("public List<string> HighLevelKeywords { get; set; } = [];");
        source.Should().Contain("public List<string> LowLevelKeywords { get; set; } = [];");
        source.Should().Contain("public bool OnlyNeedContext { get; set; }");
        source.Should().Contain("public bool OnlyNeedPrompt { get; set; }");
    }

    [Fact]
    public void RagQueryEvent_ExposesMetadataEvent()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Share", "Models", "RagQueryEvent.cs");

        source.Should().Contain("[JsonDerivedType(typeof(QueryMetadataEvent), \"metadata\")]");
        source.Should().Contain("public sealed class QueryMetadataEvent : RagQueryEvent");
        source.Should().Contain("public QueryMode Mode { get; init; }");
        source.Should().Contain("public bool Stream { get; init; }");
        source.Should().Contain("public bool IncludeReferences { get; init; }");
        source.Should().Contain("public IReadOnlyList<RagQueryReferenceDto> References { get; init; }");
        source.Should().Contain("public IReadOnlyList<string> HighLevelKeywords { get; init; }");
        source.Should().Contain("public IReadOnlyList<string> LowLevelKeywords { get; init; }");
        source.Should().Contain("public IReadOnlyDictionary<string, string> Diagnostics { get; init; }");
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([repositoryRoot, .. relativeParts]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LightRAGNet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing LightRAGNet.slnx.");
    }
}
```

- [ ] **Step 2: Run the failing test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagQueryContractSourceTests
```

Expected: FAIL because `RagQueryRequest.cs`, `QueryMetadataEvent`, and `RagQueryReferenceDto` do not exist yet.

- [ ] **Step 3: Add shared request contract**

Create `src/LightRAGNet.Share/Models/RagQueryRequest.cs`:

```csharp
using LightRAGNet.Core.Models;

namespace LightRAGNet.Share.Models;

public sealed class RagQueryRequest
{
    public string Query { get; set; } = string.Empty;

    public QueryMode Mode { get; set; } = QueryMode.Mix;

    public bool Stream { get; set; } = true;

    public bool IncludeReferences { get; set; } = true;

    public string ResponseType { get; set; } = "Multiple Paragraphs";

    public int TopK { get; set; } = 40;

    public int ChunkTopK { get; set; } = 20;

    public bool EnableRerank { get; set; } = true;

    public List<string> HighLevelKeywords { get; set; } = [];

    public List<string> LowLevelKeywords { get; set; } = [];

    public bool OnlyNeedContext { get; set; }

    public bool OnlyNeedPrompt { get; set; }
}
```

- [ ] **Step 4: Extend SSE event contracts**

Modify `src/LightRAGNet.Share/Models/RagQueryEvent.cs` so it keeps the existing polymorphic discriminator model and adds the metadata derived type:

```csharp
using LightRAGNet.Core.Models;

namespace LightRAGNet.Share.Models;

/// <summary>
/// Metadata event containing query options and result diagnostics.
/// </summary>
public sealed class QueryMetadataEvent : RagQueryEvent
{
    public QueryMode Mode { get; init; } = QueryMode.Mix;

    public bool Stream { get; init; }

    public bool IncludeReferences { get; init; }

    public string ResponseType { get; init; } = "Multiple Paragraphs";

    public string CachePolicy { get; init; } = "Unknown";

    public IReadOnlyList<RagQueryReferenceDto> References { get; init; } = [];

    public IReadOnlyList<string> HighLevelKeywords { get; init; } = [];

    public IReadOnlyList<string> LowLevelKeywords { get; init; } = [];

    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } =
        new Dictionary<string, string>();
}

public sealed class RagQueryReferenceDto
{
    public string ReferenceId { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;
}
```

Add the metadata derived type to the existing attribute list:

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextChunkEvent), "text_chunk")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
[JsonDerivedType(typeof(DoneEvent), "done")]
[JsonDerivedType(typeof(QueryMetadataEvent), "metadata")]
public abstract class RagQueryEvent
{
}
```

- [ ] **Step 5: Run contract tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagQueryContractSourceTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add .\src\LightRAGNet.Share\Models\RagQueryRequest.cs .\src\LightRAGNet.Share\Models\RagQueryEvent.cs .\tests\LightRAGNet.Tests\Web\RagQueryContractSourceTests.cs
git commit -m "feat: add rag query request contract"
```

---

### Task 2: Server Request Mapping And Metadata Packaging

**Files:**
- Create: `src/LightRAGNet.Server/Services/RagQueryRequestMapper.cs`
- Modify: `src/LightRAGNet.Server/Controllers/RagQueryController.cs`
- Test: `tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs`

- [ ] **Step 1: Write failing mapper tests**

Create `tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Server.Services;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Tests;

public sealed class RagQueryRequestMapperTests
{
    [Fact]
    public void ToQueryParam_MapsRequestOptions()
    {
        var request = new RagQueryRequest
        {
            Query = "hello",
            Mode = QueryMode.Naive,
            Stream = false,
            IncludeReferences = false,
            ResponseType = "Bullet Points",
            TopK = 12,
            ChunkTopK = 6,
            EnableRerank = false,
            HighLevelKeywords = ["system"],
            LowLevelKeywords = ["queue"],
            OnlyNeedContext = true,
            OnlyNeedPrompt = true
        };

        var queryParam = RagQueryRequestMapper.ToQueryParam(request);

        queryParam.Mode.Should().Be(QueryMode.Naive);
        queryParam.Stream.Should().BeFalse();
        queryParam.IncludeReferences.Should().BeFalse();
        queryParam.ResponseType.Should().Be("Bullet Points");
        queryParam.TopK.Should().Be(12);
        queryParam.ChunkTopK.Should().Be(6);
        queryParam.EnableRerank.Should().BeFalse();
        queryParam.HighLevelKeywords.Should().Equal("system");
        queryParam.LowLevelKeywords.Should().Equal("queue");
        queryParam.OnlyNeedContext.Should().BeTrue();
        queryParam.OnlyNeedPrompt.Should().BeTrue();
        queryParam.ConversationHistory.Should().NotBeNull();
    }

    [Fact]
    public void ToMetadataEvent_UsesRequestAndQueryResult()
    {
        var request = new RagQueryRequest
        {
            Mode = QueryMode.Mix,
            Stream = false,
            IncludeReferences = true,
            ResponseType = "Multiple Paragraphs",
            HighLevelKeywords = ["architecture"],
            LowLevelKeywords = ["cache"]
        };

        var result = new QueryResult
        {
            Content = "answer",
            IsStreaming = false,
            RawData = new Dictionary<string, object>
            {
                ["data"] = new Dictionary<string, object>
                {
                    ["references"] = new List<Dictionary<string, object>>
                    {
                        new()
                        {
                            ["reference_id"] = "doc-1",
                            ["file_path"] = "guide.md"
                        }
                    }
                },
                ["metadata"] = new Dictionary<string, object>
                {
                    ["elapsed_ms"] = 42,
                    ["cache_status"] = "live"
                }
            }
        };

        var metadata = RagQueryRequestMapper.ToMetadataEvent(request, result);

        metadata.Mode.Should().Be(QueryMode.Mix);
        metadata.Stream.Should().BeFalse();
        metadata.IncludeReferences.Should().BeTrue();
        metadata.CachePolicy.Should().Be("Cacheable request");
        metadata.References.Should().ContainSingle();
        metadata.References[0].ReferenceId.Should().Be("doc-1");
        metadata.References[0].FilePath.Should().Be("guide.md");
        metadata.HighLevelKeywords.Should().Equal("architecture");
        metadata.LowLevelKeywords.Should().Equal("cache");
        metadata.Diagnostics.Should().ContainKey("elapsed_ms").WhoseValue.Should().Be("42");
        metadata.Diagnostics.Should().ContainKey("cache_status").WhoseValue.Should().Be("live");
    }
}
```

- [ ] **Step 2: Run the failing mapper tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter FullyQualifiedName~RagQueryRequestMapperTests
```

Expected: FAIL because `RagQueryRequestMapper` does not exist.

- [ ] **Step 3: Implement mapper**

Create `src/LightRAGNet.Server/Services/RagQueryRequestMapper.cs`:

```csharp
using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Services;

public static class RagQueryRequestMapper
{
    public static QueryParam ToQueryParam(RagQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new QueryParam
        {
            Mode = request.Mode,
            Stream = request.Stream,
            IncludeReferences = request.IncludeReferences,
            ResponseType = string.IsNullOrWhiteSpace(request.ResponseType)
                ? "Multiple Paragraphs"
                : request.ResponseType.Trim(),
            TopK = request.TopK,
            ChunkTopK = request.ChunkTopK,
            EnableRerank = request.EnableRerank,
            HighLevelKeywords = NormalizeKeywords(request.HighLevelKeywords),
            LowLevelKeywords = NormalizeKeywords(request.LowLevelKeywords),
            OnlyNeedContext = request.OnlyNeedContext,
            OnlyNeedPrompt = request.OnlyNeedPrompt,
            ConversationHistory = []
        };
    }

    public static QueryMetadataEvent ToMetadataEvent(RagQueryRequest request, QueryResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        return new QueryMetadataEvent
        {
            Mode = request.Mode,
            Stream = request.Stream,
            IncludeReferences = request.IncludeReferences,
            ResponseType = string.IsNullOrWhiteSpace(request.ResponseType)
                ? "Multiple Paragraphs"
                : request.ResponseType.Trim(),
            CachePolicy = request.Stream ? "Streaming request" : "Cacheable request",
            References = request.IncludeReferences
                ? result.ReferenceList.Select(ToReferenceDto).ToArray()
                : [],
            HighLevelKeywords = NormalizeKeywords(request.HighLevelKeywords),
            LowLevelKeywords = NormalizeKeywords(request.LowLevelKeywords),
            Diagnostics = ToDiagnostics(result.Metadata)
        };
    }

    private static List<string> NormalizeKeywords(IEnumerable<string>? keywords)
    {
        if (keywords is null)
        {
            return [];
        }

        return keywords
            .Select(keyword => keyword.Trim())
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RagQueryReferenceDto ToReferenceDto(ReferenceItem item)
    {
        return new RagQueryReferenceDto
        {
            ReferenceId = item.ReferenceId ?? string.Empty,
            FilePath = item.FilePath ?? string.Empty
        };
    }

    private static IReadOnlyDictionary<string, string> ToDiagnostics(
        IReadOnlyDictionary<string, object>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return metadata.ToDictionary(
            pair => pair.Key,
            pair => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture)
                ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
    }
}
```

If `QueryResult.Metadata` is typed as `Dictionary<string, object>` rather than `IReadOnlyDictionary<string, object>`, keep the method parameter as `IReadOnlyDictionary<string, object>?`; `Dictionary` satisfies it.

- [ ] **Step 4: Update controller to use shared request**

Modify `src/LightRAGNet.Server/Controllers/RagQueryController.cs`:

```csharp
using LightRAGNet.Server.Services;
using LightRAGNet.Share.Models;
```

Replace the controller-only request type and local query param creation while keeping the existing `Task<IResult>` and `RagQuerySseResult` structure:

```csharp
[HttpPost("query")]
public async Task<IResult> QueryAsync(
    [FromBody] RagQueryRequest request,
    CancellationToken cancellationToken = default)
{
    if (request is null || string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest(new
        {
            error = "Query cannot be empty"
        });
    }

    try
    {
        var queryParam = RagQueryRequestMapper.ToQueryParam(request);
        var queryResult = await lightRAG.QueryAsync(
            request.Query,
            queryParam,
            cancellationToken);

        var events = WrapQueryResultAsEventsAsync(request, queryResult, cancellationToken);
        return new RagQuerySseResult(events, logger);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing query: {Query}", request.Query);
        var errorEvent = new ErrorEvent
        {
            Error = "Error processing query",
            Message = ex.Message
        };

        var events = new[] { errorEvent }.ToAsyncEnumerable();
        return new RagQuerySseResult(events, logger);
    }
}
```

Replace `WrapChunksAsEventsAsync` with:

```csharp
private static async IAsyncEnumerable<RagQueryEvent> WrapQueryResultAsEventsAsync(
    RagQueryRequest request,
    QueryResult queryResult,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    if (queryResult.IsStreaming && queryResult.ResponseIterator is not null)
    {
        await foreach (var chunk in queryResult.ResponseIterator.WithCancellation(cancellationToken))
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

    yield return RagQueryRequestMapper.ToMetadataEvent(request, queryResult);
    yield return new DoneEvent();
}
```

Remove the private controller-only request type:

```csharp
public class QueryRequest
{
    public string Query { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Run mapper tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter FullyQualifiedName~RagQueryRequestMapperTests
```

Expected: PASS.

- [ ] **Step 6: Run server build**

Run:

```powershell
dotnet build .\src\LightRAGNet.Server\LightRAGNet.Server.csproj /p:UseAppHost=false
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```powershell
git add .\src\LightRAGNet.Server\Services\RagQueryRequestMapper.cs .\src\LightRAGNet.Server\Controllers\RagQueryController.cs .\tests\LightRAGNet.Server.Tests\RagQueryRequestMapperTests.cs
git commit -m "feat: map rag query request options"
```

---

### Task 3: Web ApiClient Streaming Pipeline

**Files:**
- Create: `src/LightRAGNet.Web/Models/RagQueryStreamHandlers.cs`
- Create: `src/LightRAGNet.Web/Models/RagQueryException.cs`
- Modify: `src/LightRAGNet.Web/ApiClient.cs`
- Test: `tests/LightRAGNet.Tests/Web/RagQueryContractSourceTests.cs`

- [ ] **Step 1: Extend source tests for ApiClient error behavior**

Append these tests to `tests/LightRAGNet.Tests/Web/RagQueryContractSourceTests.cs`:

```csharp
[Fact]
public void ApiClient_QueryRagAsync_AcceptsSharedRequestAndHandlers()
{
    var source = ReadRepositoryFile("src", "LightRAGNet.Web", "ApiClient.cs");

    source.Should().Contain("Task QueryRagAsync(RagQueryRequest request, RagQueryStreamHandlers handlers");
    source.Should().Contain("PostAsJsonAsync(\"api/RagQuery/query\", request");
    source.Should().Contain("case QueryMetadataEvent metadataEvent:");
    source.Should().Contain("await handlers.OnMetadataReceived(metadataEvent);");
}

[Fact]
public void ApiClient_QueryRagAsync_DoesNotSwallowFailures()
{
    var source = ReadRepositoryFile("src", "LightRAGNet.Web", "ApiClient.cs");

    source.Should().NotContain("catch (Exception)");
    source.Should().Contain("throw new RagQueryException(errorEvent.Error, errorEvent.Message);");
}
```

- [ ] **Step 2: Run failing source tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagQueryContractSourceTests
```

Expected: FAIL because `ApiClient` still posts `{ query }` and swallows exceptions.

- [ ] **Step 3: Add Web stream handler and exception models**

Create `src/LightRAGNet.Web/Models/RagQueryStreamHandlers.cs`:

```csharp
using LightRAGNet.Share.Models;

namespace LightRAGNet.Web.Models;

public sealed class RagQueryStreamHandlers
{
    public Func<string, Task> OnChunkReceived { get; init; } = _ => Task.CompletedTask;

    public Func<QueryMetadataEvent, Task> OnMetadataReceived { get; init; } = _ => Task.CompletedTask;
}
```

Create `src/LightRAGNet.Web/Models/RagQueryException.cs`:

```csharp
namespace LightRAGNet.Web.Models;

public sealed class RagQueryException : Exception
{
    public RagQueryException(string error, string? message)
        : base(string.IsNullOrWhiteSpace(message) ? error : message)
    {
        Error = error;
    }

    public string Error { get; }
}
```

- [ ] **Step 4: Update ApiClient QueryRagAsync**

Modify `src/LightRAGNet.Web/ApiClient.cs`:

```csharp
using LightRAGNet.Share.Models;
using LightRAGNet.Web.Models;
```

Replace the current broad-swallowing query method with this shape:

```csharp
public Task QueryRagAsync(
    string query,
    Func<string, Task> onChunkReceived,
    CancellationToken cancellationToken = default)
{
    return QueryRagAsync(
        new RagQueryRequest { Query = query },
        new RagQueryStreamHandlers { OnChunkReceived = onChunkReceived },
        cancellationToken);
}

public async Task QueryRagAsync(
    RagQueryRequest request,
    RagQueryStreamHandlers handlers,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(handlers);

    var response = await httpClient.PostAsJsonAsync(
        "api/RagQuery/query",
        request,
        cancellationToken);

    response.EnsureSuccessStatusCode();

    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var reader = new StreamReader(stream);

    while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
    {
        var line = await reader.ReadLineAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
        {
            continue;
        }

        var json = line[6..];
        var ragEvent = JsonSerializer.Deserialize<RagQueryEvent>(
            json,
            JsonSerializerOptions);

        switch (ragEvent)
        {
            case TextChunkEvent textChunkEvent:
                await handlers.OnChunkReceived(textChunkEvent.Chunk);
                break;
            case QueryMetadataEvent metadataEvent:
                await handlers.OnMetadataReceived(metadataEvent);
                break;
            case ErrorEvent errorEvent:
                throw new RagQueryException(errorEvent.Error, errorEvent.Message);
            case DoneEvent:
                return;
        }
    }
}
```

Use the existing `JsonSerializerOptions` member if it already exists. If the file has only inline options today, extract the existing options into a private static member:

```csharp
private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() }
};
```

- [ ] **Step 5: Run source tests and Web build**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagQueryContractSourceTests
dotnet build .\src\LightRAGNet.Web\LightRAGNet.Web.csproj /p:UseAppHost=false
```

Expected: tests PASS, build 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add .\src\LightRAGNet.Web\Models\RagQueryStreamHandlers.cs .\src\LightRAGNet.Web\Models\RagQueryException.cs .\src\LightRAGNet.Web\ApiClient.cs .\tests\LightRAGNet.Tests\Web\RagQueryContractSourceTests.cs
git commit -m "fix: surface rag query stream errors"
```

---

### Task 4: Chat Message Metadata Model

**Files:**
- Modify: `src/LightRAGNet.Web/Services/ChatHistoryService.cs`
- Test: `tests/LightRAGNet.Tests/Web/RagChatSourceTests.cs`

- [ ] **Step 1: Write failing message model source tests**

Create `tests/LightRAGNet.Tests/Web/RagChatSourceTests.cs`:

```csharp
using FluentAssertions;

namespace LightRAGNet.Tests.Web;

public sealed class RagChatSourceTests
{
    [Fact]
    public void ChatMessageModel_StoresQueryMetadata()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Services", "ChatHistoryService.cs");

        source.Should().Contain("public QueryMode? Mode { get; set; }");
        source.Should().Contain("public bool IsStreaming { get; set; }");
        source.Should().Contain("public bool IsCacheable { get; set; }");
        source.Should().Contain("public List<RagQueryReferenceDto> References { get; set; } = [];");
        source.Should().Contain("public List<string> HighLevelKeywords { get; set; } = [];");
        source.Should().Contain("public List<string> LowLevelKeywords { get; set; } = [];");
        source.Should().Contain("public Dictionary<string, string> Diagnostics { get; set; } = [];");
        source.Should().Contain("public string? ErrorMessage { get; set; }");
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([repositoryRoot, .. relativeParts]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LightRAGNet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing LightRAGNet.slnx.");
    }
}
```

- [ ] **Step 2: Run the failing source test**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagChatSourceTests
```

Expected: FAIL because `ChatMessageModel` currently stores only `Role` and `Text`.

- [ ] **Step 3: Extend ChatMessageModel**

Modify `src/LightRAGNet.Web/Services/ChatHistoryService.cs`:

```csharp
using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;
```

Update the model:

```csharp
public class ChatMessageModel
{
    public string Role { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public QueryMode? Mode { get; set; }

    public bool IsStreaming { get; set; }

    public bool IsCacheable { get; set; }

    public List<RagQueryReferenceDto> References { get; set; } = [];

    public List<string> HighLevelKeywords { get; set; } = [];

    public List<string> LowLevelKeywords { get; set; } = [];

    public Dictionary<string, string> Diagnostics { get; set; } = [];

    public string? ErrorMessage { get; set; }
}
```

- [ ] **Step 4: Run message model source tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagChatSourceTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add .\src\LightRAGNet.Web\Services\ChatHistoryService.cs .\tests\LightRAGNet.Tests\Web\RagChatSourceTests.cs
git commit -m "feat: store chat query metadata"
```

---

### Task 5: RagChat UI Controls And Metadata Rendering

**Files:**
- Modify: `src/LightRAGNet.Web/Components/Pages/RagChat.razor`
- Test: `tests/LightRAGNet.Tests/Web/RagChatSourceTests.cs`

- [ ] **Step 1: Add failing source tests for Chat UI controls**

Append these tests to `tests/LightRAGNet.Tests/Web/RagChatSourceTests.cs`:

```csharp
[Fact]
public void RagChat_ProvidesQuerySettingsControls()
{
    var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagChat.razor");

    source.Should().Contain("MudToggleGroup");
    source.Should().Contain("_selectedMode");
    source.Should().Contain("_streamResponse");
    source.Should().Contain("_includeReferences");
    source.Should().Contain("BuildQueryRequest(userMessage)");
    source.Should().Contain("new RagQueryStreamHandlers");
}

[Fact]
public void RagChat_RendersAssistantMetadataAndErrors()
{
    var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagChat.razor");

    source.Should().Contain("ShouldRenderReferences(chatMessage)");
    source.Should().Contain("ShouldRenderDiagnostics(chatMessage)");
    source.Should().Contain("chatMessage.References.Count");
    source.Should().Contain("chatMessage.Diagnostics");
    source.Should().Contain("chatMessage.ErrorMessage");
    source.Should().Contain("ApplyMetadata(assistantMessage, metadataEvent)");
}
```

- [ ] **Step 2: Run failing Chat source tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagChatSourceTests
```

Expected: FAIL because the page has no settings controls or metadata render helpers.

- [ ] **Step 3: Add page state and request builder**

In `src/LightRAGNet.Web/Components/Pages/RagChat.razor`, add imports:

```razor
@using LightRAGNet.Core
@using LightRAGNet.Share.Models
@using LightRAGNet.Web.Models
```

Add state fields in the `@code` block:

```csharp
private QueryMode _selectedMode = QueryMode.Mix;
private bool _streamResponse = true;
private bool _includeReferences = true;
private bool _enableRerank = true;
private int _topK = 40;
private int _chunkTopK = 20;
private string _responseType = "Multiple Paragraphs";
private string _highLevelKeywordsText = string.Empty;
private string _lowLevelKeywordsText = string.Empty;
private bool _onlyNeedContext;
private bool _onlyNeedPrompt;
```

Add helpers:

```csharp
private RagQueryRequest BuildQueryRequest(string query)
{
    return new RagQueryRequest
    {
        Query = query,
        Mode = _selectedMode,
        Stream = _streamResponse,
        IncludeReferences = _selectedMode != QueryMode.Bypass && _includeReferences,
        ResponseType = _responseType,
        TopK = _topK,
        ChunkTopK = _chunkTopK,
        EnableRerank = _enableRerank,
        HighLevelKeywords = ParseKeywords(_highLevelKeywordsText),
        LowLevelKeywords = ParseKeywords(_lowLevelKeywordsText),
        OnlyNeedContext = _onlyNeedContext,
        OnlyNeedPrompt = _onlyNeedPrompt
    };
}

private static List<string> ParseKeywords(string value)
{
    return value
        .Split([',', '，', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

private static void ApplyMetadata(ChatMessageModel message, QueryMetadataEvent metadataEvent)
{
    message.Mode = metadataEvent.Mode;
    message.IsStreaming = metadataEvent.Stream;
    message.IsCacheable = !metadataEvent.Stream;
    message.References = metadataEvent.References.ToList();
    message.HighLevelKeywords = metadataEvent.HighLevelKeywords.ToList();
    message.LowLevelKeywords = metadataEvent.LowLevelKeywords.ToList();
    message.Diagnostics = metadataEvent.Diagnostics.ToDictionary(
        pair => pair.Key,
        pair => pair.Value,
        StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Add compact settings UI above the input**

Add this block above the existing input row:

```razor
<MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2" Class="mb-2 flex-wrap">
    <MudToggleGroup T="QueryMode" @bind-Value="_selectedMode" Color="Color.Primary" Size="Size.Small">
        <MudToggleItem Value="QueryMode.Mix" Text="Mix" />
        <MudToggleItem Value="QueryMode.Naive" Text="Naive" />
        <MudToggleItem Value="QueryMode.Bypass" Text="Bypass" />
        <MudToggleItem Value="QueryMode.Local" Text="Local" />
        <MudToggleItem Value="QueryMode.Global" Text="Global" />
        <MudToggleItem Value="QueryMode.Hybrid" Text="Hybrid" />
    </MudToggleGroup>

    <MudSwitch T="bool" @bind-Value="_streamResponse" Color="Color.Primary" Label="@(_streamResponse ? "Streaming" : "Cacheable")" />
    <MudSwitch T="bool" @bind-Value="_includeReferences" Color="Color.Primary" Disabled="@(_selectedMode == QueryMode.Bypass)" Label="References" />
</MudStack>

<MudExpansionPanels Elevation="0" Class="mb-2">
    <MudExpansionPanel Text="Advanced query options" Dense="true">
        <MudGrid Spacing="2">
            <MudItem xs="12" sm="4">
                <MudNumericField T="int" @bind-Value="_topK" Label="TopK" Min="1" Max="200" />
            </MudItem>
            <MudItem xs="12" sm="4">
                <MudNumericField T="int" @bind-Value="_chunkTopK" Label="ChunkTopK" Min="1" Max="200" />
            </MudItem>
            <MudItem xs="12" sm="4">
                <MudSelect T="string" @bind-Value="_responseType" Label="Response">
                    <MudSelectItem Value="@("Multiple Paragraphs")">Multiple Paragraphs</MudSelectItem>
                    <MudSelectItem Value="@("Single Paragraph")">Single Paragraph</MudSelectItem>
                    <MudSelectItem Value="@("Bullet Points")">Bullet Points</MudSelectItem>
                </MudSelect>
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField @bind-Value="_highLevelKeywordsText" Label="High-level keywords" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField @bind-Value="_lowLevelKeywordsText" Label="Low-level keywords" />
            </MudItem>
            <MudItem xs="12">
                <MudStack Row="true" Spacing="2" Class="flex-wrap">
                    <MudCheckBox T="bool" @bind-Value="_enableRerank" Label="Rerank" />
                    <MudCheckBox T="bool" @bind-Value="_onlyNeedContext" Label="Context only" />
                    <MudCheckBox T="bool" @bind-Value="_onlyNeedPrompt" Label="Prompt only" />
                </MudStack>
            </MudItem>
        </MudGrid>
    </MudExpansionPanel>
</MudExpansionPanels>
```

If the current MudBlazor version uses a different property name for `MudToggleItem` text, follow the compiler and keep the same control intent.

- [ ] **Step 5: Wire SendMessageAsync to full request and typed errors**

Inside `SendMessageAsync`, replace the string-only call with:

```csharp
var request = BuildQueryRequest(userMessage);
assistantMessage.Mode = request.Mode;
assistantMessage.IsStreaming = request.Stream;
assistantMessage.IsCacheable = !request.Stream;

await ApiClient.QueryRagAsync(
    request,
    new RagQueryStreamHandlers
    {
        OnChunkReceived = async chunk =>
        {
            assistantMessage.Text += chunk;
            await InvokeAsync(StateHasChanged);
        },
        OnMetadataReceived = async metadataEvent =>
        {
            ApplyMetadata(assistantMessage, metadataEvent);
            await InvokeAsync(StateHasChanged);
        }
    },
    queryLease.Token);
```

Update exception handling:

```csharp
catch (RagQueryException ex)
{
    assistantMessage.ErrorMessage = ex.Message;
    if (string.IsNullOrWhiteSpace(assistantMessage.Text))
    {
        assistantMessage.Text = "Query failed.";
    }

    Snackbar.Add(ex.Message, Severity.Error);
}
catch (OperationCanceledException) when (queryLease.Token.IsCancellationRequested)
{
    assistantMessage.ErrorMessage = "Cancelled";
}
```

Do not add a broad `catch (Exception)` unless it logs and surfaces the error. If a broad catch is needed for UI safety, use this exact shape:

```csharp
catch (Exception ex)
{
    assistantMessage.ErrorMessage = ex.Message;
    if (string.IsNullOrWhiteSpace(assistantMessage.Text))
    {
        assistantMessage.Text = "Query failed.";
    }

    Snackbar.Add(ex.Message, Severity.Error);
}
```

- [ ] **Step 6: Render assistant metadata**

Add lightweight render predicates to the `@code` block:

```csharp
private static bool ShouldRenderReferences(ChatMessageModel chatMessage)
{
    return chatMessage.Role == "assistant" && chatMessage.References.Count > 0;
}

private static bool ShouldRenderDiagnostics(ChatMessageModel chatMessage)
{
    return chatMessage.Role == "assistant" &&
        (chatMessage.Diagnostics.Count > 0 ||
         chatMessage.HighLevelKeywords.Count > 0 ||
         chatMessage.LowLevelKeywords.Count > 0);
}
```

Render this markup inside each assistant message bubble directly below `MudMarkdown`:

```razor
@if (chatMessage.Role == "assistant")
{
    <MudMarkdown Value="@chatMessage.Text" />
    <MudStack Row="true" Spacing="1" Class="mt-1 flex-wrap">
        @if (chatMessage.Mode is not null)
        {
            <MudChip T="string" Size="Size.Small" Variant="Variant.Outlined">@chatMessage.Mode</MudChip>
        }
        <MudChip T="string" Size="Size.Small" Variant="Variant.Outlined">
            @(chatMessage.IsCacheable ? "Cacheable" : "Streaming")
        </MudChip>
        @if (!string.IsNullOrWhiteSpace(chatMessage.ErrorMessage))
        {
            <MudChip T="string" Size="Size.Small" Color="Color.Error">@chatMessage.ErrorMessage</MudChip>
        }
    </MudStack>

    @if (ShouldRenderReferences(chatMessage))
    {
        <MudExpansionPanels Elevation="0" Class="mt-2">
            <MudExpansionPanel Text="@($"References ({chatMessage.References.Count})")" Dense="true">
                <MudList T="RagQueryReferenceDto" Dense="true">
                    @foreach (var reference in chatMessage.References)
                    {
                        <MudListItem>
                            <MudText Typo="Typo.body2">@reference.FilePath</MudText>
                            <MudText Typo="Typo.caption">@reference.ReferenceId</MudText>
                        </MudListItem>
                    }
                </MudList>
            </MudExpansionPanel>
        </MudExpansionPanels>
    }

    @if (ShouldRenderDiagnostics(chatMessage))
    {
        <MudExpansionPanels Elevation="0" Class="mt-2">
            <MudExpansionPanel Text="Diagnostics" Dense="true">
                @foreach (var keyword in chatMessage.HighLevelKeywords)
                {
                    <MudChip T="string" Size="Size.Small" Variant="Variant.Outlined">HL: @keyword</MudChip>
                }
                @foreach (var keyword in chatMessage.LowLevelKeywords)
                {
                    <MudChip T="string" Size="Size.Small" Variant="Variant.Outlined">LL: @keyword</MudChip>
                }
                @foreach (var diagnostic in chatMessage.Diagnostics)
                {
                    <MudText Typo="Typo.caption">@diagnostic.Key: @diagnostic.Value</MudText>
                }
            </MudExpansionPanel>
        </MudExpansionPanels>
    }
}
```

- [ ] **Step 7: Run Chat tests and Web build**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagChatSourceTests
dotnet build .\src\LightRAGNet.Web\LightRAGNet.Web.csproj /p:UseAppHost=false
```

Expected: tests PASS, build 0 errors.

- [ ] **Step 8: Commit**

```powershell
git add .\src\LightRAGNet.Web\Components\Pages\RagChat.razor .\tests\LightRAGNet.Tests\Web\RagChatSourceTests.cs
git commit -m "feat: adapt chat query controls"
```

---

### Task 6: Full Verification, Asset Gate, And Final Commit Hygiene

**Files:**
- Potentially create or update: `docs/superpowers/archives/2026-05/2026-05-20-chat-query-ui-adaptation-archive.md`
- Potentially update: `docs/superpowers/archives/INDEX.md`
- Potentially create or update: `docs/superpowers/problems/` or `docs/superpowers/inbox/` only if implementation exposes a reusable failure mode.

- [ ] **Step 1: Run focused tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagQueryContractSourceTests
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter FullyQualifiedName~RagChatSourceTests
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter FullyQualifiedName~RagQueryRequestMapperTests
```

Expected: all focused tests PASS.

- [ ] **Step 2: Run solution verification**

Run:

```powershell
dotnet test .\LightRAGNet.slnx
```

Expected: all tests PASS. If the Web executable is locked by a running app, also run:

```powershell
dotnet build .\src\LightRAGNet.Web\LightRAGNet.Web.csproj /p:UseAppHost=false
```

Expected: 0 errors.

- [ ] **Step 3: Search for old swallowed query errors**

Run:

```powershell
rg -n "Ignore exceptions|catch \(Exception\)|ErrorEvent.*break|new \{ query \}" src\LightRAGNet.Web src\LightRAGNet.Server
```

Expected: no match for the old `ApiClient.QueryRagAsync` broad-swallow path. A broad UI catch in `RagChat.razor` is acceptable only if it sets `assistantMessage.ErrorMessage` and shows `Snackbar.Add`.

- [ ] **Step 4: Archive completed requirement**

Use `superpowers-asset-compounding:using-asset-compounding` before close-out. If implementation is accepted and verified, create a requirement archive that records:

```text
event_type: completed_requirement
topic: chat-query-ui-adaptation
delivered:
  - shared RagQueryRequest contract
  - metadata SSE event
  - server request mapping
  - ApiClient typed error surfacing
  - Chat query controls and metadata rendering
verification:
  - focused source tests
  - server mapper tests
  - solution test or scoped build evidence
```

Run the archive validator if a formal archive is created:

```powershell
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\archive-superpowers-feature\scripts\validate_archive_asset.py .\docs\superpowers\archives\2026-05\2026-05-20-chat-query-ui-adaptation-archive.md
```

Expected: validator reports success.

- [ ] **Step 5: Run index checks**

Run:

```powershell
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.3\skills\compound-development-asset\scripts\check_indexes.py . --json
```

Expected: `issues` is an empty array, or every issue is unrelated pre-existing drift and is stated in the handoff.

- [ ] **Step 6: Commit asset updates**

If Task 6 creates or updates docs assets:

```powershell
git add .\docs\superpowers\archives .\docs\superpowers\problems .\docs\superpowers\inbox
git commit -m "docs: archive chat query ui adaptation"
```

If no docs assets changed because implementation is not yet accepted as complete, skip this commit and explain why in the handoff.

- [ ] **Step 7: Final handoff**

Include:

```text
summary:
- Added chat query settings, shared request metadata contracts, server mapping, and visible query error handling.
- Added focused Web source tests, server mapper tests, and requirement archive coverage.

verification:
- dotnet test .\LightRAGNet.slnx: PASS

asset_gate:
  event_type: completed_requirement
  route: archive
  reason: chat query UI adaptation is a coherent accepted requirement with spec, plan, implementation, and verification evidence
  evidence: archive validator PASS; check_indexes.py issues []
  related_assets: docs/superpowers/specs/2026-05-20-chat-query-ui-adaptation-design.md; docs/superpowers/plans/2026-05-20-chat-query-ui-adaptation-implementation-plan.md; docs/superpowers/archives/2026-05/2026-05-20-chat-query-ui-adaptation-archive.md
  asset_candidates: none unless implementation exposes a new reusable failure mode
  deferred_signals: none unless verification leaves a follow-up
  next_step: run the chat page manually against a local RAG workspace to inspect UX spacing and metadata readability
```

---

## Plan Self-Review

- Spec coverage: all acceptance criteria from `docs/superpowers/specs/2026-05-20-chat-query-ui-adaptation-design.md` map to Tasks 1-6.
- Scope control: no task changes core RAG ranking, cache key semantics, persistent chat sessions, or cache management UI.
- Type consistency: `RagQueryRequest`, `QueryMetadataEvent`, `RagQueryReferenceDto`, `RagQueryStreamHandlers`, and `RagQueryException` are introduced before later tasks use them.
- Testability: each implementation task starts with a failing source or unit test and ends with a focused verification command.
- Placeholder scan: the plan contains no open-ended placeholders; version-specific MudBlazor syntax risk is handled by compiler-guided adjustment while preserving explicit control intent.
