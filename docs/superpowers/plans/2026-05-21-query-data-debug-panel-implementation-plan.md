# Query Data Debug Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a message-level "查看检索数据" action to RAG Chat that retrieves and displays the structured raw retrieval data for the assistant response being inspected.

**Architecture:** Keep normal chat on the existing SSE `/api/RagQuery/query` path. Add a separate JSON `/api/RagQuery/data` endpoint that reuses `LightRAG.QueryAsync` with a cloned, forced debug request (`Stream=false`, `OnlyNeedContext=true`, `OnlyNeedPrompt=false`, `IncludeReferences=true`). Store the original query request snapshot on each assistant message so the UI inspects the request that produced that response, not the current toolbar state.

**Tech Stack:** .NET 10, ASP.NET Core MVC, Blazor Server, MudBlazor, System.Text.Json, xUnit, FluentAssertions.

---

## Scope Check

This plan covers one subsystem: message-level retrieval data inspection for existing Blazor RAG Chat. It does not implement graph curation, React graph workbench, retrieval ranking changes, prompt changes, cache key changes, or persistent audit storage.

## File Structure

- Create: `src/LightRAGNet.Share/Models/RagQueryDataResponse.cs`
  - Shared JSON response contract for `/api/RagQuery/data`.
- Modify: `src/LightRAGNet.Server/Controllers/RagQueryController.cs`
  - Add `POST data` endpoint.
  - Add helpers to force the request into retrieval-data mode and split `QueryResult.RawData`.
- Modify: `src/LightRAGNet.Web/ApiClient.cs`
  - Add `GetRagQueryDataAsync`.
- Modify: `src/LightRAGNet.Web/Models/ChatMessageModel.cs`
  - Store the original query request snapshot for assistant messages.
  - Store transient retrieval data loading/error/result state.
- Modify: `src/LightRAGNet.Web/Models/ChatQuerySettingsModel.cs`
  - Add a request clone helper used by chat history and the data button.
- Create: `src/LightRAGNet.Web/Components/Pages/RagQueryDataDialog.razor`
  - Render structured retrieval data in grouped panels plus raw JSON.
- Modify: `src/LightRAGNet.Web/Components/Pages/RagChat.razor`
  - Save request snapshot on assistant messages.
  - Add message-level "查看检索数据" action.
  - Open the dialog and call the API on demand.
- Modify tests:
  - `tests/LightRAGNet.Tests/Web/RagQueryContractSourceTests.cs`
  - `tests/LightRAGNet.Server.Tests/RagQueryControllerSourceTests.cs`
  - `tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs`
  - `tests/LightRAGNet.Web.Tests/ChatMessageModelTests.cs`
  - `tests/LightRAGNet.Web.Tests/ChatQuerySettingsModelTests.cs`
  - `tests/LightRAGNet.Web.Tests/ApiClientQueryRagTests.cs`
  - `tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs`

## Task 1: Add Shared Query Data Contract

**Files:**
- Create: `src/LightRAGNet.Share/Models/RagQueryDataResponse.cs`
- Modify: `tests/LightRAGNet.Tests/Web/RagQueryContractSourceTests.cs`

- [ ] **Step 1: Write the failing source contract test**

Append this test to `tests/LightRAGNet.Tests/Web/RagQueryContractSourceTests.cs`:

```csharp
[Fact]
public void RagQueryDataResponse_ExposesStructuredDataContract()
{
    var source = ReadRepositoryFile("src", "LightRAGNet.Share", "Models", "RagQueryDataResponse.cs");

    source.Should().Contain("public sealed class RagQueryDataResponse");
    source.Should().Contain("public string Status { get; init; } = \"success\";");
    source.Should().Contain("public string Message { get; init; } = string.Empty;");
    source.Should().Contain("public Dictionary<string, object> Data { get; init; } = [];");
    source.Should().Contain("public Dictionary<string, object> Metadata { get; init; } = [];");
}
```

