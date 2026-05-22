# ManagedCode MarkItDown Document Intake Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add offline PDF and DOCX upload support where upload only stores the original file, and the existing `Add to RAG` action triggers local Markdown conversion plus existing RAG indexing.

**Architecture:** Upload persists source artifacts and creates inactive document rows. `POST /api/MarkdownDocuments/{id}/add-to-rag` branches by document type: Markdown/text rows keep the current direct RAG enqueue behavior, while PDF/DOCX rows enter a Server-side conversion queue. A hosted conversion worker creates `converted.md` through `ManagedCode.MarkItDown`, stores conversion metadata, then enqueues the existing `IRagTaskQueueService` with the converted Markdown.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core SQLite, Blazor Server, xUnit, FluentAssertions, `ManagedCode.MarkItDown` 10.0.7.

---

## Read First

- Spec: `docs/superpowers/specs/2026-05-22-markitdown-document-intake-design.md`
- Existing upload/intake service: `src/LightRAGNet.Server/Services/DocumentIntakeService.cs`
- Existing single-file Markdown upload and `Add to RAG`: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
- Existing status constants: `src/LightRAGNet.Server/Services/DocumentIntakeStatus.cs`
- Existing document model: `src/LightRAGNet.Server/Models/MarkdownDocument.cs`
- Existing DTO mapper: `src/LightRAGNet.Server/Extensions/MarkdownModelMapper.cs`
- Existing server tests and queue doubles: `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`

## Product Rules

These rules are requirements, not suggestions:

- Uploading `合同.pdf` or `说明书.docx` must show that original file name in the document list.
- Upload must not call `IRagTaskQueueService.EnqueueTaskAsync`.
- Upload must not run document conversion.
- Upload must set `RagStatus = null` so the existing Web document list still shows the `Add to RAG` button.
- `Add to RAG` is the only trigger for PDF/DOCX conversion.
- `converted.md` is an internal artifact and must not replace `MarkdownDocument.FileName`.

## Status Contract

Create `src/LightRAGNet.Server/Services/DocumentConversionStatus.cs`:

```csharp
namespace LightRAGNet.Server.Services;

public static class DocumentConversionStatus
{
    public const string NotStarted = "NotStarted";
    public const string NotRequired = "NotRequired";
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
```

Use these exact transitions:

| Moment | RagStatus | RagCurrentStage | ConversionStatus | ActiveRagTaskId |
| --- | --- | --- | --- | --- |
| PDF/DOCX upload | `null` | `null` | `NotStarted` | `null` |
| PDF/DOCX Add to RAG | `Queued` | `Accepted` | `Queued` | `null` |
| Conversion worker claim | `Processing` | `Converting` | `Processing` | `null` |
| Converted and RAG task queued | `Queued` | `Indexing` | `Completed` | task id |
| Conversion failed | `Failed` | `Converting` | `Failed` | `null` |
| Converted but RAG queue rejected | `Failed` | `Indexing` | `Completed` | `null` |

## Post-Conversion Handoff Contract

After a PDF/DOCX document is converted, the processor must immediately hand the converted Markdown to the existing RAG queue:

```text
converted.md saved
  -> read/use converted Markdown content
  -> IRagTaskQueueService.EnqueueTaskAsync(document.Id, markdown, source, token)
  -> existing RagTaskProcessorService handles chunking/extraction/merge/indexing
```

Do not leave converted documents idle after `converted.md` is written. Also do not mark indexing enqueue failures as conversion failures:

```text
Converter fails or returns empty Markdown:
  ConversionStatus = Failed
  RagCurrentStage = Converting

Converter succeeds but RAG queue rejects or throws:
  ConversionStatus = Completed
  RagCurrentStage = Indexing
  RagStatus = Failed
  RagErrorMessage = Document could not be queued for indexing.
```

---

### Task 1: Schema and DTO Metadata

**Files:**
- Create: `src/LightRAGNet.Server/Services/DocumentConversionStatus.cs`
- Modify: `Directory.Packages.props`
- Modify: `src/LightRAGNet.Server/LightRAGNet.Server.csproj`
- Modify: `src/LightRAGNet.Server/Models/MarkdownDocument.cs`
- Modify: `src/LightRAGNet.Server/Data/AppDbContext.cs`
- Modify: `src/LightRAGNet.Share/Models/MarkdownDocumentDto.cs`
- Modify: `src/LightRAGNet.Server/Extensions/MarkdownModelMapper.cs`
- Create: `src/LightRAGNet.Server/Migrations/<timestamp>_AddDocumentConversionArtifacts.cs`
- Modify: `src/LightRAGNet.Server/Migrations/AppDbContextModelSnapshot.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`

- [ ] **Step 1: Write the failing DTO metadata test**

Add this test after `GetMarkdownDocuments_WhenStatusAndTrackExist_ReturnsPipelineMetadata`:

```csharp
[Fact]
public async Task GetMarkdownDocuments_ReturnsSafeConversionMetadataWithoutLocalPaths()
{
    using var factory = new LightRagServerFactory();
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 801,
        FileName = "合同.pdf",
        Content = "# Converted",
        FileSize = 128,
        TrackId = "track-conversion-metadata",
        OriginalFileName = "合同.pdf",
        OriginalFilePath = "documents/801/original.pdf",
        OriginalContentType = "application/pdf",
        OriginalContentHash = "original-hash",
        ConvertedMarkdownPath = "documents/801/converted.md",
        ConvertedMarkdownHash = "markdown-hash",
        ConversionStatus = DocumentConversionStatus.Completed,
        ConversionErrorMessage = null,
        ConversionStartedAt = new DateTime(2026, 5, 22, 1, 2, 3, DateTimeKind.Utc),
        ConversionCompletedAt = new DateTime(2026, 5, 22, 1, 2, 8, DateTimeKind.Utc),
        ConversionTool = "ManagedCode.MarkItDown",
        ConversionToolVersion = "10.0.7"
    });
    using var client = factory.CreateClient();

    var result = await client.GetFromJsonAsync<PagedResult<MarkdownDocumentDto>>(
        "/api/MarkdownDocuments?page=1&pageSize=10&trackId=track-conversion-metadata");

    result.Should().NotBeNull();
    var document = result!.Items.Should().ContainSingle(d => d.Id == 801).Subject;
    document.FileName.Should().Be("合同.pdf");
    document.OriginalFileName.Should().Be("合同.pdf");
    document.OriginalContentType.Should().Be("application/pdf");
    document.OriginalContentHash.Should().Be("original-hash");
    document.ConvertedMarkdownHash.Should().Be("markdown-hash");
    document.ConversionStatus.Should().Be(DocumentConversionStatus.Completed);
    document.ConversionErrorMessage.Should().BeNull();
    document.ConversionTool.Should().Be("ManagedCode.MarkItDown");
    document.ConversionToolVersion.Should().Be("10.0.7");
    document.ConversionStartedAt.Should().Be(new DateTime(2026, 5, 22, 1, 2, 3, DateTimeKind.Utc));
    document.ConversionCompletedAt.Should().Be(new DateTime(2026, 5, 22, 1, 2, 8, DateTimeKind.Utc));
    typeof(MarkdownDocumentDto).GetProperty("OriginalFilePath").Should().BeNull();
    typeof(MarkdownDocumentDto).GetProperty("ConvertedMarkdownPath").Should().BeNull();
}
```

