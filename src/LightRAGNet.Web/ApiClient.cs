using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using LightRAGNet.Share.Models;
using LightRAGNet.Web.Models;

namespace LightRAGNet.Web;

/// <summary>
/// API client that encapsulates all interactions with backend API
/// </summary>
public class ApiClient(HttpClient httpClient)
{
    /// <summary>
    /// Get Markdown document list (paged)
    /// </summary>
    /// <param name="page">Page number (starts from 1)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paged document list</returns>
    public async Task<PagedResult<MarkdownDocumentDto>?> GetMarkdownDocumentsAsync(
        int page = 1, 
        int pageSize = 10, 
        CancellationToken cancellationToken = default,
        string? status = null,
        string? trackId = null)
    {
        var queryParameters = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrWhiteSpace(status))
        {
            queryParameters.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (!string.IsNullOrWhiteSpace(trackId))
        {
            queryParameters.Add($"trackId={Uri.EscapeDataString(trackId)}");
        }

        var url = $"api/MarkdownDocuments?{string.Join("&", queryParameters)}";
        return await httpClient.GetFromJsonAsync<PagedResult<MarkdownDocumentDto>>(url, cancellationToken);
    }

    /// <summary>
    /// Get total count of Markdown documents
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Total document count</returns>
    public async Task<int> GetMarkdownDocumentsCountAsync(CancellationToken cancellationToken = default)
    {
        var url = "api/MarkdownDocuments/count";
        var response = await httpClient.GetAsync(url, cancellationToken);
        
        if (response.IsSuccessStatusCode)
        {
            var count = await response.Content.ReadFromJsonAsync<int>(cancellationToken: cancellationToken);
            return count;
        }
        
        return 0;
    }

    /// <summary>
    /// Get Markdown document details by ID
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Document details</returns>
    public async Task<MarkdownDocumentDto?> GetMarkdownDocumentAsync(
        int id, 
        CancellationToken cancellationToken = default)
    {
        var url = $"api/MarkdownDocuments/{id}";
        return await httpClient.GetFromJsonAsync<MarkdownDocumentDto>(url, cancellationToken);
    }

    /// <summary>
    /// Upload a source document.
    /// </summary>
    /// <param name="file">File to upload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Upload result, containing document information and error information</returns>
    public async Task<UploadResult> UploadDocumentAsync(
        IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        return await UploadDocumentsAsync([file], cancellationToken);
    }