- [ ] **Step 2: Run the test to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RagQueryContractSourceTests --verbosity minimal
```

Expected: FAIL because `RagQueryDataResponse.cs` does not exist.

- [ ] **Step 3: Add the shared response model**

Create `src/LightRAGNet.Share/Models/RagQueryDataResponse.cs`:

```csharp
namespace LightRAGNet.Share.Models;

public sealed class RagQueryDataResponse
{
    public string Status { get; init; } = "success";
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, object> Data { get; init; } = [];
    public Dictionary<string, object> Metadata { get; init; } = [];
}
```

- [ ] **Step 4: Run the focused contract test to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RagQueryContractSourceTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 5: Commit Task 1**

Run:

```powershell
git add src/LightRAGNet.Share/Models/RagQueryDataResponse.cs tests/LightRAGNet.Tests/Web/RagQueryContractSourceTests.cs
git commit -m "feat: add query data response contract"
```

Expected: commit succeeds.

## Task 2: Add Server Query Data Endpoint

**Files:**
- Modify: `src/LightRAGNet.Server/Controllers/RagQueryController.cs`
- Modify: `tests/LightRAGNet.Server.Tests/RagQueryControllerSourceTests.cs`
- Modify: `tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs`

- [ ] **Step 1: Add failing source test for endpoint shape**

Append this test to `tests/LightRAGNet.Server.Tests/RagQueryControllerSourceTests.cs`:

```csharp
[Fact]
public void QueryDataAsync_ExposesJsonEndpointAndForcesRetrievalDataMode()
{
    var source = ReadRepositoryFile("src", "LightRAGNet.Server", "Controllers", "RagQueryController.cs");

    source.Should().Contain("[HttpPost(\"data\")]");
    source.Should().Contain("public async Task<ActionResult<RagQueryDataResponse>> QueryDataAsync(");
    source.Should().Contain("ForceRetrievalDataRequest(request)");
    source.Should().Contain("Stream = false");
    source.Should().Contain("IncludeReferences = true");
    source.Should().Contain("OnlyNeedContext = true");
    source.Should().Contain("OnlyNeedPrompt = false");
    source.Should().Contain("SplitRawData(queryResult.RawData)");
}
```

- [ ] **Step 2: Add failing mapper unit test for clone-for-data semantics**

Append this test to `tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs`:

```csharp
[Fact]
public void ForceRetrievalDataRequest_PreservesRetrievalOptionsAndForcesDebugFlags()
{
    var request = new RagQueryRequest
    {
        Query = "explain indexing",
        Mode = QueryMode.Hybrid,
        Stream = true,
        IncludeReferences = false,
        ResponseType = "Bullet Points",
        TopK = 11,
        ChunkTopK = 7,
        EnableRerank = false,
        HighLevelKeywords = ["index"],
        LowLevelKeywords = ["chunk"],
        OnlyNeedContext = false,
        OnlyNeedPrompt = true
    };

    var forced = RagQueryRequestMapper.ForceRetrievalDataRequest(request);

    forced.Should().NotBeSameAs(request);
    forced.Query.Should().Be("explain indexing");
    forced.Mode.Should().Be(QueryMode.Hybrid);
    forced.Stream.Should().BeFalse();
    forced.IncludeReferences.Should().BeTrue();
    forced.ResponseType.Should().Be("Bullet Points");
    forced.TopK.Should().Be(11);
    forced.ChunkTopK.Should().Be(7);
    forced.EnableRerank.Should().BeFalse();
    forced.HighLevelKeywords.Should().Equal("index");
    forced.LowLevelKeywords.Should().Equal("chunk");
    forced.OnlyNeedContext.Should().BeTrue();
    forced.OnlyNeedPrompt.Should().BeFalse();

    request.HighLevelKeywords.Add("mutated");
    forced.HighLevelKeywords.Should().Equal("index");
}
```

- [ ] **Step 3: Run tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RagQueryControllerSourceTests|FullyQualifiedName~RagQueryRequestMapperTests" --verbosity minimal
```

Expected: FAIL because endpoint and mapper helper do not exist.

- [ ] **Step 4: Add mapper helper**

In `src/LightRAGNet.Server/Services/RagQueryRequestMapper.cs`, add this public method after `ToQueryParam`:

```csharp
public static RagQueryRequest ForceRetrievalDataRequest(RagQueryRequest request)
{
    ArgumentNullException.ThrowIfNull(request);

    return new RagQueryRequest
    {
        Query = request.Query,
        Mode = request.Mode,
        Stream = false,
        IncludeReferences = true,
        ResponseType = request.ResponseType,
        TopK = request.TopK,
        ChunkTopK = request.ChunkTopK,
        EnableRerank = request.EnableRerank,
        HighLevelKeywords = NormalizeKeywords(request.HighLevelKeywords),
        LowLevelKeywords = NormalizeKeywords(request.LowLevelKeywords),
        OnlyNeedContext = true,
        OnlyNeedPrompt = false
    };
}
```

- [ ] **Step 5: Add controller endpoint and raw data splitter**

In `src/LightRAGNet.Server/Controllers/RagQueryController.cs`, add this method before `WrapQueryResultAsEventsAsync`:

```csharp
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
            Message = ex.Message
        });
    }
}
```

Add this helper below the endpoint:

```csharp
private static (Dictionary<string, object> Data, Dictionary<string, object> Metadata) SplitRawData(
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
```

- [ ] **Step 6: Run focused server tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RagQueryControllerSourceTests|FullyQualifiedName~RagQueryRequestMapperTests" --verbosity minimal
```

Expected: PASS.

- [ ] **Step 7: Commit Task 2**

Run:

```powershell
git add src/LightRAGNet.Server/Controllers/RagQueryController.cs src/LightRAGNet.Server/Services/RagQueryRequestMapper.cs tests/LightRAGNet.Server.Tests/RagQueryControllerSourceTests.cs tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs
git commit -m "feat: add query data endpoint"
```

Expected: commit succeeds.

## Task 3: Store Assistant Request Snapshots

**Files:**
- Modify: `src/LightRAGNet.Web/Models/ChatMessageModel.cs`
- Modify: `src/LightRAGNet.Web/Models/ChatQuerySettingsModel.cs`
- Modify: `tests/LightRAGNet.Web.Tests/ChatMessageModelTests.cs`
- Modify: `tests/LightRAGNet.Web.Tests/ChatQuerySettingsModelTests.cs`

- [ ] **Step 1: Add failing tests for message state and deep clone**

Append this test to `tests/LightRAGNet.Web.Tests/ChatMessageModelTests.cs`:

```csharp
[Fact]
public void ChatMessageModel_DefaultsRetrievalDataState()
{
    var message = new ChatMessageModel();

    message.RetrievalDataRequest.Should().BeNull();
    message.RetrievalData.Should().BeNull();
    message.IsLoadingRetrievalData.Should().BeFalse();
    message.RetrievalDataError.Should().BeNull();
}
```

Append this test to `tests/LightRAGNet.Web.Tests/ChatQuerySettingsModelTests.cs`:

```csharp
[Fact]
public void CloneRequest_CopiesListsSoHistoryDoesNotFollowToolbarMutation()
{
    var request = new RagQueryRequest
    {
        Query = "inspect retrieval",
        Mode = QueryMode.Mix,
        Stream = true,
        IncludeReferences = true,
        ResponseType = "Concise",
        TopK = 5,
        ChunkTopK = 3,
        EnableRerank = true,
        HighLevelKeywords = ["graph"],
        LowLevelKeywords = ["chunk"],
        OnlyNeedContext = false,
        OnlyNeedPrompt = false
    };

    var clone = ChatQuerySettingsModel.CloneRequest(request);

    clone.Should().NotBeSameAs(request);
    clone.HighLevelKeywords.Should().NotBeSameAs(request.HighLevelKeywords);
    clone.LowLevelKeywords.Should().NotBeSameAs(request.LowLevelKeywords);
    clone.HighLevelKeywords.Should().Equal("graph");
    clone.LowLevelKeywords.Should().Equal("chunk");

    request.HighLevelKeywords.Add("mutated");
    request.LowLevelKeywords.Add("changed");

    clone.HighLevelKeywords.Should().Equal("graph");
    clone.LowLevelKeywords.Should().Equal("chunk");
}
```

- [ ] **Step 2: Run Web model tests to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~ChatMessageModelTests|FullyQualifiedName~ChatQuerySettingsModelTests" --verbosity minimal
```