- [ ] **Step 2: Run the red test**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~GetMarkdownDocuments_ReturnsSafeConversionMetadataWithoutLocalPaths" --no-restore --verbosity minimal
```

Expected: FAIL with compile errors for missing conversion members.

- [ ] **Step 3: Add package references**

Add to `Directory.Packages.props`:

```xml
<PackageVersion Include="ManagedCode.MarkItDown" Version="10.0.7" />
```

Add to `src/LightRAGNet.Server/LightRAGNet.Server.csproj`:

```xml
<PackageReference Include="ManagedCode.MarkItDown" />
```

- [ ] **Step 4: Add conversion status constants**

Create `src/LightRAGNet.Server/Services/DocumentConversionStatus.cs` using the exact code in the Status Contract.

- [ ] **Step 5: Add model fields**

Append these properties to `src/LightRAGNet.Server/Models/MarkdownDocument.cs`:

```csharp
public string? OriginalFileName { get; set; }
public string? OriginalFilePath { get; set; }
public string? OriginalContentType { get; set; }
public string? OriginalContentHash { get; set; }
public string? ConvertedMarkdownPath { get; set; }
public string? ConvertedMarkdownHash { get; set; }
public string? ConversionStatus { get; set; }
public string? ConversionErrorMessage { get; set; }
public DateTime? ConversionStartedAt { get; set; }
public DateTime? ConversionCompletedAt { get; set; }
public string? ConversionTool { get; set; }
public string? ConversionToolVersion { get; set; }
```

- [ ] **Step 6: Add DTO fields**

Append these properties to `src/LightRAGNet.Share/Models/MarkdownDocumentDto.cs`. Do not add local filesystem path fields:

```csharp
public string? OriginalFileName { get; set; }
public string? OriginalContentType { get; set; }
public string? OriginalContentHash { get; set; }
public string? ConvertedMarkdownHash { get; set; }
public string? ConversionStatus { get; set; }
public string? ConversionErrorMessage { get; set; }
public DateTime? ConversionStartedAt { get; set; }
public DateTime? ConversionCompletedAt { get; set; }
public string? ConversionTool { get; set; }
public string? ConversionToolVersion { get; set; }
```

- [ ] **Step 7: Map DTO fields**

Add these assignments to `MarkdownModelMapper.ToDto(...)`:

```csharp
OriginalFileName = model.OriginalFileName,
OriginalContentType = model.OriginalContentType,
OriginalContentHash = model.OriginalContentHash,
ConvertedMarkdownHash = model.ConvertedMarkdownHash,
ConversionStatus = model.ConversionStatus,
ConversionErrorMessage = model.ConversionErrorMessage,
ConversionStartedAt = model.ConversionStartedAt,
ConversionCompletedAt = model.ConversionCompletedAt,
ConversionTool = model.ConversionTool,
ConversionToolVersion = model.ConversionToolVersion,
```

- [ ] **Step 8: Configure EF columns**

Add inside `AppDbContext.OnModelCreating` for `MarkdownDocument`:

```csharp
entity.Property(e => e.OriginalFileName).HasMaxLength(255);
entity.Property(e => e.OriginalFilePath).HasMaxLength(1024);
entity.Property(e => e.OriginalContentType).HasMaxLength(128);
entity.Property(e => e.OriginalContentHash).HasMaxLength(128);
entity.Property(e => e.ConvertedMarkdownPath).HasMaxLength(1024);
entity.Property(e => e.ConvertedMarkdownHash).HasMaxLength(128);
entity.Property(e => e.ConversionStatus).HasMaxLength(32);
entity.Property(e => e.ConversionErrorMessage).HasMaxLength(2048);
entity.Property(e => e.ConversionTool).HasMaxLength(128);
entity.Property(e => e.ConversionToolVersion).HasMaxLength(64);
entity.HasIndex(e => e.ConversionStatus);
```

- [ ] **Step 9: Generate migration**

```powershell
dotnet ef migrations add AddDocumentConversionArtifacts --project .\src\LightRAGNet.Server\LightRAGNet.Server.csproj --startup-project .\src\LightRAGNet.Server\LightRAGNet.Server.csproj
```

Expected: a new migration and `AppDbContextModelSnapshot.cs` update appear under `src/LightRAGNet.Server/Migrations/`.

- [ ] **Step 10: Run green test**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~GetMarkdownDocuments_ReturnsSafeConversionMetadataWithoutLocalPaths" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 11: Commit**

```powershell
git add Directory.Packages.props src\LightRAGNet.Server\LightRAGNet.Server.csproj src\LightRAGNet.Server\Services\DocumentConversionStatus.cs src\LightRAGNet.Server\Models\MarkdownDocument.cs src\LightRAGNet.Server\Data\AppDbContext.cs src\LightRAGNet.Share\Models\MarkdownDocumentDto.cs src\LightRAGNet.Server\Extensions\MarkdownModelMapper.cs src\LightRAGNet.Server\Migrations tests\LightRAGNet.Server.Tests\DocumentIntakePipelineApiTests.cs
git commit -m "feat: add document conversion metadata"
```

### Task 2: Filesystem Artifact Store

**Files:**
- Create: `src/LightRAGNet.Server/Services/DocumentArtifacts/DocumentArtifactStoreOptions.cs`
- Create: `src/LightRAGNet.Server/Services/DocumentArtifacts/DocumentArtifactWriteResult.cs`
- Create: `src/LightRAGNet.Server/Services/DocumentArtifacts/IDocumentArtifactStore.cs`
- Create: `src/LightRAGNet.Server/Services/DocumentArtifacts/FileSystemDocumentArtifactStore.cs`
- Modify: `src/LightRAGNet.Server/Program.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentArtifactStoreTests.cs`

- [ ] **Step 1: Write artifact store tests**

Create `tests/LightRAGNet.Server.Tests/DocumentArtifactStoreTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services.DocumentArtifacts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests;

