using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Extensions;
using LightRAGNet.Server.Hubs;
using LightRAGNet.Server.Models;
using LightRAGNet.Services.TaskQueue;
using LightRAGNet.Share.Models;
using LightRAGNet.Storage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Neo4j.Driver;
using Qdrant.Client;

namespace LightRAGNet.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MarkdownDocumentsController(
    AppDbContext context,
    ILogger<MarkdownDocumentsController> logger,
    IRagTaskQueueService taskQueueService,
    IServiceProvider serviceProvider)
    : ControllerBase
{
    /// <summary>
    /// Get upload file directory path (based on application runtime path)
    /// </summary>
    private string GetUploadsPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var uploadsPath = Path.Combine(baseDir, "Uploads");

        // Ensure directory exists
        Directory.CreateDirectory(uploadsPath);
        return uploadsPath;
    }

    /// <summary>
    /// Get Markdown document list (paged)
    /// </summary>
    /// <param name="page">Page number (starts from 1, default 1)</param>
    /// <param name="pageSize">Number of items per page (default 10, max 100)</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Paged document list</returns>
    /// <response code="200">Successfully returns document list</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<MarkdownDocumentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MarkdownDocumentDto>>> GetMarkdownDocuments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        // Limit maximum items per page
        if (pageSize > 100)
            pageSize = 100;
        if (pageSize < 1)
            pageSize = 10;
        if (page < 1)
            page = 1;

        var totalCount = await context.MarkdownDocuments.CountAsync(cancellationToken: cancellationToken);

        // Sorting rule: documents being processed (Processing, Pending) come first, then failed status, finally other statuses sorted by upload time descending
        var documents = await context.MarkdownDocuments
            .OrderBy(d => d.RagStatus == "Processing" ? 0 : 
                         d.RagStatus == "Pending" ? 1 : 
                         d.RagStatus == "Failed" ? 2 : 3) // Priority: Processing < Pending < Failed < Others
            .ThenByDescending(d => d.UploadTime) // Same priority sorted by upload time descending
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => d.ToDto())
            .ToListAsync(cancellationToken: cancellationToken);

        // Batch query task status to improve performance
        var pendingOrProcessingDocuments = documents
            .Where(d => d.RagStatus is "Pending" or "Processing")
            .ToList();
        
        if (pendingOrProcessingDocuments.Count > 0)
        {
            try
            {
                var documentIds = pendingOrProcessingDocuments.Select(d => d.Id).ToList();
                var tasks = await taskQueueService.GetTasksByDocumentIdsAsync(documentIds, cancellationToken);
                
                // Update task status information for documents
                foreach (var document in pendingOrProcessingDocuments)
                {
                    if (tasks.TryGetValue(document.Id, out var task))
                    {
                        document.RagStatus = task.Status.ToString();
                        document.RagCurrentStage = task.CurrentStage?.ToString();
                        document.RagProgress = task.Progress;
                        
                        // If task is completed, update related status
                        if (task.Status == RagTaskStatus.Completed)
                        {
                            document.IsInRagSystem = true;
                            document.RagAddedTime = task.CompletedAt ?? DateTime.UtcNow;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Request was cancelled (e.g., client disconnected), handle silently without logging warning
                // Document keeps original status
            }
            catch (Exception ex)
            {
                // Task status query failure does not affect document list display, only log
                logger.LogWarning(ex, "Failed to batch query task status");
            }
        }

        var result = new PagedResult<MarkdownDocumentDto>
        {
            Items = documents,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };

        return Ok(result);
    }

    /// <summary>
    /// Get total count of Markdown documents
    /// </summary>
    /// <returns>Total document count</returns>
    /// <response code="200">Successfully returns total document count</response>
    [HttpGet("count")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> GetMarkdownDocumentsCount()
    {
        var totalCount = await context.MarkdownDocuments.CountAsync();
        return Ok(totalCount);
    }

    /// <summary>
    /// Get Markdown document details by ID
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <returns>Document details (including content)</returns>
    /// <response code="200">Successfully returns document details</response>
    /// <response code="404">Document not found</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(MarkdownDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MarkdownDocumentDto>> GetMarkdownDocument(int id)
    {
        var document = await context.MarkdownDocuments.FindAsync(id);

        if (document == null)
        {
            return NotFound();
        }

        return Ok(document.ToDto());
    }

    /// <summary>
    /// Upload Markdown document
    /// </summary>
    /// <param name="file">Markdown file (.md or .markdown, max 10MB)</param>
    /// <returns>Uploaded document information</returns>
    /// <response code="201">Document uploaded successfully</response>
    /// <response code="400">Invalid file format or file too large</response>
    /// <response code="500">Server error</response>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MarkdownDocumentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MarkdownDocumentDto>> UploadMarkdownDocument([FromForm] IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("File cannot be empty");
        }

        // Validate file extension
        var allowedExtensions = new[] { ".md", ".markdown" };
        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(fileExtension))
        {
            return BadRequest("Only .md or .markdown files are supported");
        }

        // Validate file size (e.g., max 10MB)
        const long maxFileSize = 10 * 1024 * 1024; // 10MB
        if (file.Length > maxFileSize)
        {
            return BadRequest("File size cannot exceed 10MB");
        }

        try
        {
            // Calculate file hash value (SHA256)
            byte[] fileBytes;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }

            var hashBytes = SHA256.HashData(fileBytes);
            var fileHash = Convert.ToHexStringLower(hashBytes);

            // Check if original file name already exists
            var existingDocumentByName = await context.MarkdownDocuments
                .FirstOrDefaultAsync(d => d.FileName == file.FileName);

            if (existingDocumentByName != null)
            {
                return BadRequest($"File name already exists, duplicate upload not allowed. Existing file: {existingDocumentByName.FileName} (ID: {existingDocumentByName.Id})");
            }

            // Check if file already exists (by hash value)
            var existingDocumentByHash = await context.MarkdownDocuments
                .FirstOrDefaultAsync(d => d.FileHash == fileHash);

            if (existingDocumentByHash != null)
            {
                return BadRequest($"File already exists, duplicate upload not allowed. Existing file: {existingDocumentByHash.FileName} (ID: {existingDocumentByHash.Id})");
            }

            // Use extension method to detect file encoding and decode content
            var encodingResult = fileBytes.DetectEncodingAndDecode();
            var content = encodingResult.Content;
            var detectedEncoding = encodingResult.DetectedEncoding;

            logger.LogInformation("Detected file encoding: {Encoding} (Charset: {Charset}, Confidence: {Confidence})",
                detectedEncoding.EncodingName,
                encodingResult.Charset ?? "Not detected",
                encodingResult.Charset != null ? encodingResult.Confidence.ToString("P2") : "N/A");

            // Save file to file system
            var uploadsFolder = GetUploadsPath();

            // Generate unique file name (original filename_timestamp_hash.extension)
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var safeFileName = Path.GetFileNameWithoutExtension(file.FileName)
                .Replace(" ", "_")
                .Replace("(", "")
                .Replace(")", "");
            var extension = Path.GetExtension(file.FileName);
            // Use first 8 characters of hash as part of file name
            var hashPrefix = fileHash.Substring(0, Math.Min(8, fileHash.Length));
            var uniqueFileName = $"{safeFileName}_{timestamp}_{hashPrefix}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            // Save file (use UTF-8 encoding with BOM for compatibility)
            await System.IO.File.WriteAllTextAsync(filePath, content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            // Generate file URL with full HTTP address
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var fileUrl = $"{baseUrl}/uploads/{uniqueFileName}";

            var document = new MarkdownDocument
            {
                FileName = file.FileName,
                Content = content,
                FileSize = file.Length,
                UploadTime = DateTime.UtcNow,
                FileUrl = fileUrl,
                FileHash = fileHash
            };

            context.MarkdownDocuments.Add(document);
            await context.SaveChangesAsync();

            logger.LogInformation("Uploaded Markdown document: {FileName}, ID: {Id}, FileUrl: {FileUrl}, FileHash: {FileHash}",
                document.FileName, document.Id, document.FileUrl, document.FileHash);

            // No longer automatically add to RAG queue, user needs to manually click "Add to RAG" button
            // Document initial status is not added to RAG system
            document.RagStatus = null;
            document.IsInRagSystem = false;

            var dto = document.ToDto();
            return CreatedAtAction(nameof(GetMarkdownDocument), new { id = document.Id }, dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while uploading Markdown document");
            return StatusCode(500, "Error occurred while uploading file");
        }
    }

    /// <summary>
    /// Delete Markdown document
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <returns>No content</returns>
    /// <response code="204">Delete successful</response>
    /// <response code="404">Document not found</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteMarkdownDocument(int id)
    {
        var document = await context.MarkdownDocuments.FindAsync(id);
        if (document == null)
        {
            return NotFound();
        }

        // Prevent deletion of documents that have completed RAG insertion
        if (document.IsInRagSystem)
        {
            return BadRequest(new { error = "Cannot delete document", message = "Documents that have completed RAG insertion cannot be deleted." });
        }

        // Delete file from file system
        if (!string.IsNullOrEmpty(document.FileUrl))
        {
            try
            {
                // FileUrl format is /uploads/{filename}, need to convert to actual file path
                var fileName = document.FileUrl.Replace("/uploads/", "").TrimStart('/');
                var uploadsFolder = GetUploadsPath();
                var filePath = Path.Combine(uploadsFolder, fileName);

                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                    logger.LogInformation("Deleted file: {FilePath}", filePath);
                }
                else
                {
                    logger.LogWarning("File does not exist, skipping deletion: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while deleting file: {FileUrl}", document.FileUrl);
                // Continue to delete database record even if file deletion fails
            }
        }

        // Delete database record
        context.MarkdownDocuments.Remove(document);
        await context.SaveChangesAsync();

        logger.LogInformation("Deleted Markdown document: {FileName}, ID: {Id}", document.FileName, document.Id);

        return NoContent();
    }

    /// <summary>
    /// Add document to RAG system
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <returns>Updated document information</returns>
    /// <response code="200">Successfully added to RAG processing queue</response>
    /// <response code="404">Document not found</response>
    /// <response code="400">Document is already being processed or has been added to RAG system</response>
    [HttpPost("{id:int}/add-to-rag")]
    [ProducesResponseType(typeof(MarkdownDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MarkdownDocumentDto>> AddToRagSystem(int id)
    {
        var document = await context.MarkdownDocuments.FindAsync(id);
        if (document == null)
        {
            return NotFound(new { error = "Document not found", id });
        }

        // Check if document is already in RAG system
        if (document.IsInRagSystem)
        {
            return BadRequest(new { error = "Document already added to RAG system", message = "This document has already been added to the RAG system, no need to add again." });
        }

        // Check if document is currently being processed
        if (document.RagStatus == "Processing" || document.RagStatus == "Pending")
        {
            return BadRequest(new { error = "Document is being processed", message = "This document is currently in the RAG processing queue, please wait for processing to complete." });
        }

        try
        {
            // Ensure FileUrl is a full URL (handle legacy relative paths)
            var fileUrl = document.FileUrl ?? string.Empty;
            if (!string.IsNullOrEmpty(fileUrl) && !fileUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !fileUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Convert relative path to full URL
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                fileUrl = fileUrl.StartsWith("/") ? $"{baseUrl}{fileUrl}" : $"{baseUrl}/{fileUrl}";
            }

            // Create task and add to queue
            var taskId = await taskQueueService.EnqueueTaskAsync(
                document.Id,
                document.Content,
                fileUrl,
                cancellationToken: HttpContext.RequestAborted);

            logger.LogInformation("Document added to RAG processing queue: DocumentId={DocumentId}, TaskId={TaskId}",
                document.Id, taskId);

            // Update document status to Pending
            document.RagStatus = "Pending";
            await context.SaveChangesAsync();

            var dto = document.ToDto();
            return Ok(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create RAG task: DocumentId={DocumentId}", document.Id);
            return StatusCode(500, new { error = "Failed to add to RAG system", message = ex.Message });
        }
    }

    /// <summary>
    /// Clear all data (including documents, tasks, RAG storage content, etc.)
    /// </summary>
    /// <returns>Operation result</returns>
    /// <response code="200">Clear successful</response>
    [HttpPost("clear-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> ClearAllData()
    {
        try
        {
            var results = new List<string>();

            // 1. Delete all documents (including files in file system)
            var documents = await context.MarkdownDocuments.ToListAsync();
            var documentCount = documents.Count;
            foreach (var document in documents)
            {
                // Delete file from file system
                if (!string.IsNullOrEmpty(document.FileUrl))
                {
                    try
                    {
                        var fileName = document.FileUrl.Replace("/uploads/", "").TrimStart('/');
                        var uploadsFolder = GetUploadsPath();
                        var filePath = Path.Combine(uploadsFolder, fileName);

                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                            logger.LogInformation("Deleted file: {FilePath}", filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error occurred while deleting file: {FileUrl}", document.FileUrl);
                    }
                }
            }

            // 2. Delete all database records
            context.MarkdownDocuments.RemoveRange(documents);
            await context.SaveChangesAsync();
            results.Add($"Deleted {documentCount} documents");

            // 2.1 Delete all files in Uploads folder (including possible orphaned files)
            try
            {
                var uploadsFolder = GetUploadsPath();
                if (Directory.Exists(uploadsFolder))
                {
                    var files = Directory.GetFiles(uploadsFolder);
                    int deletedFileCount = 0;
                    foreach (var file in files)
                    {
                        try
                        {
                            System.IO.File.Delete(file);
                            deletedFileCount++;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to delete file: {FilePath}", file);
                        }
                    }

                    if (deletedFileCount > 0)
                    {
                        results.Add($"Deleted {deletedFileCount} files from Uploads folder");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error occurred while clearing Uploads folder: {Error}", ex.Message);
            }

            // 3. Stop all tasks being processed
            var stoppedCount = await taskQueueService.StopAllTasksAsync();
            if (stoppedCount > 0)
            {
                results.Add($"Stopped {stoppedCount} tasks being processed");
            }

            // 4. Clear all tasks
            await taskQueueService.ClearAllTasksAsync();
            results.Add("Cleared all RAG tasks");

            // 5. Clear RAG storage content
            // 5.1 Clear KV stores
            try
            {
                var kvStoreNames = KVContracts.GetKVStoreNames().ToList();
                int kvStoreCount = 0;
                foreach (var storeName in kvStoreNames)
                {
                    try
                    {
                        var kvStore = serviceProvider.GetRequiredKeyedService<IKVStore>(storeName);
                        await kvStore.DropAsync();
                        kvStoreCount++;
                        logger.LogInformation("Cleared KV store: {StoreName}", storeName);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to clear KV store: {StoreName}", ex.Message);
                    }
                }

                if (kvStoreCount > 0)
                {
                    results.Add($"Cleared {kvStoreCount} KV stores");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error occurred while clearing KV stores: {Error}", ex.Message);
            }

            // 5.2 Clear local JSON files
            try
            {
                var lightragOptions = serviceProvider.GetRequiredService<IOptions<LightRAGOptions>>().Value;
                var workingDir = lightragOptions.WorkingDir;
                // If relative path, convert to absolute path based on application runtime path
                if (!Path.IsPathRooted(workingDir))
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    workingDir = Path.Combine(baseDir, workingDir);
                }

                var jsonFiles = new[]
                {
                    Path.Combine(workingDir, "text_chunks.json"),
                    Path.Combine(workingDir, "full_docs.json"),
                    Path.Combine(workingDir, "full_entities.json"),
                    Path.Combine(workingDir, "full_relations.json"),
                    Path.Combine(workingDir, "entity_chunks.json"),
                    Path.Combine(workingDir, "relation_chunks.json"),
                    Path.Combine(workingDir, "llm_cache.json"),
                    Path.Combine(workingDir, "tasks.json")
                };

                int deletedFileCount = 0;
                foreach (var jsonFile in jsonFiles)
                {
                    if (System.IO.File.Exists(jsonFile))
                    {
                        System.IO.File.Delete(jsonFile);
                        deletedFileCount++;
                        logger.LogInformation("Deleted file: {File}", jsonFile);
                    }
                }

                if (deletedFileCount > 0)
                {
                    results.Add($"Deleted {deletedFileCount} JSON files");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error occurred while clearing JSON files: {Error}", ex.Message);
            }

            // 5.3 Clear Qdrant collections
            try
            {
                var qdrantClient = serviceProvider.GetRequiredService<QdrantClient>();
                var collections = await qdrantClient.ListCollectionsAsync();
                var lightragCollections = collections.Where(c => c.StartsWith("lightrag_vdb_dotnet_")).ToList();

                int deletedCollectionCount = 0;
                foreach (var collection in lightragCollections)
                {
                    try
                    {
                        await qdrantClient.DeleteCollectionAsync(collection);
                        deletedCollectionCount++;
                        logger.LogInformation("Deleted Qdrant collection: {Collection}", collection);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to delete Qdrant collection: {Collection}, {Error}", collection, ex.Message);
                    }
                }

                if (deletedCollectionCount > 0)
                {
                    results.Add($"Deleted {deletedCollectionCount} Qdrant collections");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error occurred while clearing Qdrant collections: {Error}", ex.Message);
            }

            // 4.4 Clear Neo4j data
            try
            {
                var neo4JDriver = serviceProvider.GetRequiredService<IDriver>();
                await using var session = neo4JDriver.AsyncSession();
                var deleteResult = await session.RunAsync("MATCH (n) DETACH DELETE n RETURN count(n) as deleted");
                var record = await deleteResult.SingleAsync();
                var deletedCount = record["deleted"].As<int>();
                if (deletedCount > 0)
                {
                    results.Add($"Deleted {deletedCount} Neo4j nodes");
                }

                logger.LogInformation("Deleted Neo4j node count: {Count}", deletedCount);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error occurred while clearing Neo4j data: {Error}", ex.Message);
            }

            logger.LogInformation("Cleared all data: {Results}", string.Join(", ", results));

            // Notify frontend to refresh page via SignalR
            try
            {
                var hubContext = HttpContext.RequestServices.GetRequiredService<IHubContext<RagTaskHub>>();
                await hubContext.Clients.All.SendAsync("DataCleared", cancellationToken: CancellationToken.None);
                logger.LogInformation("Pushed data cleared event to frontend");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to push data cleared event, but does not affect clear operation");
            }

            return Ok(new
            {
                success = true,
                message = "All data cleared",
                details = results
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while clearing all data");
            return StatusCode(500, new { error = "Failed to clear data", message = ex.Message });
        }
    }
}