Expected: FAIL because properties and clone helper do not exist.

- [ ] **Step 3: Add message state**

In `src/LightRAGNet.Web/Models/ChatMessageModel.cs`, add these properties before `ErrorMessage`:

```csharp
/// <summary>
/// Original request snapshot used to produce this assistant response.
/// </summary>
public RagQueryRequest? RetrievalDataRequest { get; set; }

/// <summary>
/// Structured retrieval data loaded on demand for this assistant response.
/// </summary>
public RagQueryDataResponse? RetrievalData { get; set; }

/// <summary>
/// Indicates whether retrieval data is currently loading.
/// </summary>
public bool IsLoadingRetrievalData { get; set; }

/// <summary>
/// Retrieval data loading error.
/// </summary>
public string? RetrievalDataError { get; set; }
```

- [ ] **Step 4: Add request clone helper**

In `src/LightRAGNet.Web/Models/ChatQuerySettingsModel.cs`, add this method after `BuildRequest`:

```csharp
public static RagQueryRequest CloneRequest(RagQueryRequest request)
{
    ArgumentNullException.ThrowIfNull(request);

    return new RagQueryRequest
    {
        Query = request.Query,
        Mode = request.Mode,
        Stream = request.Stream,
        IncludeReferences = request.IncludeReferences,
        ResponseType = request.ResponseType,
        TopK = request.TopK,
        ChunkTopK = request.ChunkTopK,
        EnableRerank = request.EnableRerank,
        HighLevelKeywords = [.. request.HighLevelKeywords],
        LowLevelKeywords = [.. request.LowLevelKeywords],
        OnlyNeedContext = request.OnlyNeedContext,
        OnlyNeedPrompt = request.OnlyNeedPrompt
    };
}
```

- [ ] **Step 5: Run Web model tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~ChatMessageModelTests|FullyQualifiedName~ChatQuerySettingsModelTests" --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit Task 3**

Run:

```powershell
git add src/LightRAGNet.Web/Models/ChatMessageModel.cs src/LightRAGNet.Web/Models/ChatQuerySettingsModel.cs tests/LightRAGNet.Web.Tests/ChatMessageModelTests.cs tests/LightRAGNet.Web.Tests/ChatQuerySettingsModelTests.cs
git commit -m "feat: store chat query data snapshots"
```

Expected: commit succeeds.

## Task 4: Add Web API Client Method

**Files:**
- Modify: `src/LightRAGNet.Web/ApiClient.cs`
- Modify: `tests/LightRAGNet.Web.Tests/ApiClientQueryRagTests.cs`

- [ ] **Step 1: Add failing ApiClient test**

Append this test to `tests/LightRAGNet.Web.Tests/ApiClientQueryRagTests.cs`:

```csharp
[Fact]
public async Task GetRagQueryDataAsync_PostsToQueryDataEndpoint()
{
    HttpRequestMessage? capturedRequest = null;
    var response = new RagQueryDataResponse
    {
        Status = "success",
        Message = "Retrieval data returned.",
        Data = new Dictionary<string, object>
        {
            ["chunks"] = new[] { "chunk-a" }
        },
        Metadata = new Dictionary<string, object>
        {
            ["query_mode"] = "Mix"
        }
    };
    var client = CreateClient(new CapturingHandler(request =>
    {
        capturedRequest = request;
        return JsonResponse(response);
    }));

    var result = await client.GetRagQueryDataAsync(new RagQueryRequest
    {
        Query = "inspect",
        Mode = QueryMode.Mix
    });

    capturedRequest.Should().NotBeNull();
    capturedRequest!.Method.Should().Be(HttpMethod.Post);
    capturedRequest.RequestUri!.ToString().Should().Be("api/RagQuery/data");
    result.Should().NotBeNull();
    result!.Status.Should().Be("success");
    result.Metadata.Should().ContainKey("query_mode");
}
```