public sealed class DocumentArtifactStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "LightRAGNet.Artifacts.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveOriginalAsync_WritesOriginalFileUnderDocumentDirectory()
    {
        var store = CreateStore();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("pdf bytes"));

        var result = await store.SaveOriginalAsync(42, stream, "合同.pdf", CancellationToken.None);

        result.RelativePath.Should().Be(Path.Combine("documents", "42", "original.pdf"));
        result.AbsolutePath.Should().StartWith(root);
        File.Exists(result.AbsolutePath).Should().BeTrue();
        result.Hash.Should().Be("d1cb546b102fab8362de413fdacc187b05be10df72b72db3b3e50b4953f6a555");
        result.Size.Should().Be(9);
    }

    [Fact]
    public async Task SaveConvertedMarkdownAsync_WritesConvertedMarkdownAndReadsItBack()
    {
        var store = CreateStore();

        var result = await store.SaveConvertedMarkdownAsync(7, "# Title\n\nBody", CancellationToken.None);
        var markdown = await store.ReadConvertedMarkdownAsync(result.RelativePath, CancellationToken.None);

        result.RelativePath.Should().Be(Path.Combine("documents", "7", "converted.md"));
        markdown.Should().Be("# Title\n\nBody");
        result.Hash.Should().Be("b7b510d34e84878ec3d4d2bdc287f223faef14f09c59bd3b51597c88a3d260c7");
    }

    [Fact]
    public async Task GetFileInfo_WhenPathEscapesRoot_Throws()
    {
        var store = CreateStore();

        var act = () => store.GetFileInfo(Path.Combine("documents", "..", "..", "secret.pdf"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Document artifact path is outside the configured root.");
    }

    [Fact]
    public async Task DeleteArtifactsAsync_RemovesDocumentDirectory()
    {
        var store = CreateStore();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("docx bytes"));
        await store.SaveOriginalAsync(99, stream, "sample.docx", CancellationToken.None);
        await store.SaveConvertedMarkdownAsync(99, "converted", CancellationToken.None);

        await store.DeleteArtifactsAsync(new MarkdownDocument { Id = 99 }, CancellationToken.None);

        Directory.Exists(Path.Combine(root, "documents", "99")).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private FileSystemDocumentArtifactStore CreateStore()
    {
        return new FileSystemDocumentArtifactStore(
            Options.Create(new DocumentArtifactStoreOptions { RootPath = root }),
            NullLogger<FileSystemDocumentArtifactStore>.Instance);
    }
}
```

- [ ] **Step 2: Run red tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~DocumentArtifactStoreTests" --no-restore --verbosity minimal
```

Expected: FAIL because artifact store types do not exist.

- [ ] **Step 3: Implement store option and result**

Create `DocumentArtifactStoreOptions.cs`:

```csharp
namespace LightRAGNet.Server.Services.DocumentArtifacts;

public sealed class DocumentArtifactStoreOptions
{
    public string RootPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rag_storage");
}
```

Create `DocumentArtifactWriteResult.cs`:

```csharp
namespace LightRAGNet.Server.Services.DocumentArtifacts;

public sealed record DocumentArtifactWriteResult(
    string AbsolutePath,
    string RelativePath,
    string Hash,
    long Size);
```

- [ ] **Step 4: Implement artifact store interface**

Create `IDocumentArtifactStore.cs`:

```csharp
using LightRAGNet.Server.Models;

namespace LightRAGNet.Server.Services.DocumentArtifacts;

public interface IDocumentArtifactStore
{
    Task<DocumentArtifactWriteResult> SaveOriginalAsync(
        int documentId,
        Stream source,
        string originalFileName,
        CancellationToken cancellationToken);

    Task<DocumentArtifactWriteResult> SaveConvertedMarkdownAsync(
        int documentId,
        string markdown,
        CancellationToken cancellationToken);

    Task<string> ReadConvertedMarkdownAsync(
        string relativePath,
        CancellationToken cancellationToken);

    FileInfo GetFileInfo(string relativePath);

    bool Exists(string? relativePath);

    Task DeleteArtifactsAsync(
        MarkdownDocument document,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Implement filesystem store**

Create `FileSystemDocumentArtifactStore.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using LightRAGNet.Server.Models;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Services.DocumentArtifacts;

public sealed class FileSystemDocumentArtifactStore(
    IOptions<DocumentArtifactStoreOptions> options,
    ILogger<FileSystemDocumentArtifactStore> logger) : IDocumentArtifactStore
{
    private readonly string rootPath = Path.GetFullPath(options.Value.RootPath);

    public Task<DocumentArtifactWriteResult> SaveOriginalAsync(
        int documentId,
        Stream source,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (extension is not ".pdf" and not ".docx")
        {
            throw new NotSupportedException("Only .pdf and .docx original artifacts are supported.");
        }

        var relativePath = Path.Combine("documents", documentId.ToString(), $"original{extension}");
        return WriteStreamAsync(relativePath, source, cancellationToken);
    }

    public async Task<DocumentArtifactWriteResult> SaveConvertedMarkdownAsync(
        int documentId,
        string markdown,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.Combine("documents", documentId.ToString(), "converted.md");
        await using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(markdown));
        return await WriteStreamAsync(relativePath, stream, cancellationToken);
    }

    public async Task<string> ReadConvertedMarkdownAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        return await File.ReadAllTextAsync(
            ResolveTrustedPath(relativePath),
            Encoding.UTF8,
            cancellationToken);
    }

    public FileInfo GetFileInfo(string relativePath)
    {
        return new FileInfo(ResolveTrustedPath(relativePath));
    }

    public bool Exists(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        try
        {
            return File.Exists(ResolveTrustedPath(relativePath));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public Task DeleteArtifactsAsync(
        MarkdownDocument document,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = ResolveTrustedDirectory(Path.Combine("documents", document.Id.ToString()));
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
            logger.LogInformation("Deleted document artifact directory: {Directory}", directory);
        }

        return Task.CompletedTask;
    }

    private async Task<DocumentArtifactWriteResult> WriteStreamAsync(
        string relativePath,
        Stream source,
        CancellationToken cancellationToken)
    {
        var absolutePath = ResolveTrustedPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using var output = File.Create(absolutePath);
        using var sha256 = SHA256.Create();
        await using (var crypto = new CryptoStream(output, sha256, CryptoStreamMode.Write))
        {
            await source.CopyToAsync(crypto, cancellationToken);
            crypto.FlushFinalBlock();
        }

        var info = new FileInfo(absolutePath);
        return new DocumentArtifactWriteResult(
            absolutePath,
            relativePath,
            Convert.ToHexStringLower(sha256.Hash!),
            info.Length);
    }

    private string ResolveTrustedPath(string relativePath)
    {
        var absolutePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var trustedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;

        if (!absolutePath.StartsWith(trustedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Document artifact path is outside the configured root.");
        }

        return absolutePath;
    }

    private string ResolveTrustedDirectory(string relativePath)
    {
        var markerPath = ResolveTrustedPath(Path.Combine(relativePath, ".marker"));
        return Path.GetDirectoryName(markerPath)!;
    }
}
```

- [ ] **Step 6: Register artifact store**

Add using to `Program.cs`:

```csharp
using LightRAGNet.Server.Services.DocumentArtifacts;
```

Add after `builder.Services.AddDbContext<AppDbContext>(...)`:

```csharp
builder.Services.Configure<DocumentArtifactStoreOptions>(options =>
{
    var workingDir = builder.Configuration["LightRAG:WorkingDir"] ?? "rag_storage";
    if (!Path.IsPathRooted(workingDir))
    {
        workingDir = Path.Combine(baseDir, workingDir);
    }

    options.RootPath = workingDir;
});
builder.Services.AddScoped<IDocumentArtifactStore, FileSystemDocumentArtifactStore>();
```

- [ ] **Step 7: Run green tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~DocumentArtifactStoreTests" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 8: Commit**

```powershell
git add src\LightRAGNet.Server\Services\DocumentArtifacts src\LightRAGNet.Server\Program.cs tests\LightRAGNet.Server.Tests\DocumentArtifactStoreTests.cs
git commit -m "feat: add document artifact store"
```

### Task 3: ManagedCode Converter Adapter

**Files:**
- Create: `src/LightRAGNet.Server/Services/DocumentConversion/DocumentMarkdownConversionResult.cs`
- Create: `src/LightRAGNet.Server/Services/DocumentConversion/IDocumentMarkdownConverter.cs`
- Create: `src/LightRAGNet.Server/Services/DocumentConversion/ManagedCodeDocumentMarkdownConverter.cs`
- Modify: `src/LightRAGNet.Server/Program.cs`
- Test: `tests/LightRAGNet.Server.Tests/ManagedCodeDocumentMarkdownConverterTests.cs`

- [ ] **Step 1: Write converter tests**

Create `tests/LightRAGNet.Server.Tests/ManagedCodeDocumentMarkdownConverterTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Server.Services.DocumentConversion;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightRAGNet.Server.Tests;

public sealed class ManagedCodeDocumentMarkdownConverterTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "LightRAGNet.Converter.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConvertAsync_WhenExtensionUnsupported_ThrowsNotSupportedException()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample.txt");
        await File.WriteAllTextAsync(path, "plain text");
        var converter = CreateConverter();

        var act = () => converter.ConvertAsync(
            new FileInfo(path),
            "sample.txt",
            "text/plain",
            CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("Only .pdf and .docx conversion is supported.");
    }

    [Fact]
    public async Task ConvertAsync_WhenSourceFileMissing_ThrowsFileNotFoundException()
    {
        var converter = CreateConverter();

        var act = () => converter.ConvertAsync(
            new FileInfo(Path.Combine(directory, "missing.pdf")),
            "missing.pdf",
            "application/pdf",
            CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ManagedCodeDocumentMarkdownConverter CreateConverter()
    {
        return new ManagedCodeDocumentMarkdownConverter(
            NullLogger<ManagedCodeDocumentMarkdownConverter>.Instance);
    }
}
```

These tests cover the adapter boundary. End-to-end PDF/DOCX conversion will be covered by a manual smoke command in Task 9 using real files, because handcrafted PDF/DOCX fixtures tend to test the fixture more than the library.

- [ ] **Step 2: Run red tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ManagedCodeDocumentMarkdownConverterTests" --no-restore --verbosity minimal
```

Expected: FAIL because converter types do not exist.

- [ ] **Step 3: Add converter result and interface**

Create `DocumentMarkdownConversionResult.cs`:

```csharp
namespace LightRAGNet.Server.Services.DocumentConversion;

public sealed record DocumentMarkdownConversionResult(
    string Markdown,
    string? DetectedMediaType = null,
    IReadOnlyList<string>? Warnings = null);
```

Create `IDocumentMarkdownConverter.cs`:

```csharp
namespace LightRAGNet.Server.Services.DocumentConversion;

public interface IDocumentMarkdownConverter
{
    Task<DocumentMarkdownConversionResult> ConvertAsync(
        FileInfo sourceFile,
        string originalFileName,
        string? contentType,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement ManagedCode adapter**

Create `ManagedCodeDocumentMarkdownConverter.cs`:

```csharp
using MarkItDown;

namespace LightRAGNet.Server.Services.DocumentConversion;

public sealed class ManagedCodeDocumentMarkdownConverter(
    ILogger<ManagedCodeDocumentMarkdownConverter> logger) : IDocumentMarkdownConverter
{
    public async Task<DocumentMarkdownConversionResult> ConvertAsync(
        FileInfo sourceFile,
        string originalFileName,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (extension is not ".pdf" and not ".docx")
        {
            throw new NotSupportedException("Only .pdf and .docx conversion is supported.");
        }

        if (!sourceFile.Exists)
        {
            throw new FileNotFoundException("Source document file was not found.", sourceFile.FullName);
        }

        logger.LogInformation("Converting document to Markdown: {FileName}", originalFileName);

        await using var client = new MarkItDownClient();
        await using var result = await client.ConvertAsync(
            sourceFile.FullName,
            cancellationToken: cancellationToken);

        var markdown = result.Markdown?.Trim();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidOperationException("Document conversion produced empty Markdown.");
        }

        return new DocumentMarkdownConversionResult(
            markdown,
            contentType ?? GuessMediaType(extension),
            []);
    }

    private static string GuessMediaType(string extension)
    {
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };
    }
}
```

This adapter must not call `ConvertFromUrlAsync`, must not configure AI model providers, and must not configure Azure/Google/AWS provider options.

- [ ] **Step 5: Register converter**

Add using to `Program.cs`:

```csharp
using LightRAGNet.Server.Services.DocumentConversion;
```

Add service registration:

```csharp
builder.Services.AddScoped<IDocumentMarkdownConverter, ManagedCodeDocumentMarkdownConverter>();
```

- [ ] **Step 6: Run green tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~ManagedCodeDocumentMarkdownConverterTests" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src\LightRAGNet.Server\Services\DocumentConversion src\LightRAGNet.Server\Program.cs tests\LightRAGNet.Server.Tests\ManagedCodeDocumentMarkdownConverterTests.cs
git commit -m "feat: add managedcode document converter"
```

### Task 4: Upload Saves PDF/DOCX Without Starting RAG

**Files:**
- Modify: `src/LightRAGNet.Server/Services/DocumentIntakeService.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`

- [ ] **Step 1: Replace existing batch upload tests**

Replace `UploadMarkdownDocumentsBatch_CreatesOneTrackForAllFiles` and `UploadMarkdownDocumentsBatch_UsesUploadSourceUriForFileUrlAndQueuePath` with:

