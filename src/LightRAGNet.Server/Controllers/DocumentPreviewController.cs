using System.Net;
using System.Text;
using System.Text.Json;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Services.DocumentArtifacts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace LightRAGNet.Server.Controllers;

[Route("document-preview")]
public sealed class DocumentPreviewController(
    AppDbContext context,
    IDocumentArtifactStore artifactStore) : Controller
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    [HttpGet("{documentId:int}")]
    public async Task<IActionResult> PreviewPage(int documentId, CancellationToken cancellationToken)
    {
        var document = await context.MarkdownDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        var title = WebUtility.HtmlEncode(document.OriginalFileName ?? document.FileName);
        var pathBase = Request.PathBase.ToUriComponent();
        var contentUrl = JsonSerializer.Serialize($"{pathBase}/api/document-preview/{documentId}/content");
        var originalUrl = JsonSerializer.Serialize($"{pathBase}/api/document-preview/{documentId}/original");
        var body = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{title}}</title>
              <style>
                :root { color-scheme: dark; }
                * { box-sizing: border-box; }
                body { margin: 0; background: #0d1117; color: #edf2f7; font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif; }
                header { padding: 16px 20px; border-bottom: 1px solid #303946; background: #151b23; }
                h1 { margin: 0; font-size: 20px; line-height: 1.35; overflow-wrap: anywhere; }
                main { padding: 16px 20px; }
                iframe { width: 100%; height: calc(100vh - 96px); border: 1px solid #303946; border-radius: 8px; background: #151b23; }
                pre { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; border: 1px solid #303946; border-radius: 8px; background: #151b23; color: #edf2f7; padding: 16px; line-height: 1.6; }
                .status { color: #9fb2c8; }
              </style>
            </head>
            <body>
              <header><h1>{{title}}</h1></header>
              <main id="preview-root"><p class="status">Loading...</p></main>
              <script>
                const root = document.getElementById('preview-root');
                const contentUrl = {{contentUrl}};
                const originalUrl = {{originalUrl}};

                function renderOriginal() {
                  root.replaceChildren();
                  const iframe = document.createElement('iframe');
                  iframe.title = 'Document preview';
                  iframe.src = originalUrl;
                  root.appendChild(iframe);
                }

                fetch(contentUrl)
                  .then(async response => {
                    if (response.ok) {
                      const text = await response.text();
                      const pre = document.createElement('pre');
                      pre.textContent = text;
                      root.replaceChildren(pre);
                      return;
                    }

                    renderOriginal();
                  })
                  .catch(renderOriginal);
              </script>
            </body>
            </html>
            """;

        return Content(body, "text/html", Encoding.UTF8);
    }

    [HttpGet("/api/document-preview/{documentId:int}/content")]
    public async Task<IActionResult> ContentPreview(int documentId, CancellationToken cancellationToken)
    {
        var document = await context.MarkdownDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(document.ConvertedMarkdownPath)
            && artifactStore.Exists(document.ConvertedMarkdownPath))
        {
            var converted = await artifactStore.ReadConvertedMarkdownAsync(
                document.ConvertedMarkdownPath,
                cancellationToken);
            return Content(converted, "text/markdown", Encoding.UTF8);
        }

        if (!string.IsNullOrWhiteSpace(document.Content))
        {
            return Content(document.Content, "text/markdown", Encoding.UTF8);
        }

        return NotFound();
    }

    [HttpGet("/api/document-preview/{documentId:int}/original")]
    public async Task<IActionResult> OriginalArtifact(int documentId, CancellationToken cancellationToken)
    {
        var document = await context.MarkdownDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);

        if (document is null
            || string.IsNullOrWhiteSpace(document.OriginalFilePath)
            || !artifactStore.Exists(document.OriginalFilePath))
        {
            return NotFound();
        }

        var fileInfo = artifactStore.GetFileInfo(document.OriginalFilePath);
        if (!ContentTypeProvider.TryGetContentType(fileInfo.Name, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var stream = fileInfo.OpenRead();
        return File(stream, contentType, enableRangeProcessing: true);
    }
}