If `JsonResponse` does not exist in this test file, add this helper near the existing response helpers:

```csharp
private static HttpResponseMessage JsonResponse<T>(T value)
{
    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(value)
    };
}
```

Add required usings if missing:

```csharp
using System.Net;
using System.Net.Http.Json;
```

- [ ] **Step 2: Run ApiClient test to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter FullyQualifiedName~ApiClientQueryRagTests --verbosity minimal
```

Expected: FAIL because `GetRagQueryDataAsync` does not exist.

- [ ] **Step 3: Add ApiClient method**

In `src/LightRAGNet.Web/ApiClient.cs`, add this method after `QueryRagAsync` overloads and before `ItemParser`:

```csharp
public async Task<RagQueryDataResponse?> GetRagQueryDataAsync(
    RagQueryRequest request,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(request);

    using var response = await httpClient.PostAsJsonAsync(
        "api/RagQuery/data",
        request,
        cancellationToken);

    response.EnsureSuccessStatusCode();

    return await response.Content.ReadFromJsonAsync<RagQueryDataResponse>(
        cancellationToken: cancellationToken);
}
```

- [ ] **Step 4: Run ApiClient test to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter FullyQualifiedName~ApiClientQueryRagTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 5: Commit Task 4**

Run:

```powershell
git add src/LightRAGNet.Web/ApiClient.cs tests/LightRAGNet.Web.Tests/ApiClientQueryRagTests.cs
git commit -m "feat: add query data api client"
```

Expected: commit succeeds.

## Task 5: Add Retrieval Data Dialog

**Files:**
- Create: `src/LightRAGNet.Web/Components/Pages/RagQueryDataDialog.razor`
- Modify: `tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs`

- [ ] **Step 1: Add failing source test for dialog sections**

Append this test to `tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs`:

```csharp
[Fact]
public void RagQueryDataDialog_RendersGroupedRetrievalDataSections()
{
    var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagQueryDataDialog.razor");

    source.Should().Contain("@using System.Text.Json");
    source.Should().Contain("[CascadingParameter] IMudDialogInstance MudDialog");
    source.Should().Contain("[Parameter] public RagQueryDataResponse? RetrievalData { get; set; }");
    source.Should().Contain("Entities");
    source.Should().Contain("Relationships");
    source.Should().Contain("Chunks");
    source.Should().Contain("References");
    source.Should().Contain("Metadata");
    source.Should().Contain("Raw JSON");
    source.Should().Contain("SerializeSection");
}
```

- [ ] **Step 2: Run source test to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter FullyQualifiedName~RagChatSourceTests --verbosity minimal
```

Expected: FAIL because dialog file does not exist.

- [ ] **Step 3: Create dialog component**

Create `src/LightRAGNet.Web/Components/Pages/RagQueryDataDialog.razor`:

```razor
@using System.Text.Json
@using LightRAGNet.Core.Utils
@using LightRAGNet.Share.Models

<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">Retrieval Data</MudText>
    </TitleContent>
    <DialogContent>
        @if (!string.IsNullOrWhiteSpace(Query))
        {
            <MudText Typo="Typo.caption" Color="Color.Secondary" Class="mb-2">@Query</MudText>
        }

        @if (!string.IsNullOrWhiteSpace(ErrorMessage))
        {
            <MudAlert Severity="Severity.Error">@ErrorMessage</MudAlert>
        }
        else if (RetrievalData is null)
        {
            <MudText Typo="Typo.body2" Color="Color.Secondary">No retrieval data returned for this response.</MudText>
        }
        else
        {
            <MudTabs Rounded="true" Border="true" Elevation="0">
                <MudTabPanel Text="Entities">
                    <MudCodeBlock Code="@SerializeSection("entities")" Language="json" />
                </MudTabPanel>
                <MudTabPanel Text="Relationships">
                    <MudCodeBlock Code="@SerializeSection("relationships")" Language="json" />
                </MudTabPanel>
                <MudTabPanel Text="Chunks">
                    <MudCodeBlock Code="@SerializeSection("chunks")" Language="json" />
                </MudTabPanel>
                <MudTabPanel Text="References">
                    <MudCodeBlock Code="@SerializeSection("references")" Language="json" />
                </MudTabPanel>
                <MudTabPanel Text="Metadata">
                    <MudCodeBlock Code="@SerializeObject(RetrievalData.Metadata)" Language="json" />
                </MudTabPanel>
                <MudTabPanel Text="Raw JSON">
                    <MudCodeBlock Code="@SerializeObject(RetrievalData)" Language="json" />
                </MudTabPanel>
            </MudTabs>
        }
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Close">Close</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public RagQueryDataResponse? RetrievalData { get; set; }
    [Parameter] public string? Query { get; set; }
    [Parameter] public string? ErrorMessage { get; set; }

    private string SerializeSection(string key)
    {
        if (RetrievalData?.Data.TryGetValue(key, out var value) != true)
        {
            return "[]";
        }

        return SerializeObject(value);
    }

    private static string SerializeObject(object? value)
    {
        return JsonSerializer.Serialize(value, LightRAGJsonOptions.HumanReadableIndented);
    }

    private void Close()
    {
        MudDialog.Close();
    }
}
```

- [ ] **Step 4: Run source test to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter FullyQualifiedName~RagChatSourceTests --verbosity minimal
```

Expected: PASS for dialog source test. If the component does not compile because `MudCodeBlock` is unavailable in this MudBlazor version, replace each `MudCodeBlock` with:

```razor
<pre class="mud-typography-body2">@SerializeSection("entities")</pre>
```

and update the source assertion from `MudCodeBlock` to `SerializeSection`.

- [ ] **Step 5: Commit Task 5**

Run:

```powershell
git add src/LightRAGNet.Web/Components/Pages/RagQueryDataDialog.razor tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs
git commit -m "feat: add query data dialog"
```

Expected: commit succeeds.

## Task 6: Wire Message-Level Button In Chat

**Files:**
- Modify: `src/LightRAGNet.Web/Components/Pages/RagChat.razor`
- Modify: `tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs`

- [ ] **Step 1: Add failing source test for message-level action**

Append this test to `tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs`:

```csharp
[Fact]
public void RagChat_WiresMessageLevelRetrievalDataAction()
{
    var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagChat.razor");

    source.Should().Contain("@inject IDialogService DialogService");
    source.Should().Contain("查看检索数据");
    source.Should().Contain("CanShowRetrievalDataButton(chatMessage)");
    source.Should().Contain("LoadRetrievalDataAsync(chatMessage)");
    source.Should().Contain("OpenRetrievalDataDialog(chatMessage)");
    source.Should().Contain("assistantMessage.RetrievalDataRequest = ChatQuerySettingsModel.CloneRequest(request);");
    source.Should().Contain("message.Mode != QueryMode.Bypass");
    source.Should().Contain("await ApiClient.GetRagQueryDataAsync(message.RetrievalDataRequest");
}
```

- [ ] **Step 2: Run source test to verify RED**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter FullyQualifiedName~RagChatSourceTests --verbosity minimal
```

Expected: FAIL because the button and helpers do not exist.

- [ ] **Step 3: Inject dialog service**

In `src/LightRAGNet.Web/Components/Pages/RagChat.razor`, add this line with the other injections:

```razor
@inject IDialogService DialogService
```

- [ ] **Step 4: Add the message button**

Inside the assistant message rendering block, after diagnostics panels and before the `else` branch for user messages, add:

```razor
@if (CanShowRetrievalDataButton(chatMessage))
{
    <div class="d-flex justify-end mt-2">
        <MudTooltip Text="查看这次回复使用的实体、关系、文本片段和引用。">
            <span>
                <MudButton Variant="Variant.Text"
                           Size="Size.Small"
                           StartIcon="@Icons.Material.Filled.ManageSearch"
                           Disabled="@chatMessage.IsLoadingRetrievalData"
                           OnClick="@(() => LoadRetrievalDataAsync(chatMessage))">
                    查看检索数据
                </MudButton>
            </span>
        </MudTooltip>
    </div>
}
```