```csharp
[Fact]
public async Task UploadDocumentsBatch_WhenPdfAndDocx_SavesOriginalsButDoesNotQueueRag()
{
    var queue = new RecordingRagTaskQueueService();
    using var factory = new LightRagServerFactory(services =>
    {
        services.RemoveAll<IRagTaskQueueService>();
        services.AddSingleton<IRagTaskQueueService>(queue);
    });
    using var client = factory.CreateClient();
    using var content = new MultipartFormDataContent();
    content.Add(new ByteArrayContent("pdf bytes"u8.ToArray()), "files", "合同.pdf");
    content.Add(new ByteArrayContent("docx bytes"u8.ToArray()), "files", "说明书.docx");

    var response = await client.PostAsync("/api/MarkdownDocuments/upload", content);

    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var body = await response.Content.ReadFromJsonAsync<DocumentSubmissionResponse>();
    body.Should().NotBeNull();
    body!.TrackId.Should().NotBeNullOrWhiteSpace();
    body.Documents.Should().HaveCount(2);
    body.Documents.Select(d => d.FileName).Should().BeEquivalentTo(["合同.pdf", "说明书.docx"]);
    body.Documents.Select(d => d.RagStatus).Should().OnlyContain(status => status is null);
    body.Documents.Select(d => d.RagCurrentStage).Should().OnlyContain(stage => stage is null);
    body.Documents.Select(d => d.ConversionStatus).Should().OnlyContain(status => status == DocumentConversionStatus.NotStarted);
    body.Documents.Select(d => d.IsInRagSystem).Should().OnlyContain(value => value == false);
    queue.EnqueueCalls.Should().BeEmpty();

    using var scope = factory.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var documents = context.MarkdownDocuments.OrderBy(d => d.FileName).ToList();
    documents.Should().HaveCount(2);
    documents[0].OriginalFilePath.Should().EndWith(Path.Combine("documents", documents[0].Id.ToString(), "original.pdf"));
    documents[1].OriginalFilePath.Should().EndWith(Path.Combine("documents", documents[1].Id.ToString(), "original.docx"));
    documents.Select(d => d.OriginalContentHash).Should().OnlyContain(hash => !string.IsNullOrWhiteSpace(hash));
}
```

Add this unsupported extension test:

```csharp
[Theory]
[InlineData("notes.md")]
[InlineData("notes.txt")]
[InlineData("slides.pptx")]
[InlineData("legacy.doc")]
[InlineData("program.exe")]
public async Task UploadDocumentsBatch_WhenExtensionUnsupported_ReturnsBadRequest(string fileName)
{
    using var factory = new LightRagServerFactory();
    using var client = factory.CreateClient();
    using var content = new MultipartFormDataContent();
    content.Add(new ByteArrayContent("content"u8.ToArray()), "files", fileName);

    var response = await client.PostAsync("/api/MarkdownDocuments/upload", content);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var error = await response.Content.ReadAsStringAsync();
    error.Should().Contain("Only .pdf and .docx files are supported.");
}
```

Change `UploadMarkdownDocumentsBatch_WhenFileExceedsLimit_ReturnsBadRequest` to upload `"large.pdf"`, not `"large.md"`.

- [ ] **Step 2: Run red tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~UploadDocumentsBatch" --no-restore --verbosity minimal
```

Expected: FAIL because current batch upload accepts `.md/.markdown/.txt`, reads text, and queues RAG tasks immediately.

- [ ] **Step 3: Update DocumentIntakeService constructor**

Add `IDocumentArtifactStore artifactStore`:

```csharp
public sealed class DocumentIntakeService(
    AppDbContext context,
    IRagTaskQueueService taskQueueService,
    IDocumentArtifactStore artifactStore,
    ILogger<DocumentIntakeService> logger)
```

Add usings:

```csharp
using LightRAGNet.Server.Services.DocumentArtifacts;
```

- [ ] **Step 4: Make text submissions explicitly conversion-not-required**

Inside `SubmitDocumentsAsync(...)`, add this property when constructing `MarkdownDocument`:

```csharp
ConversionStatus = DocumentConversionStatus.NotRequired,
```

- [ ] **Step 5: Replace uploaded file implementation**

Replace `SubmitUploadedFilesAsync(...)` with:

```csharp
public async Task<DocumentSubmissionResponse> SubmitUploadedFilesAsync(
    IReadOnlyList<IFormFile> files,
    CancellationToken cancellationToken)
{
    if (files.Count == 0)
    {
        throw new ArgumentException("At least one file is required.", nameof(files));
    }

    foreach (var file in files)
    {
        ValidateUploadedDocument(file);
    }

    var trackId = CreateTrackId();
    var now = DateTime.UtcNow;
    var documents = files.Select(file => new MarkdownDocument
    {
        FileName = file.FileName,
        OriginalFileName = file.FileName,
        OriginalContentType = string.IsNullOrWhiteSpace(file.ContentType) ? GuessContentType(file.FileName) : file.ContentType,
        Content = string.Empty,
        FileSize = file.Length,
        UploadTime = now,
        FileUrl = CreateSourceUri("upload", trackId, file.FileName),
        TrackId = trackId,
        RagStatus = null,
        RagCurrentStage = null,
        ActiveRagTaskId = null,
        ConversionStatus = DocumentConversionStatus.NotStarted,
        IsInRagSystem = false,
        RagProgress = 0
    }).ToList();

    context.MarkdownDocuments.AddRange(documents);
    await context.SaveChangesAsync(cancellationToken);

    try
    {
        for (var i = 0; i < files.Count; i++)
        {
            await using var stream = files[i].OpenReadStream();
            var saved = await artifactStore.SaveOriginalAsync(
                documents[i].Id,
                stream,
                files[i].FileName,
                cancellationToken);

            documents[i].OriginalFilePath = saved.RelativePath;
            documents[i].OriginalContentHash = saved.Hash;
            documents[i].FileHash = saved.Hash;
            documents[i].FileSize = saved.Size;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Original document file could not be saved for track {TrackId}", trackId);
        foreach (var document in documents)
        {
            await artifactStore.DeleteArtifactsAsync(document, CancellationToken.None);
        }

        context.MarkdownDocuments.RemoveRange(documents);
        await context.SaveChangesAsync(CancellationToken.None);
        throw new ArgumentException("Original file could not be saved.", nameof(files), ex);
    }

    return new DocumentSubmissionResponse
    {
        TrackId = trackId,
        Documents = documents.Select(d => d.ToDto()).ToList()
    };
}
```

Add helper methods near `IsSupportedUploadExtension`:

```csharp
private static void ValidateUploadedDocument(IFormFile file)
{
    if (file.Length == 0)
    {
        throw new ArgumentException("File cannot be empty.");
    }

    if (file.Length > MaxUploadFileSize)
    {
        throw new ArgumentException("File size cannot exceed 10MB.");
    }

    if (!IsSupportedUploadExtension(Path.GetExtension(file.FileName)))
    {
        throw new ArgumentException("Only .pdf and .docx files are supported.");
    }
}

private static bool IsSupportedUploadExtension(string? extension)
{
    return extension?.ToLowerInvariant() is ".pdf" or ".docx";
}

private static string GuessContentType(string fileName)
{
    return Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };
}
```

Remove or replace the old `.md/.markdown/.txt` implementation of `IsSupportedUploadExtension`.

- [ ] **Step 6: Run green tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~UploadDocumentsBatch|FullyQualifiedName~SubmitTextDocuments" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src\LightRAGNet.Server\Services\DocumentIntakeService.cs tests\LightRAGNet.Server.Tests\DocumentIntakePipelineApiTests.cs
git commit -m "feat: upload documents without starting rag"
```

### Task 5: Add to RAG Queues Conversion for PDF/DOCX

**Files:**
- Modify: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`

- [ ] **Step 1: Add PDF/DOCX Add-to-RAG test**

Add after `AddToRagSystem_WhenQueueAcceptsTask_StoresActiveTaskId`:

```csharp
[Fact]
public async Task AddToRagSystem_WhenUploadedPdf_QueuesConversionButDoesNotQueueRagTask()
{
    var queue = new RecordingRagTaskQueueService();
    using var factory = new LightRagServerFactory(services =>
    {
        services.RemoveAll<IRagTaskQueueService>();
        services.AddSingleton<IRagTaskQueueService>(queue);
    });
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 901,
        FileName = "合同.pdf",
        Content = string.Empty,
        OriginalFileName = "合同.pdf",
        OriginalFilePath = Path.Combine("documents", "901", "original.pdf"),
        OriginalContentType = "application/pdf",
        ConversionStatus = DocumentConversionStatus.NotStarted,
        IsInRagSystem = false,
        RagStatus = null
    });
    using var client = factory.CreateClient();

    var response = await client.PostAsync("/api/MarkdownDocuments/901/add-to-rag", null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    queue.EnqueueCalls.Should().BeEmpty();

    var body = await response.Content.ReadFromJsonAsync<MarkdownDocumentDto>();
    body.Should().NotBeNull();
    body!.FileName.Should().Be("合同.pdf");
    body.RagStatus.Should().Be(DocumentIntakeStatus.Queued);
    body.RagCurrentStage.Should().Be("Accepted");
    body.ConversionStatus.Should().Be(DocumentConversionStatus.Queued);
    body.ActiveRagTaskId.Should().BeNull();

    using var scope = factory.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var document = await context.MarkdownDocuments.FindAsync(901);
    document!.FileName.Should().Be("合同.pdf");
    document.RagStatus.Should().Be(DocumentIntakeStatus.Queued);
    document.RagCurrentStage.Should().Be("Accepted");
    document.ConversionStatus.Should().Be(DocumentConversionStatus.Queued);
    document.ActiveRagTaskId.Should().BeNull();
}
```

- [ ] **Step 2: Add Markdown regression test**

Add:

```csharp
[Fact]
public async Task AddToRagSystem_WhenMarkdownDocument_EnqueuesRagTaskImmediately()
{
    var queue = new RecordingRagTaskQueueService();
    using var factory = new LightRagServerFactory(services =>
    {
        services.RemoveAll<IRagTaskQueueService>();
        services.AddSingleton<IRagTaskQueueService>(queue);
    });
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 902,
        FileName = "notes.md",
        Content = "# Notes",
        FileUrl = "/uploads/notes.md",
        ConversionStatus = DocumentConversionStatus.NotRequired,
        IsInRagSystem = false
    });
    using var client = factory.CreateClient();

    var response = await client.PostAsync("/api/MarkdownDocuments/902/add-to-rag", null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    queue.EnqueueCalls.Should().ContainSingle();
    queue.EnqueueCalls[0].Content.Should().Be("# Notes");

    using var scope = factory.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var document = await context.MarkdownDocuments.FindAsync(902);
    document!.RagStatus.Should().Be("Pending");
    document.ActiveRagTaskId.Should().Be("task-1");
}
```

- [ ] **Step 3: Run red tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~AddToRagSystem_When" --no-restore --verbosity minimal
```