    /// <summary>
    /// Upload source documents as a single batch.
    /// </summary>
    /// <param name="files">Files to upload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Upload result, containing document information and error information</returns>
    public async Task<UploadResult> UploadDocumentsAsync(
        IReadOnlyList<IBrowserFile> files,
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();

        if (files.Count == 0)
        {
            return new UploadResult
            {
                Success = false,
                ErrorMessage = "No files selected"
            };
        }

        foreach (var file in files)
        {
            var contentType = GetUploadContentType(file.Name);
            if (contentType is null)
            {
                return new UploadResult
                {
                    Success = false,
                    ErrorMessage = $"Unsupported file type: {file.Name}. Only Markdown, PDF, or DOCX files are supported."
                };
            }

            var fileStream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024, cancellationToken: cancellationToken); // 10MB
            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "files", file.Name);
        }

        var response = await httpClient.PostAsync("api/MarkdownDocuments/upload", content, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var submission = await response.Content.ReadFromJsonAsync<DocumentSubmissionResponse>(
                cancellationToken: cancellationToken);

            return new UploadResult
            {
                Success = true,
                Document = submission?.Documents.FirstOrDefault(),
                Documents = submission?.Documents ?? [],
                TrackId = submission?.TrackId
            };
        }

        var errorMessage = await ReadErrorMessageAsync(response.Content, cancellationToken);
        return new UploadResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }

    private static string? GetUploadContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".md" or ".markdown" => "text/markdown",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => null
        };
    }

    /// <summary>
    /// Delete Markdown document
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <param name="deleteLlmCache">Whether to delete related LLM cache entries</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deletion result</returns>
    public async Task<MarkdownDocumentDeleteClientResult> DeleteMarkdownDocumentAsync(
        int id,
        bool deleteLlmCache = false,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/MarkdownDocuments/{id}?deleteLlmCache={deleteLlmCache.ToString().ToLowerInvariant()}";
        var response = await httpClient.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return new MarkdownDocumentDeleteClientResult
            {
                Succeeded = true,
                DeletedImmediately = true
            };
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            var body = await response.Content.ReadFromJsonAsync<MarkdownDocumentDeleteResult>(
                cancellationToken: cancellationToken);

            return new MarkdownDocumentDeleteClientResult
            {
                Succeeded = true,
                Accepted = true,
                TaskId = body?.TaskId
            };
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return new MarkdownDocumentDeleteClientResult
            {
                Conflict = true,
                ErrorMessage = await ReadErrorMessageAsync(response.Content, cancellationToken)
            };
        }

        return new MarkdownDocumentDeleteClientResult
        {
            ErrorMessage = await ReadErrorMessageAsync(response.Content, cancellationToken)
        };
    }

    public async Task<DocumentPipelineActionResult?> RetryDocumentAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"api/MarkdownDocuments/{id}/retry", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<DocumentPipelineActionResult>(
            cancellationToken: cancellationToken);
    }

    public async Task<DocumentPipelineActionResult?> CancelDocumentPipelineAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"api/MarkdownDocuments/{id}/cancel", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<DocumentPipelineActionResult>(
            cancellationToken: cancellationToken);
    }

    private static async Task<string> ReadErrorMessageAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var body = await content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            return "Request failed";

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString() ?? body;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetJsonString(root, "message", out var message))
                    return message;

                if (TryGetJsonString(root, "error", out var error))
                    return error;

                if (TryGetJsonString(root, "title", out var title))
                    return title;
            }
        }
        catch (JsonException)
        {
            // Fall back to the raw response body.
        }

        return body;
    }

    private static bool TryGetJsonString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Add document to RAG system
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated document information</returns>
    public async Task<MarkdownDocumentDto?> AddToRagSystemAsync(
        int id, 
        CancellationToken cancellationToken = default)
    {
        var url = $"api/MarkdownDocuments/{id}/add-to-rag";
        var response = await httpClient.PostAsync(url, null, cancellationToken);
        
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<MarkdownDocumentDto>(cancellationToken: cancellationToken);
        }
        
        return null;
    }

    /// <summary>
    /// Clear all data (including documents, tasks, etc.)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Whether successful</returns>
    public async Task<bool> ClearAllDataAsync(CancellationToken cancellationToken = default)
    {
        const string url = "api/MarkdownDocuments/clear-all";
        var response = await httpClient.PostAsync(url, null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Query RAG system with streaming response
    /// </summary>
    /// <param name="query">User query</param>
    /// <param name="onChunkReceived">Callback when a chunk is received</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Whether the query was successful</returns>
    public Task QueryRagAsync(string query,
        Func<string, Task> onChunkReceived,
        CancellationToken cancellationToken = default)
    {
        return QueryRagAsync(new RagQueryRequest { Query = query }, new RagQueryStreamHandlers { OnChunkReceived = onChunkReceived }, cancellationToken);
    }

    public async Task QueryRagAsync(RagQueryRequest request, RagQueryStreamHandlers handlers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handlers);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "api/RagQuery/query");
        requestMessage.Content = JsonContent.Create(request);

        using var response = await httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var items = SseParser.Create(responseStream, ItemParser).EnumerateAsync(cancellationToken);

        await foreach (var sseItem in items.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sseItem.Data is TextChunkEvent textEvent)
            {
                if (!string.IsNullOrEmpty(textEvent.Chunk))
                {
                    await handlers.OnChunkReceived(textEvent.Chunk);
                }
            }
            else if (sseItem.Data is QueryMetadataEvent metadataEvent)
            {
                await handlers.OnMetadataReceived(metadataEvent);
            }
            else if (sseItem.Data is ErrorEvent errorEvent)
            {
                throw new RagQueryException(errorEvent.Error, errorEvent.Message);
            }
            else if (sseItem.Data is DoneEvent)
            {
                return;
            }
        }
    }

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

    private static RagQueryEvent ItemParser(string type, ReadOnlySpan<byte> data)
    {
        return JsonSerializer.Deserialize<RagQueryEvent>(data) ??
               throw new InvalidOperationException("Failed to deserialize SSE item.");
    }

    /// <summary>
    /// Get API base address
    /// </summary>
    /// <returns>API base address string</returns>
    public string GetBaseAddress()
    {
        return httpClient.BaseAddress?.ToString() ?? "http://localhost:5261";
    }

    /// <summary>
    /// Get knowledge graph data for visualization
    /// </summary>
    /// <param name="nodeLabel">Node label filter (use "*" for all nodes)</param>
    /// <param name="maxDepth">Maximum depth for graph traversal</param>
    /// <param name="maxNodes">Maximum number of nodes to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Graph view data</returns>
    public async Task<GraphViewDto?> GetGraphViewAsync(
        string? nodeLabel = "*",
        int maxDepth = 2,
        int maxNodes = 100,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/GraphView?nodeLabel={Uri.EscapeDataString(nodeLabel ?? "*")}&maxDepth={maxDepth}&maxNodes={maxNodes}";
        return await httpClient.GetFromJsonAsync<GraphViewDto>(url, cancellationToken);
    }
}

/// <summary>
/// Upload result
/// </summary>
public class UploadResult
{
    public bool Success { get; set; }
    public MarkdownDocumentDto? Document { get; set; }
    public IReadOnlyList<MarkdownDocumentDto> Documents { get; set; } = [];
    public string? TrackId { get; set; }
    public string? ErrorMessage { get; set; }
}