- [ ] **Step 5: Save the request snapshot when creating assistant messages**

In `SendMessageAsync`, update the `assistantMessage` initializer to include:

```csharp
RetrievalDataRequest = ChatQuerySettingsModel.CloneRequest(request),
```

The initializer should look like:

```csharp
var assistantMessage = new ChatMessageModel
{
    Role = "Assistant",
    Text = string.Empty,
    Mode = request.Mode,
    IsStreaming = request.Stream,
    IsCacheable = !request.Stream,
    HighLevelKeywords = [.. request.HighLevelKeywords],
    LowLevelKeywords = [.. request.LowLevelKeywords],
    RetrievalDataRequest = ChatQuerySettingsModel.CloneRequest(request)
};
```

- [ ] **Step 6: Add button visibility and loading helpers**

Add these methods near `ShouldRenderDiagnostics`:

```csharp
private static bool CanShowRetrievalDataButton(ChatMessageModel message)
{
    return message is
    {
        Role: "Assistant",
        IsComplete: true,
        RetrievalDataRequest: not null,
        Mode: not null
    } && message.Mode != QueryMode.Bypass;
}

private async Task LoadRetrievalDataAsync(ChatMessageModel message)
{
    if (message.RetrievalDataRequest is null || message.IsLoadingRetrievalData)
    {
        return;
    }

    message.IsLoadingRetrievalData = true;
    message.RetrievalDataError = null;
    StateHasChanged();

    try
    {
        message.RetrievalData = await ApiClient.GetRagQueryDataAsync(
            message.RetrievalDataRequest,
            CancellationToken.None);
    }
    catch (Exception ex)
    {
        message.RetrievalDataError = ex.Message;
        Snackbar.Add($"Retrieval data failed: {ex.Message}", Severity.Error);
    }
    finally
    {
        message.IsLoadingRetrievalData = false;
        OpenRetrievalDataDialog(message);
        StateHasChanged();
    }
}

private void OpenRetrievalDataDialog(ChatMessageModel message)
{
    var parameters = new DialogParameters<RagQueryDataDialog>
    {
        { dialog => dialog.RetrievalData, message.RetrievalData },
        { dialog => dialog.Query, message.RetrievalDataRequest?.Query },
        { dialog => dialog.ErrorMessage, message.RetrievalDataError }
    };

    var options = new DialogOptions
    {
        FullWidth = true,
        MaxWidth = MaxWidth.Large,
        CloseButton = true
    };

    DialogService.Show<RagQueryDataDialog>("Retrieval Data", parameters, options);
}
```

- [ ] **Step 7: Run Web source tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter FullyQualifiedName~RagChatSourceTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 8: Commit Task 6**

Run:

```powershell
git add src/LightRAGNet.Web/Components/Pages/RagChat.razor tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs
git commit -m "feat: show query data action in chat"
```

Expected: commit succeeds.

## Task 7: Focused Verification And Build

**Files:**
- Modify only if tests reveal regressions in files already touched by Tasks 1-6.

- [ ] **Step 1: Run focused core contract tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --no-restore --filter FullyQualifiedName~RagQueryContractSourceTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 2: Run focused server tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RagQueryControllerSourceTests|FullyQualifiedName~RagQueryRequestMapperTests" --verbosity minimal
```

Expected: PASS.

- [ ] **Step 3: Run focused Web tests**

Run:

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~ApiClientQueryRagTests|FullyQualifiedName~ChatMessageModelTests|FullyQualifiedName~ChatQuerySettingsModelTests|FullyQualifiedName~RagChatSourceTests" --verbosity minimal
```

Expected: PASS.

- [ ] **Step 4: Run full solution tests**

Run:

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: PASS for all test projects. If restore assets are stale, run `dotnet restore .\LightRAGNet.slnx`, then rerun this command.

- [ ] **Step 5: Run full build**

Run:

```powershell
dotnet build .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: build succeeds with `0` errors. Existing `NU1900` vulnerability-source warnings may appear when nuget.org audit data is unreachable; do not treat unchanged NU1900 warnings as implementation failures.

- [ ] **Step 6: Review diff scope**

Run:

```powershell
git status --short
git diff --stat HEAD
git diff --name-only HEAD
```

Expected changed files are limited to final verification fixes in:

```text
src/LightRAGNet.Share/Models/RagQueryDataResponse.cs
src/LightRAGNet.Server/Controllers/RagQueryController.cs
src/LightRAGNet.Server/Services/RagQueryRequestMapper.cs
src/LightRAGNet.Web/ApiClient.cs
src/LightRAGNet.Web/Models/ChatMessageModel.cs
src/LightRAGNet.Web/Models/ChatQuerySettingsModel.cs
src/LightRAGNet.Web/Components/Pages/RagQueryDataDialog.razor
src/LightRAGNet.Web/Components/Pages/RagChat.razor
tests/LightRAGNet.Tests/Web/RagQueryContractSourceTests.cs
tests/LightRAGNet.Server.Tests/RagQueryControllerSourceTests.cs
tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs
tests/LightRAGNet.Web.Tests/ApiClientQueryRagTests.cs
tests/LightRAGNet.Web.Tests/ChatMessageModelTests.cs
tests/LightRAGNet.Web.Tests/ChatQuerySettingsModelTests.cs
tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs
```

No GraphView, SigmaGraph, KnowledgeGraphMerge, React graph workbench, or `/api/graph/*` files should change.

- [ ] **Step 7: Commit final verification fixes if any**

If Step 1-6 required additional fixes after Task 6, run:

```powershell
git add src/LightRAGNet.Share/Models/RagQueryDataResponse.cs src/LightRAGNet.Server/Controllers/RagQueryController.cs src/LightRAGNet.Server/Services/RagQueryRequestMapper.cs src/LightRAGNet.Web/ApiClient.cs src/LightRAGNet.Web/Models/ChatMessageModel.cs src/LightRAGNet.Web/Models/ChatQuerySettingsModel.cs src/LightRAGNet.Web/Components/Pages/RagQueryDataDialog.razor src/LightRAGNet.Web/Components/Pages/RagChat.razor tests/LightRAGNet.Tests/Web/RagQueryContractSourceTests.cs tests/LightRAGNet.Server.Tests/RagQueryControllerSourceTests.cs tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs tests/LightRAGNet.Web.Tests/ApiClientQueryRagTests.cs tests/LightRAGNet.Web.Tests/ChatMessageModelTests.cs tests/LightRAGNet.Web.Tests/ChatQuerySettingsModelTests.cs tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs
git commit -m "fix: stabilize query data debug panel"
```

Expected: commit succeeds when there were final fixes. If no final fixes were needed, skip this commit.

- [ ] **Step 8: Run asset completion gate before final handoff**

Run:

```powershell
$env:PYTHONIOENCODING='utf-8'; python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "query-data-debug-panel" --json
```

Expected: command completes. If it reports missing archive coverage for completed spec+plan, create or update the archive before final close-out using repository asset-compounding guidance.

## Self-Review

- Spec coverage:
  - Message-level button is covered by Task 6.
  - On-demand JSON endpoint is covered by Task 2.
  - Request snapshot and deep copy are covered by Task 3.
  - Dialog grouped display plus raw JSON is covered by Task 5.
  - ApiClient method is covered by Task 4.
  - Conflict avoidance is covered by Task 7 diff scope.
- Placeholder scan:
  - No unresolved marker text is present.
  - Every implementation step names exact files and code.
- Type consistency:
  - `RagQueryDataResponse` is defined in Task 1 and used by server and Web tasks.
  - `ForceRetrievalDataRequest` is defined in Task 2 and referenced by controller tests.
  - `RetrievalDataRequest`, `RetrievalData`, `IsLoadingRetrievalData`, and `RetrievalDataError` are defined in Task 3 and used by Task 6.
  - `GetRagQueryDataAsync` is defined in Task 4 and used by Task 6.