Expected: PDF test FAILS because current controller enqueues empty `Content` directly.

- [ ] **Step 4: Add PDF/DOCX branch in controller**

Add helper methods at the bottom of `MarkdownDocumentsController`:

```csharp
private static bool RequiresDocumentConversion(MarkdownDocument document)
{
    if (string.IsNullOrWhiteSpace(document.OriginalFilePath))
    {
        return false;
    }

    var extension = Path.GetExtension(document.OriginalFileName ?? document.FileName);
    return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);
}

private static void QueueDocumentConversion(MarkdownDocument document)
{
    document.RagStatus = DocumentIntakeStatus.Queued;
    document.RagCurrentStage = "Accepted";
    document.RagProgress = 0;
    document.RagErrorMessage = null;
    document.ActiveRagTaskId = null;
    document.PipelineStartedAt = null;
    document.PipelineCompletedAt = null;
    document.PipelineCancelledAt = null;
    document.ConversionStatus = DocumentConversionStatus.Queued;
    document.ConversionErrorMessage = null;
    document.ConversionStartedAt = null;
    document.ConversionCompletedAt = null;
}
```

In `AddToRagSystem`, after the existing active-status guard and before the direct `EnqueueTaskAsync` code, add:

```csharp
if (RequiresDocumentConversion(document))
{
    QueueDocumentConversion(document);
    await context.SaveChangesAsync(HttpContext.RequestAborted);

    logger.LogInformation("Document conversion queued from Add to RAG: DocumentId={DocumentId}", document.Id);
    return Ok(document.ToDto());
}
```

- [ ] **Step 5: Run green tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~AddToRagSystem_When" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src\LightRAGNet.Server\Controllers\MarkdownDocumentsController.cs tests\LightRAGNet.Server.Tests\DocumentIntakePipelineApiTests.cs
git commit -m "feat: queue conversion from add to rag"
```

### Task 6: Conversion Processor and Worker

**Files:**
- Create: `src/LightRAGNet.Server/Services/DocumentConversion/DocumentConversionProcessor.cs`
- Create: `src/LightRAGNet.Server/Services/DocumentConversion/DocumentConversionWorker.cs`
- Modify: `src/LightRAGNet.Server/Program.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentConversionProcessorTests.cs`

- [ ] **Step 1: Create processor test doubles inside test file**

Create `tests/LightRAGNet.Server.Tests/DocumentConversionProcessorTests.cs` with the fake converter and queue included in the same file:

```csharp
using FluentAssertions;
using LightRAGNet.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services;
using LightRAGNet.Server.Services.DocumentArtifacts;
using LightRAGNet.Server.Services.DocumentConversion;
using LightRAGNet.Services.TaskQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LightRAGNet.Server.Tests;

[Collection(ServerFilesystemTestCollection.Name)]
public sealed class DocumentConversionProcessorTests
{
    [Fact]
    public async Task ProcessNextBatchAsync_WhenQueuedConversionSucceeds_WritesMarkdownAndEnqueuesRag()
    {
        var converter = new FakeDocumentMarkdownConverter { Markdown = "# Converted\n\nHello" };
        var queue = new RecordingRagTaskQueueService();
        using var factory = CreateFactory(converter, queue);
        var documentId = await SeedQueuedSourceAsync(factory, "合同.pdf", "application/pdf");
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<DocumentConversionProcessor>();

        var processed = await processor.ProcessNextBatchAsync(10, CancellationToken.None);

        processed.Should().Be(1);
        converter.CallCount.Should().Be(1);
        queue.EnqueueCalls.Should().ContainSingle();
        queue.EnqueueCalls[0].DocumentId.Should().Be(documentId);
        queue.EnqueueCalls[0].Content.Should().Be("# Converted\n\nHello");

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.ChangeTracker.Clear();
        var document = await context.MarkdownDocuments.FindAsync(documentId);
        document!.FileName.Should().Be("合同.pdf");
        document.Content.Should().Be("# Converted\n\nHello");
        document.ConversionStatus.Should().Be(DocumentConversionStatus.Completed);
        document.ConvertedMarkdownPath.Should().EndWith(Path.Combine("documents", documentId.ToString(), "converted.md"));
        document.ConvertedMarkdownHash.Should().NotBeNullOrWhiteSpace();
        document.ConversionTool.Should().Be("ManagedCode.MarkItDown");
        document.ConversionToolVersion.Should().Be("10.0.7");
        document.RagStatus.Should().Be(DocumentIntakeStatus.Queued);
        document.RagCurrentStage.Should().Be("Indexing");
        document.ActiveRagTaskId.Should().Be("task-1");
    }

    [Fact]
    public async Task ProcessNextBatchAsync_IgnoresUploadedDocumentBeforeAddToRag()
    {
        var converter = new FakeDocumentMarkdownConverter();
        var queue = new RecordingRagTaskQueueService();
        using var factory = CreateFactory(converter, queue);
        await SeedSourceAsync(factory, "说明书.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", DocumentConversionStatus.NotStarted, ragStatus: null);
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<DocumentConversionProcessor>();

        var processed = await processor.ProcessNextBatchAsync(10, CancellationToken.None);

        processed.Should().Be(0);
        converter.CallCount.Should().Be(0);
        queue.EnqueueCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessNextBatchAsync_WhenConverterReturnsEmpty_MarksFailedWithoutRagTask()
    {
        var converter = new FakeDocumentMarkdownConverter { Markdown = "   " };
        var queue = new RecordingRagTaskQueueService();
        using var factory = CreateFactory(converter, queue);
        var documentId = await SeedQueuedSourceAsync(factory, "empty.pdf", "application/pdf");
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<DocumentConversionProcessor>();

        await processor.ProcessNextBatchAsync(10, CancellationToken.None);

        queue.EnqueueCalls.Should().BeEmpty();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.ChangeTracker.Clear();
        var document = await context.MarkdownDocuments.FindAsync(documentId);
        document!.RagStatus.Should().Be(DocumentIntakeStatus.Failed);
        document.RagCurrentStage.Should().Be("Converting");
        document.ConversionStatus.Should().Be(DocumentConversionStatus.Failed);
        document.ConversionErrorMessage.Should().Be("Document conversion produced empty Markdown.");
        document.ActiveRagTaskId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessNextBatchAsync_WhenConverterThrows_SanitizesUserFacingError()
    {
        var converter = new FakeDocumentMarkdownConverter
        {
            Exception = new InvalidOperationException("C:\\secret\\contract.pdf exploded")
        };
        var queue = new RecordingRagTaskQueueService();
        using var factory = CreateFactory(converter, queue);
        var documentId = await SeedQueuedSourceAsync(factory, "secret.pdf", "application/pdf");
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<DocumentConversionProcessor>();

        await processor.ProcessNextBatchAsync(10, CancellationToken.None);

        queue.EnqueueCalls.Should().BeEmpty();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.ChangeTracker.Clear();
        var document = await context.MarkdownDocuments.FindAsync(documentId);
        document!.ConversionStatus.Should().Be(DocumentConversionStatus.Failed);
        document.ConversionErrorMessage.Should().Be("Document conversion failed.");
        document.RagErrorMessage.Should().Be("Document conversion failed.");
    }

    [Fact]
    public async Task ProcessNextBatchAsync_WhenRagQueueRejects_AfterConversionKeepsConversionCompleted()
    {
        var converter = new FakeDocumentMarkdownConverter { Markdown = "# Converted\n\nHello" };
        var queue = new RecordingRagTaskQueueService { RejectEnqueue = true };
        using var factory = CreateFactory(converter, queue);
        var documentId = await SeedQueuedSourceAsync(factory, "queue-fail.pdf", "application/pdf");
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<DocumentConversionProcessor>();

        await processor.ProcessNextBatchAsync(10, CancellationToken.None);

        queue.EnqueueCalls.Should().ContainSingle();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.ChangeTracker.Clear();
        var document = await context.MarkdownDocuments.FindAsync(documentId);
        document!.ConversionStatus.Should().Be(DocumentConversionStatus.Completed);
        document.ConvertedMarkdownPath.Should().NotBeNullOrWhiteSpace();
        document.RagStatus.Should().Be(DocumentIntakeStatus.Failed);
        document.RagCurrentStage.Should().Be("Indexing");
        document.RagErrorMessage.Should().Be("Document could not be queued for indexing.");
        document.ActiveRagTaskId.Should().BeNull();
    }

    private static LightRagServerFactory CreateFactory(
        FakeDocumentMarkdownConverter converter,
        RecordingRagTaskQueueService queue)
    {
        return new LightRagServerFactory(services =>
        {
            services.RemoveAll<IDocumentMarkdownConverter>();
            services.AddSingleton<IDocumentMarkdownConverter>(converter);
            services.RemoveAll<IRagTaskQueueService>();
            services.AddSingleton<IRagTaskQueueService>(queue);
        });
    }

    private static Task<int> SeedQueuedSourceAsync(
        LightRagServerFactory factory,
        string fileName,
        string contentType)
    {
        return SeedSourceAsync(factory, fileName, contentType, DocumentConversionStatus.Queued, DocumentIntakeStatus.Queued);
    }

    private static async Task<int> SeedSourceAsync(
        LightRagServerFactory factory,
        string fileName,
        string contentType,
        string conversionStatus,
        string? ragStatus)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentArtifactStore>();
        var document = new MarkdownDocument
        {
            FileName = fileName,
            OriginalFileName = fileName,
            OriginalContentType = contentType,
            Content = string.Empty,
            TrackId = "track-convert",
            FileUrl = $"upload://track-convert/{Uri.EscapeDataString(fileName)}",
            RagStatus = ragStatus,
            RagCurrentStage = ragStatus == DocumentIntakeStatus.Queued ? "Accepted" : null,
            ConversionStatus = conversionStatus,
            FileSize = 5
        };
        context.MarkdownDocuments.Add(document);
        await context.SaveChangesAsync();

        await using var stream = new MemoryStream("bytes"u8.ToArray());
        var saved = await store.SaveOriginalAsync(document.Id, stream, fileName, CancellationToken.None);
        document.OriginalFilePath = saved.RelativePath;
        document.OriginalContentHash = saved.Hash;
        await context.SaveChangesAsync();
        return document.Id;
    }

    private sealed class FakeDocumentMarkdownConverter : IDocumentMarkdownConverter
    {
        public string Markdown { get; set; } = "# Converted";
        public Exception? Exception { get; set; }
        public int CallCount { get; private set; }

        public Task<DocumentMarkdownConversionResult> ConvertAsync(
            FileInfo sourceFile,
            string originalFileName,
            string? contentType,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(new DocumentMarkdownConversionResult(Markdown, contentType, []));
        }
    }

    private sealed class RecordingRagTaskQueueService : IRagTaskQueueService
    {
        private int nextTaskId;
        public List<EnqueueCall> EnqueueCalls { get; } = [];
        public bool RejectEnqueue { get; set; }

        public Task<string?> EnqueueTaskAsync(int documentId, string content, string filePath, CancellationToken cancellationToken = default)
        {
            EnqueueCalls.Add(new EnqueueCall(documentId, content, filePath));
            if (RejectEnqueue)
            {
                return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>($"task-{++nextTaskId}");
        }

        public Task<string?> EnqueueDeletionTaskAsync(int documentId, string ragDocumentId, string filePath, bool deleteLlmCache, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<RagTask?> GetNextTaskAsync(CancellationToken cancellationToken = default) => Task.FromResult<RagTask?>(null);
        public Task<List<RagTask>> GetAllTasksAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<RagTask>());
        public Task<RagTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) => Task.FromResult<RagTask?>(null);
        public Task<RagTask?> GetTaskByDocumentIdAsync(int documentId, CancellationToken cancellationToken = default) => Task.FromResult<RagTask?>(null);
        public Task<Dictionary<int, RagTask>> GetTasksByDocumentIdsAsync(IEnumerable<int> documentIds, CancellationToken cancellationToken = default) => Task.FromResult(new Dictionary<int, RagTask>());
        public Task UpdateTaskStatusAsync(string taskId, RagTaskStatus status, string? errorMessage = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateTaskProgressAsync(string taskId, TaskStage? stage, int? progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReorderTaskAsync(string taskId, int newPriority, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> RetryTaskAsync(string taskId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ClearAllTasksAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> HasProcessingTasksAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<int> StopAllTasksAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed record EnqueueCall(int DocumentId, string Content, string FilePath);
}
```

- [ ] **Step 2: Run red tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~DocumentConversionProcessorTests" --no-restore --verbosity minimal
```

Expected: FAIL because `DocumentConversionProcessor` is missing.

- [ ] **Step 3: Implement conversion processor**

Create `src/LightRAGNet.Server/Services/DocumentConversion/DocumentConversionProcessor.cs`:

```csharp
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services.DocumentArtifacts;
using LightRAGNet.Services.TaskQueue;
using Microsoft.EntityFrameworkCore;

namespace LightRAGNet.Server.Services.DocumentConversion;

public sealed class DocumentConversionProcessor(
    AppDbContext context,
    IDocumentArtifactStore artifactStore,
    IDocumentMarkdownConverter converter,
    IRagTaskQueueService taskQueueService,
    ILogger<DocumentConversionProcessor> logger)
{
    public async Task<int> ProcessNextBatchAsync(
        int maxDocuments,
        CancellationToken cancellationToken)
    {
        var documents = await context.MarkdownDocuments
            .Where(d => d.ConversionStatus == DocumentConversionStatus.Queued &&
                        d.RagStatus == DocumentIntakeStatus.Queued)
            .OrderBy(d => d.UploadTime)
            .Take(maxDocuments)
            .ToListAsync(cancellationToken);

        foreach (var document in documents)
        {
            await ProcessDocumentAsync(document, cancellationToken);
        }

        return documents.Count;
    }

    private async Task ProcessDocumentAsync(
        MarkdownDocument document,
        CancellationToken cancellationToken)
    {
        document.RagStatus = DocumentIntakeStatus.Processing;
        document.RagCurrentStage = "Converting";
        document.ConversionStatus = DocumentConversionStatus.Processing;
        document.ConversionStartedAt = DateTime.UtcNow;
        document.ConversionCompletedAt = null;
        document.ConversionErrorMessage = null;
        document.RagErrorMessage = null;
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            if (string.IsNullOrWhiteSpace(document.OriginalFilePath))
            {
                throw new InvalidOperationException("Original document artifact is missing.");
            }

            var markdown = await ConvertAndPersistMarkdownAsync(document, cancellationToken);
            await QueueConvertedMarkdownForIndexingAsync(document, markdown, cancellationToken);
        }
        catch (Exception ex)
        {
            MarkFailed(document, ex);
            await context.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<string> ConvertAndPersistMarkdownAsync(
        MarkdownDocument document,
        CancellationToken cancellationToken)
    {
        var result = await converter.ConvertAsync(
            artifactStore.GetFileInfo(document.OriginalFilePath!),
            document.OriginalFileName ?? document.FileName,
            document.OriginalContentType,
            cancellationToken);

        var markdown = result.Markdown.Trim();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidOperationException("Document conversion produced empty Markdown.");
        }

        var saved = await artifactStore.SaveConvertedMarkdownAsync(
            document.Id,
            markdown,
            cancellationToken);

        document.Content = markdown;
        document.ConvertedMarkdownPath = saved.RelativePath;
        document.ConvertedMarkdownHash = saved.Hash;
        document.ConversionStatus = DocumentConversionStatus.Completed;
        document.ConversionCompletedAt = DateTime.UtcNow;
        document.ConversionTool = "ManagedCode.MarkItDown";
        document.ConversionToolVersion = "10.0.7";
        await context.SaveChangesAsync(cancellationToken);

        return markdown;
    }

    private async Task QueueConvertedMarkdownForIndexingAsync(
        MarkdownDocument document,
        string markdown,
        CancellationToken cancellationToken)
    {
        try
        {
            var taskId = await taskQueueService.EnqueueTaskAsync(
                document.Id,
                markdown,
                document.FileUrl ?? document.FileName,
                cancellationToken);

            if (taskId is null)
            {
                MarkIndexQueueFailed(document);
                await context.SaveChangesAsync(CancellationToken.None);
                return;
            }

            document.ActiveRagTaskId = taskId;
            document.RagStatus = DocumentIntakeStatus.Queued;
            document.RagCurrentStage = "Indexing";
            document.RagProgress = 0;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Document indexing queue failed for document {DocumentId}", document.Id);
            MarkIndexQueueFailed(document);
            await context.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static void MarkIndexQueueFailed(MarkdownDocument document)
    {
        document.RagStatus = DocumentIntakeStatus.Failed;
        document.RagCurrentStage = "Indexing";
        document.RagErrorMessage = "Document could not be queued for indexing.";
        document.ActiveRagTaskId = null;
    }

    private void MarkFailed(MarkdownDocument document, Exception ex)
    {
        var message = ex.Message == "Document conversion produced empty Markdown."
            ? ex.Message
            : "Document conversion failed.";

        document.RagStatus = DocumentIntakeStatus.Failed;
        document.RagCurrentStage = "Converting";
        document.ConversionStatus = DocumentConversionStatus.Failed;
        document.ConversionCompletedAt = DateTime.UtcNow;
        document.ConversionErrorMessage = message;
        document.RagErrorMessage = message;
        document.ActiveRagTaskId = null;

        logger.LogWarning(ex, "Document conversion failed for document {DocumentId}", document.Id);
    }
}
```

- [ ] **Step 4: Implement worker**

Create `DocumentConversionWorker.cs`:

```csharp
namespace LightRAGNet.Server.Services.DocumentConversion;

public sealed class DocumentConversionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentConversionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private const int BatchSize = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<DocumentConversionProcessor>();
                var processed = await processor.ProcessNextBatchAsync(BatchSize, stoppingToken);

                if (processed == 0)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Document conversion worker failed.");
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
```

- [ ] **Step 5: Register processor and worker**

Add to `Program.cs`:

```csharp
builder.Services.AddScoped<DocumentConversionProcessor>();
builder.Services.AddHostedService<DocumentConversionWorker>();
```

Tests already remove `IHostedService` in `LightRagServerFactory`, so the worker will not run during API tests unless explicitly registered back.

- [ ] **Step 6: Run green tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~DocumentConversionProcessorTests" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src\LightRAGNet.Server\Services\DocumentConversion src\LightRAGNet.Server\Program.cs tests\LightRAGNet.Server.Tests\DocumentConversionProcessorTests.cs
git commit -m "feat: convert documents before rag indexing"
```

### Task 7: Retry, Cancel, Delete, and Clear-All

**Files:**
- Modify: `src/LightRAGNet.Server/Services/DocumentIntakeService.cs`
- Modify: `src/LightRAGNet.Server/Services/MarkdownDocumentDeletionService.cs`
- Modify: `src/LightRAGNet.Server/Controllers/MarkdownDocumentsController.cs`
- Modify: `src/LightRAGNet.Server/Handlers/RagTaskStatusChangedHandler.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentIntakePipelineApiTests.cs`

- [ ] **Step 1: Add conversion retry tests**

Add:

```csharp
[Fact]
public async Task RetryDocument_WhenConversionFailed_RequeuesConversionWithoutRagTask()
{
    var queue = new RecordingRagTaskQueueService();
    using var factory = new LightRagServerFactory(services =>
    {
        services.RemoveAll<IRagTaskQueueService>();
        services.AddSingleton<IRagTaskQueueService>(queue);
    });
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 910,
        FileName = "failed.pdf",
        Content = string.Empty,
        OriginalFileName = "failed.pdf",
        OriginalFilePath = Path.Combine("documents", "910", "original.pdf"),
        RagStatus = DocumentIntakeStatus.Failed,
        RagCurrentStage = "Converting",
        ConversionStatus = DocumentConversionStatus.Failed,
        ConversionErrorMessage = "Document conversion failed.",
        RagErrorMessage = "Document conversion failed.",
        RagRetryCount = 2
    });
    using var client = factory.CreateClient();

    var response = await client.PostAsync("/api/MarkdownDocuments/910/retry", null);

    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    queue.EnqueueCalls.Should().BeEmpty();
    using var scope = factory.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var document = await context.MarkdownDocuments.FindAsync(910);
    document!.RagStatus.Should().Be(DocumentIntakeStatus.Queued);
    document.RagCurrentStage.Should().Be("Accepted");
    document.ConversionStatus.Should().Be(DocumentConversionStatus.Queued);
    document.RagRetryCount.Should().Be(3);
    document.ActiveRagTaskId.Should().BeNull();
}
```

Add:

```csharp
[Fact]
public async Task RetryDocument_WhenIndexingFailedAfterConversion_ReusesConvertedMarkdown()
{
    var queue = new RecordingRagTaskQueueService();
    using var factory = new LightRagServerFactory(services =>
    {
        services.RemoveAll<IRagTaskQueueService>();
        services.AddSingleton<IRagTaskQueueService>(queue);
    });
    using (var scope = factory.Services.CreateScope())
    {
        var store = scope.ServiceProvider.GetRequiredService<IDocumentArtifactStore>();
        var saved = await store.SaveConvertedMarkdownAsync(911, "# Existing\n\nMarkdown", CancellationToken.None);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = 911,
            FileName = "indexed.docx",
            Content = "# Existing\n\nMarkdown",
            OriginalFileName = "indexed.docx",
            OriginalFilePath = Path.Combine("documents", "911", "original.docx"),
            ConvertedMarkdownPath = saved.RelativePath,
            ConvertedMarkdownHash = saved.Hash,
            RagStatus = DocumentIntakeStatus.Failed,
            RagCurrentStage = "ProcessingChunks",
            ConversionStatus = DocumentConversionStatus.Completed,
            RagErrorMessage = "index failed"
        });
        await context.SaveChangesAsync();
    }
    using var client = factory.CreateClient();

    var response = await client.PostAsync("/api/MarkdownDocuments/911/retry", null);

    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    queue.EnqueueCalls.Should().ContainSingle();
    queue.EnqueueCalls[0].Content.Should().Be("# Existing\n\nMarkdown");
}
```

- [ ] **Step 2: Add conversion cancel test**

Add:

```csharp
[Fact]
public async Task CancelDocument_WhenConversionQueued_MarksCancelledWithoutQueueCall()
{
    var queue = new RecordingRagTaskQueueService();
    using var factory = new LightRagServerFactory(services =>
    {
        services.RemoveAll<IRagTaskQueueService>();
        services.AddSingleton<IRagTaskQueueService>(queue);
    });
    await SeedDocumentAsync(factory, new MarkdownDocument
    {
        Id = 912,
        FileName = "queued.pdf",
        Content = string.Empty,
        OriginalFilePath = Path.Combine("documents", "912", "original.pdf"),
        RagStatus = DocumentIntakeStatus.Queued,
        RagCurrentStage = "Accepted",
        ConversionStatus = DocumentConversionStatus.Queued
    });
    using var client = factory.CreateClient();

    var response = await client.PostAsync("/api/MarkdownDocuments/912/cancel", null);

    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    queue.CancelCalls.Should().BeEmpty();
    using var scope = factory.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var document = await context.MarkdownDocuments.FindAsync(912);
    document!.RagStatus.Should().Be(DocumentIntakeStatus.Cancelled);
    document.PipelineCancelledAt.Should().NotBeNull();
}
```

- [ ] **Step 3: Run red tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RetryDocument_WhenConversion|FullyQualifiedName~RetryDocument_WhenIndexing|FullyQualifiedName~CancelDocument_WhenConversion" --no-restore --verbosity minimal
```

Expected: FAIL because retry/cancel do not know conversion states.

- [ ] **Step 4: Update retry logic**

In `DocumentIntakeService.RetryDocumentAsync`, before direct RAG enqueue, add conversion branches:

```csharp
if (document.ConversionStatus == DocumentConversionStatus.Failed ||
    RequiresReconversion(document))
{
    document.RagRetryCount++;
    document.RagErrorMessage = null;
    document.ConversionErrorMessage = null;
    document.RagStatus = DocumentIntakeStatus.Queued;
    document.RagCurrentStage = "Accepted";
    document.ConversionStatus = DocumentConversionStatus.Queued;
    document.RagProgress = 0;
    document.PipelineStartedAt = null;
    document.PipelineCompletedAt = null;
    document.PipelineCancelledAt = null;
    document.ActiveRagTaskId = null;
    await context.SaveChangesAsync(cancellationToken);

    return new DocumentPipelineActionResult
    {
        Accepted = true,
        DocumentId = document.Id,
        Status = DocumentIntakeStatus.Queued,
        Message = "Document conversion retry has been queued."
    };
}

var content = document.Content;
if (document.ConversionStatus == DocumentConversionStatus.Completed &&
    !string.IsNullOrWhiteSpace(document.ConvertedMarkdownPath))
{
    content = await artifactStore.ReadConvertedMarkdownAsync(document.ConvertedMarkdownPath, cancellationToken);
}
```

Add helper:

```csharp
private bool RequiresReconversion(MarkdownDocument document)
{
    return document.ConversionStatus == DocumentConversionStatus.Completed &&
           !artifactStore.Exists(document.ConvertedMarkdownPath);
}
```

Use `content` in the existing enqueue call.

- [ ] **Step 5: Update cancel logic**

In `CancelDocumentCoreAsync`, before looking up queue task, handle conversion-only queue:

```csharp
if (document.ConversionStatus is DocumentConversionStatus.Queued or DocumentConversionStatus.Processing &&
    string.IsNullOrWhiteSpace(document.ActiveRagTaskId))
{
    document.RagStatus = DocumentIntakeStatus.Cancelled;
    document.RagCurrentStage = DocumentIntakeStatus.Cancelled;
    document.PipelineCancelledAt = DateTime.UtcNow;
    document.ActiveRagTaskId = null;
    return true;
}
```

- [ ] **Step 6: Add artifact cleanup methods**

Inject `IDocumentArtifactStore` into `MarkdownDocumentDeletionService`.

Add method:

```csharp
public Task DeleteDocumentArtifactsAsync(
    MarkdownDocument document,
    CancellationToken cancellationToken)
{
    return artifactStore.DeleteArtifactsAsync(document, cancellationToken);
}
```

In `MarkdownDocumentsController.DeleteMarkdownDocument`, before removing not-in-RAG documents:

```csharp
await documentDeletionService.DeleteDocumentArtifactsAsync(document, cancellationToken);
```

In `RagTaskStatusChangedHandler`, before removing row after delete task completion:

```csharp
await deletionService.DeleteDocumentArtifactsAsync(document, cancellationToken);
```

In `ClearAllData`, inside the document loop:

```csharp
await documentDeletionService.DeleteDocumentArtifactsAsync(document, CancellationToken.None);
```

Keep existing legacy `/uploads` cleanup calls.

- [ ] **Step 7: Run green tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RetryDocument_WhenConversion|FullyQualifiedName~RetryDocument_WhenIndexing|FullyQualifiedName~CancelDocument_WhenConversion|FullyQualifiedName~DeleteMarkdownDocument|FullyQualifiedName~ClearAllData" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 8: Commit**

```powershell
git add src\LightRAGNet.Server\Services\DocumentIntakeService.cs src\LightRAGNet.Server\Services\MarkdownDocumentDeletionService.cs src\LightRAGNet.Server\Controllers\MarkdownDocumentsController.cs src\LightRAGNet.Server\Handlers\RagTaskStatusChangedHandler.cs tests\LightRAGNet.Server.Tests\DocumentIntakePipelineApiTests.cs
git commit -m "feat: handle conversion retry and cleanup"
```

### Task 8: Web Upload Flow

**Files:**
- Modify: `src/LightRAGNet.Web/ApiClient.cs`
- Modify: `src/LightRAGNet.Web/Components/Pages/MarkdownUpload.razor`
- Test: `tests/LightRAGNet.Tests/Web/MarkdownUploadMarkupTests.cs`
- Test: `tests/LightRAGNet.Tests/Web/MarkdownDocumentsSourceTests.cs`

- [ ] **Step 1: Add Web source tests**

Add to `MarkdownUploadMarkupTests.cs`:

```csharp
[Fact]
public void MarkdownUpload_AcceptsOnlyPdfAndDocx()
{
    var markup = File.ReadAllText(FindRepositoryFile("src/LightRAGNet.Web/Components/Pages/MarkdownUpload.razor"));

    markup.Should().Contain("Accept=\".pdf,.docx\"");
    markup.Should().Contain("PDF");
    markup.Should().Contain("DOCX");
    markup.Should().NotContain("Accept=\".md,.markdown\"");
}

[Fact]
public void MarkdownUpload_CopySaysAddToRagStartsProcessing()
{
    var markup = File.ReadAllText(FindRepositoryFile("src/LightRAGNet.Web/Components/Pages/MarkdownUpload.razor"));

    markup.Should().Contain("Add to RAG");
    markup.Should().Contain("starts processing");
}
```

Add to `MarkdownDocumentsSourceTests.cs`:

```csharp
[Fact]
public void ApiClient_UploadDocument_UsesBatchUploadEndpointAndFilesField()
{
    var source = File.ReadAllText(FindRepositoryFile("src/LightRAGNet.Web/ApiClient.cs"));

    source.Should().Contain("api/MarkdownDocuments/upload");
    source.Should().Contain("content.Add(streamContent, \"files\", file.Name)");
    source.Should().NotContain("PostAsync(\"api/MarkdownDocuments\", content");
}
```

- [ ] **Step 2: Run red Web tests**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~MarkdownUpload|FullyQualifiedName~ApiClient_UploadDocument" --no-restore --verbosity minimal
```

Expected: FAIL because UI and client still target Markdown upload.

- [ ] **Step 3: Update ApiClient upload**

Replace `UploadMarkdownDocumentAsync(...)` or add a new `UploadDocumentAsync(...)` and update call sites:

```csharp
public async Task<UploadResult> UploadDocumentAsync(
    IBrowserFile file,
    CancellationToken cancellationToken = default)
{
    using var content = new MultipartFormDataContent();
    await using var fileStream = file.OpenReadStream(
        maxAllowedSize: 10 * 1024 * 1024,
        cancellationToken: cancellationToken);
    using var streamContent = new StreamContent(fileStream);

    streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
        file.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    content.Add(streamContent, "files", file.Name);

    var response = await httpClient.PostAsync("api/MarkdownDocuments/upload", content, cancellationToken);

    if (response.IsSuccessStatusCode)
    {
        var submission = await response.Content.ReadFromJsonAsync<DocumentSubmissionResponse>(
            cancellationToken: cancellationToken);
        return new UploadResult
        {
            Success = true,
            Document = submission?.Documents.FirstOrDefault(),
            TrackId = submission?.TrackId
        };
    }

    return new UploadResult
    {
        Success = false,
        ErrorMessage = await ReadErrorMessageAsync(response.Content, cancellationToken)
    };
}
```

Add to `UploadResult`:

```csharp
public string? TrackId { get; set; }
```

- [ ] **Step 4: Update upload page**

In `MarkdownUpload.razor`:

- set `Accept=".pdf,.docx"`
- validate extension `.pdf` or `.docx`
- call `ApiClient.UploadDocumentAsync(file)`
- after upload, navigate to document list
- do not call `AddToRagSystemAsync`
- include user-facing copy with these phrases: `PDF`, `DOCX`, `Add to RAG`, `starts processing`

- [ ] **Step 5: Run green Web tests**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~MarkdownUpload|FullyQualifiedName~ApiClient_UploadDocument" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src\LightRAGNet.Web\ApiClient.cs src\LightRAGNet.Web\Components\Pages\MarkdownUpload.razor tests\LightRAGNet.Tests\Web\MarkdownUploadMarkupTests.cs tests\LightRAGNet.Tests\Web\MarkdownDocumentsSourceTests.cs
git commit -m "feat: upload pdf and docx from web"
```

### Task 9: Verification and Manual Smoke

**Files:**
- Review: `docs/superpowers/specs/2026-05-22-markitdown-document-intake-design.md`
- Review: `docs/superpowers/plans/2026-05-22-managedcode-markitdown-document-intake-implementation-plan.md`
- Potential create/update: `docs/superpowers/archives/`
- Potential create/update: `docs/superpowers/problems/`
- Potential create/update: `docs/superpowers/inbox/`

- [ ] **Step 1: Run targeted server tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~DocumentIntakePipelineApiTests|FullyQualifiedName~DocumentArtifactStoreTests|FullyQualifiedName~DocumentConversionProcessorTests|FullyQualifiedName~ManagedCodeDocumentMarkdownConverterTests" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 2: Run targeted Web tests**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "FullyQualifiedName~MarkdownUpload|FullyQualifiedName~MarkdownDocumentsSourceTests" --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 3: Run full solution tests**

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 4: Run diff hygiene**

```powershell
git diff --check
```

Expected: no output.

- [ ] **Step 5: Manual smoke with real files**

Use a small real `.pdf` and `.docx` from a disposable local folder. Do not use private documents. Start server and Web if needed:

```powershell
dotnet run --project .\src\LightRAGNet.Server
dotnet run --project .\src\LightRAGNet.Web
```

Expected manual behavior:

- Upload PDF/DOCX from Web.
- Document list shows original names and `Add to RAG`.
- Before clicking `Add to RAG`, no `converted.md` exists for the document.
- Click `Add to RAG`.
- `documents/{id}/converted.md` appears under `LightRAG:WorkingDir`.
- Status progresses through `Queued` / `Converting` / `Indexing`.

- [ ] **Step 6: Run asset gate**

```powershell
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\asset_status.py . --topic "managedcode-markitdown-document-intake" --json
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\asset_closeout.py . --topic "managedcode-markitdown-document-intake" --json
python C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\check_completion_gate.py . --completed-topic "managedcode-markitdown-document-intake" --json
```

Expected: scripts return the required archive/problem/inbox route. Follow that route before final close-out.

## Self-Review Notes

- Spec coverage: upload-only semantics, original file display, Add to RAG triggered conversion, offline conversion, artifact persistence, retry, cancellation, deletion, Web flow, and verification are each covered by concrete tasks.
- Placeholder scan: no open implementation slots, incomplete sections, or cross-task shorthand instructions remain.
- Type consistency: `DocumentConversionStatus`, `IDocumentArtifactStore`, `IDocumentMarkdownConverter`, `DocumentConversionProcessor`, `OriginalFilePath`, `ConvertedMarkdownPath`, and `ConversionStatus` are named consistently across tasks.
