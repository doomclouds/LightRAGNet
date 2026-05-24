# React UI Standardization and RAG Chat Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Standardize all React pages on the `dark-ops` design system and migrate RAG Chat from Blazor to a React two-column workbench with clickable document previews and preserved message-level diagnostics.

**Architecture:** Add a shared React theme layer and move existing React islands onto it before adding the new RAG Chat island. Backend query metadata will be enriched with safe reference preview URLs, and the RAG prompt will stop asking the LLM to render a final references section.

**Tech Stack:** .NET 10, ASP.NET Core controllers, EF Core SQLite, Blazor Server host pages, React 19, Vite, TypeScript, Vitest, CSS custom properties, lucide-react.

---

## Source Spec

- `docs/superpowers/specs/2026-05-24-react-ui-standardization-rag-chat-workbench-design.md`

## File Structure

### Shared React Design System

- Create `src/LightRAGNet.Web/ClientApp/src/styles/theme.css`
  - owns `dark-ops` CSS variables and reusable `.lrn-*` classes.
- Modify `src/LightRAGNet.Web/ClientApp/src/graph-workbench/main.tsx`
  - imports shared theme before graph CSS.
- Modify `src/LightRAGNet.Web/ClientApp/src/system-status/main.tsx`
  - imports shared theme before system status CSS.
- Modify `src/LightRAGNet.Web/ClientApp/src/cache-management/main.tsx`
  - imports shared theme before cache CSS.
- Modify React page CSS files:
  - `src/LightRAGNet.Web/ClientApp/src/styles/cache-management.css`
  - `src/LightRAGNet.Web/ClientApp/src/styles/graph-workbench.css`
  - `src/LightRAGNet.Web/ClientApp/src/styles/system-status.css`
  - new `src/LightRAGNet.Web/ClientApp/src/styles/rag-chat.css`

### Backend Reference Preview

- Modify `src/LightRAGNet.Share/Models/RagQueryEvent.cs`
  - extend `RagQueryReferenceDto`.
- Create `src/LightRAGNet.Server/Services/DocumentPreview/ReferenceOpenKind.cs`
  - enum-like constants for preview source types.
- Create `src/LightRAGNet.Server/Services/DocumentPreview/DocumentReferencePreviewResolver.cs`
  - resolves `ReferenceItem.FilePath` to safe `RagQueryReferenceDto`.
- Create `src/LightRAGNet.Server/Controllers/DocumentPreviewController.cs`
  - serves the new preview page and safe content endpoints.
- Modify `src/LightRAGNet.Server/Controllers/RagQueryController.cs`
  - injects resolver and sends enriched references in metadata events.
- Modify `src/LightRAGNet.Server/Program.cs`
  - registers the resolver service.
- Modify `src/LightRAGNet/LightRAG.cs`
  - removes model-generated references-section prompt rules.

### React RAG Chat

- Create `src/LightRAGNet.Web/ClientApp/src/types/ragChat.ts`
  - query request, query event, message, reference and retrieval-data types.
- Create `src/LightRAGNet.Web/ClientApp/src/api/ragChatApi.ts`
  - SSE query client and retrieval-data client.
- Create `src/LightRAGNet.Web/ClientApp/src/api/ragChatApi.test.ts`
  - Vitest coverage for request and SSE parsing.
- Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/main.tsx`
  - mount/unmount entry.
- Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/RagChatWorkbench.tsx`
  - page-level state and orchestration.
- Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/ChatPane.tsx`
  - message list and composer.
- Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/AssistantMessage.tsx`
  - assistant message rendering, references and details action.
- Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/QuerySettingsPanel.tsx`
  - query controls.
- Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/QueryDetailsDialog.tsx`
  - per-message diagnostics dialog.
- Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/ragChatSettings.ts`
  - request builder, keyword parser and defaults.
- Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/RagChatWorkbench.test.tsx`
  - component behavior tests.
- Modify `src/LightRAGNet.Web/ClientApp/package.json`
  - add markdown rendering dependencies when implementing assistant markdown rendering.
- Modify `src/LightRAGNet.Web/ClientApp/vite.config.ts`
  - add `ragChat` entry and output paths.

### Blazor Host And Tests

- Replace or simplify `src/LightRAGNet.Web/Components/Pages/RagChat.razor`
  - turns into a React host page for `/`.
- Remove or stop using `src/LightRAGNet.Web/Components/Pages/RagChat.razor.css`
  - React CSS becomes authoritative.
- Remove or stop using `src/LightRAGNet.Web/Components/Pages/RagChat.razor.js`
  - React owns scrolling.
- Keep `src/LightRAGNet.Web/Components/Pages/RagQueryDataDialog.razor` until React details dialog fully replaces it.
- Update tests:
  - `tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs`
  - `tests/LightRAGNet.Web.Tests/ApiClientQueryRagTests.cs` only if the Blazor `ApiClient` query path is retired.
  - `tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs`
  - new `tests/LightRAGNet.Server.Tests/DocumentReferencePreviewResolverTests.cs`
  - new `tests/LightRAGNet.Server.Tests/DocumentPreviewControllerTests.cs`
  - new `tests/LightRAGNet.Tests/Query/RagPromptReferenceContractTests.cs`

---

### Task 1: Shared `dark-ops` Theme Tokens

**Files:**
- Create: `src/LightRAGNet.Web/ClientApp/src/styles/theme.css`
- Modify: `src/LightRAGNet.Web/ClientApp/src/cache-management/main.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/graph-workbench/main.tsx`
- Modify: `src/LightRAGNet.Web/ClientApp/src/system-status/main.tsx`
- Test: `src/LightRAGNet.Web/ClientApp/src/styles/theme.test.ts`

- [ ] **Step 1: Write the failing theme source test**

Create `src/LightRAGNet.Web/ClientApp/src/styles/theme.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const themeCss = readFileSync(resolve(__dirname, "theme.css"), "utf8");

describe("dark-ops theme", () => {
  it("defines the shared semantic tokens used by React pages", () => {
    [
      "--app-bg",
      "--panel-bg",
      "--panel-border",
      "--text-primary",
      "--text-secondary",
      "--accent",
      "--danger",
      "--warning",
      "--success",
      "--control-bg",
      "--control-border",
      "--shadow-panel"
    ].forEach((token) => {
      expect(themeCss).toContain(token);
    });
  });

  it("defines reusable page primitives", () => {
    [
      ".lrn-app",
      ".lrn-page-head",
      ".lrn-panel",
      ".lrn-button",
      ".lrn-icon-button",
      ".lrn-input",
      ".lrn-chip",
      ".lrn-dialog",
      ".lrn-code-surface"
    ].forEach((className) => {
      expect(themeCss).toContain(className);
    });
  });
});
```

- [ ] **Step 2: Run the failing theme test**

Run:

```powershell
npm test -- --run src/styles/theme.test.ts
```

from `src/LightRAGNet.Web/ClientApp`.

Expected: fail because `theme.css` does not exist.

- [ ] **Step 3: Add `theme.css` with `dark-ops` tokens and primitives**

Create `src/LightRAGNet.Web/ClientApp/src/styles/theme.css`:

```css
:root {
  color-scheme: dark;
  --app-bg: #0d1117;
  --panel-bg: #151b23;
  --panel-bg-elevated: #1a222d;
  --panel-border: #303946;
  --text-primary: #edf2f7;
  --text-secondary: #a9b4c2;
  --text-muted: #7d8998;
  --accent: #4cc9f0;
  --accent-soft: rgba(76, 201, 240, .13);
  --accent-border: rgba(76, 201, 240, .42);
  --danger: #ff6b6b;
  --danger-soft: rgba(255, 107, 107, .13);
  --warning: #f6c85f;
  --warning-soft: rgba(246, 200, 95, .14);
  --success: #7bd88f;
  --success-soft: rgba(123, 216, 143, .13);
  --control-bg: #151b23;
  --control-border: #303946;
  --shadow-panel: 0 18px 42px rgba(0, 0, 0, .24);
  --radius-panel: 8px;
  --radius-control: 6px;
}

.lrn-app {
  min-height: 100vh;
  margin: 0;
  background: var(--app-bg);
  color: var(--text-primary);
  font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif;
  letter-spacing: 0;
}

.lrn-app *,
.lrn-app *::before,
.lrn-app *::after {
  box-sizing: border-box;
}

.lrn-app button,
.lrn-app input,
.lrn-app textarea,
.lrn-app select {
  font: inherit;
}

.lrn-page-head {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 18px;
  align-items: start;
  margin-bottom: 16px;
}

.lrn-page-head h1 {
  margin: 0 0 8px;
  font-size: 26px;
  line-height: 1.2;
  font-weight: 760;
}

.lrn-page-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  color: var(--text-secondary);
  font-size: 13px;
}

.lrn-chip {
  min-height: 26px;
  display: inline-flex;
  align-items: center;
  border: 1px solid var(--panel-border);
  border-radius: 999px;
  padding: 0 10px;
  background: var(--panel-bg);
  color: var(--text-secondary);
}

.lrn-panel,
.lrn-dialog {
  border: 1px solid var(--panel-border);
  border-radius: var(--radius-panel);
  background: var(--panel-bg);
  box-shadow: var(--shadow-panel);
}

.lrn-panel__head {
  display: flex;
  justify-content: space-between;
  gap: 14px;
  padding: 14px 16px;
  border-bottom: 1px solid var(--panel-border);
}

.lrn-panel__head h2 {
  margin: 0 0 4px;
  font-size: 16px;
  line-height: 1.3;
  font-weight: 760;
}

.lrn-panel__head p {
  margin: 0;
  color: var(--text-secondary);
  font-size: 13px;
  line-height: 1.45;
}

.lrn-button,
.lrn-icon-button {
  min-height: 36px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  border: 1px solid var(--control-border);
  border-radius: var(--radius-control);
  background: var(--control-bg);
  color: var(--text-primary);
  padding: 0 12px;
  font-weight: 700;
  cursor: pointer;
}

.lrn-button:disabled,
.lrn-icon-button:disabled {
  cursor: not-allowed;
  opacity: .58;
}

.lrn-button--accent {
  border-color: var(--accent-border);
  background: var(--accent-soft);
  color: #c7f3ff;
}

.lrn-button--danger {
  border-color: rgba(255, 107, 107, .45);
  background: var(--danger-soft);
  color: #ffd5d5;
}

.lrn-icon-button {
  width: 36px;
  padding: 0;
}

.lrn-input,
.lrn-textarea,
.lrn-select {
  width: 100%;
  min-height: 36px;
  border: 1px solid var(--control-border);
  border-radius: var(--radius-control);
  background: var(--control-bg);
  color: var(--text-primary);
  padding: 0 10px;
  outline: none;
}

.lrn-textarea {
  min-height: 88px;
  padding: 10px;
  resize: vertical;
}

.lrn-input:focus,
.lrn-textarea:focus,
.lrn-select:focus {
  border-color: var(--accent-border);
  box-shadow: 0 0 0 2px rgba(76, 201, 240, .13);
}

.lrn-code-surface {
  max-width: 100%;
  overflow: auto;
  border: 1px solid var(--panel-border);
  border-radius: var(--radius-control);
  background: #0a0f15;
  color: var(--text-primary);
  padding: 12px;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
```

- [ ] **Step 4: Import theme in each React entry**

Add this import before page CSS in:

- `src/LightRAGNet.Web/ClientApp/src/cache-management/main.tsx`
- `src/LightRAGNet.Web/ClientApp/src/graph-workbench/main.tsx`
- `src/LightRAGNet.Web/ClientApp/src/system-status/main.tsx`

```ts
import "../styles/theme.css";
```

- [ ] **Step 5: Run theme test and build**

Run:

```powershell
npm test -- --run src/styles/theme.test.ts
npm run build
```

Expected: test passes and Vite emits graph, system-status, cache-management assets.

- [ ] **Step 6: Commit**

```powershell
git add src/LightRAGNet.Web/ClientApp/src/styles/theme.css src/LightRAGNet.Web/ClientApp/src/styles/theme.test.ts src/LightRAGNet.Web/ClientApp/src/cache-management/main.tsx src/LightRAGNet.Web/ClientApp/src/graph-workbench/main.tsx src/LightRAGNet.Web/ClientApp/src/system-status/main.tsx src/LightRAGNet.Web/wwwroot
git commit -m "feat: add shared react dark ops theme"
```

---

### Task 2: Standardize Existing React Pages

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/src/styles/cache-management.css`
- Modify: `src/LightRAGNet.Web/ClientApp/src/styles/graph-workbench.css`
- Modify: `src/LightRAGNet.Web/ClientApp/src/styles/system-status.css`
- Test: `src/LightRAGNet.Web/ClientApp/src/styles/reactPageThemeUsage.test.ts`

- [ ] **Step 1: Write failing theme-usage source test**

Create `src/LightRAGNet.Web/ClientApp/src/styles/reactPageThemeUsage.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

function css(name: string): string {
  return readFileSync(resolve(__dirname, name), "utf8");
}

describe("React page styles use dark-ops tokens", () => {
  it("cache management uses shared tokens for core surfaces", () => {
    const source = css("cache-management.css");
    expect(source).toContain("var(--app-bg)");
    expect(source).toContain("var(--panel-bg)");
    expect(source).toContain("var(--panel-border)");
  });

  it("graph workbench no longer uses the old light shell colors", () => {
    const source = css("graph-workbench.css");
    expect(source).toContain("var(--app-bg)");
    expect(source).toContain("var(--panel-bg)");
    expect(source).not.toContain("background: #eef3f1;");
    expect(source).not.toContain("rgb(255 255 255 / 72%)");
  });

  it("system status no longer uses the old light shell colors", () => {
    const source = css("system-status.css");
    expect(source).toContain("var(--app-bg)");
    expect(source).toContain("var(--panel-bg)");
    expect(source).not.toContain("background: #f4f6f8;");
    expect(source).not.toContain("background: #fff;");
  });
});
```

- [ ] **Step 2: Run failing test**

```powershell
npm test -- --run src/styles/reactPageThemeUsage.test.ts
```

Expected: fails because Graph and System Status still use old light colors.

- [ ] **Step 3: Update Cache Management CSS to token aliases**

In `src/LightRAGNet.Web/ClientApp/src/styles/cache-management.css`, keep layout selectors but replace core colors:

```css
.cache-workbench {
  min-height: 100vh;
  margin: 0;
  background: var(--app-bg);
  color: var(--text-primary);
  font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif;
  letter-spacing: 0;
}

.cache-page-meta {
  color: var(--text-secondary);
}

.cache-page-meta span,
.cache-field input,
.cache-segmented,
.cache-button,
.cache-icon-button {
  border-color: var(--panel-border);
  background: var(--panel-bg);
  color: var(--text-primary);
}

.cache-metric-card,
.cache-panel {
  border-color: var(--panel-border);
  background: var(--panel-bg);
  box-shadow: var(--shadow-panel);
}
```

Keep existing cache-specific metric layout and responsive rules.

- [ ] **Step 4: Update Graph Workbench shell and panel colors**

In `src/LightRAGNet.Web/ClientApp/src/styles/graph-workbench.css`, replace light shell and floating panel surfaces with token-backed dark surfaces:

```css
.graph-workbench {
  box-sizing: border-box;
  min-height: calc(100vh - 64px);
  overflow: hidden;
  background: var(--app-bg);
  color: var(--text-primary);
  font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif;
}

.graph-workbench__canvas {
  position: relative;
  min-width: 0;
  overflow: hidden;
  background: var(--app-bg);
}

.graph-workbench__sigma {
  background:
    radial-gradient(circle at 50% 50%, rgba(21, 27, 35, .96) 0, rgba(13, 17, 23, .98) 58%, #090d12 100%);
}

.graph-workbench__query-card,
.graph-workbench__search-card,
.graph-workbench__control-dock,
.graph-workbench__properties,
.graph-workbench__legend,
.graph-workbench__layout-menu {
  border: 1px solid var(--panel-border);
  background: rgba(21, 27, 35, .94);
  box-shadow: var(--shadow-panel);
  backdrop-filter: blur(16px);
  color: var(--text-primary);
}

.graph-workbench__compact-field span,
.graph-workbench__field span {
  color: var(--text-secondary);
}

.graph-workbench__compact-field input,
.graph-workbench__field input,
.graph-workbench__field textarea,
.graph-workbench__search-card input {
  border-color: var(--control-border);
  background: var(--control-bg);
  color: var(--text-primary);
}
```

Then continue replacing old light `#172026`, `#53645d`, `rgb(255 255 255 / ...)`, and hover colors with `var(--text-primary)`, `var(--text-secondary)`, `var(--panel-bg-elevated)`, `var(--accent-soft)`, and `var(--accent-border)`.

- [ ] **Step 5: Update System Status to dark operations style**

In `src/LightRAGNet.Web/ClientApp/src/styles/system-status.css`, replace light shell and panels:

```css
.system-status {
  box-sizing: border-box;
  min-height: calc(100vh - 64px);
  background: var(--app-bg);
  color: var(--text-primary);
  font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif;
  padding: 18px;
}

.system-status__header h1,
.system-status__panel h2,
.system-status__panel h3,
.system-status__panel h4 {
  margin: 0;
  color: var(--text-primary);
  letter-spacing: 0;
}

.system-status__eyebrow,
.system-status__copy-message {
  color: var(--text-secondary);
}

.system-status__button {
  border: 1px solid var(--control-border);
  border-radius: var(--radius-control);
  background: var(--control-bg);
  color: var(--text-primary);
}

.system-status__button--primary {
  border-color: var(--accent-border);
  background: var(--accent-soft);
  color: #c7f3ff;
}

.system-status__panel {
  border: 1px solid var(--panel-border);
  border-radius: var(--radius-panel);
  background: var(--panel-bg);
  box-shadow: var(--shadow-panel);
}
```

Then update check rows, evidence blocks, priority cards, and raw JSON surfaces to use `--panel-bg-elevated`, `--text-secondary`, `--success-soft`, `--warning-soft`, and `--danger-soft`.

- [ ] **Step 6: Run style tests and full frontend tests**

```powershell
npm test -- --run src/styles/reactPageThemeUsage.test.ts src/styles/theme.test.ts
npm test
npm run build
```

Expected: tests pass and generated assets include graph, system-status and cache-management.

- [ ] **Step 7: Commit**

```powershell
git add src/LightRAGNet.Web/ClientApp/src/styles src/LightRAGNet.Web/wwwroot
git commit -m "style: align react pages with dark ops theme"
```

---

### Task 3: Backend Reference Preview Contract

**Files:**
- Modify: `src/LightRAGNet.Share/Models/RagQueryEvent.cs`
- Create: `src/LightRAGNet.Server/Services/DocumentPreview/ReferenceOpenKind.cs`
- Create: `src/LightRAGNet.Server/Services/DocumentPreview/DocumentReferencePreviewResolver.cs`
- Modify: `src/LightRAGNet.Server/Program.cs`
- Modify: `src/LightRAGNet.Server/Controllers/RagQueryController.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentReferencePreviewResolverTests.cs`
- Test: `tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs`

- [ ] **Step 1: Write failing DTO and resolver tests**

Create `tests/LightRAGNet.Server.Tests/DocumentReferencePreviewResolverTests.cs`:

```csharp
using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services.DocumentPreview;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LightRAGNet.Server.Tests;

public sealed class DocumentReferencePreviewResolverTests
{
    [Fact]
    public async Task ResolveAsync_UploadSource_ReturnsDocumentPreviewUrl()
    {
        await using var db = CreateDb();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = 11,
            FileName = "notes.md",
            FileUrl = "/uploads/notes.md",
            Content = "# Notes"
        });
        await db.SaveChangesAsync();

        var resolver = new DocumentReferencePreviewResolver(db);
        var request = CreateRequest();

        var result = await resolver.ResolveAsync(
            [new ReferenceItem { ReferenceId = "1", FilePath = "/uploads/notes.md" }],
            request,
            CancellationToken.None);

        var reference = result.Should().ContainSingle().Subject;
        reference.ReferenceId.Should().Be("1");
        reference.FileName.Should().Be("notes.md");
        reference.PreviewUrl.Should().Be("http://localhost/document-preview/11");
        reference.OpenKind.Should().Be(ReferenceOpenKind.DocumentPreview);
    }

    [Fact]
    public async Task ResolveAsync_UploadLogicalUri_ReturnsDocumentPreviewUrl()
    {
        await using var db = CreateDb();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = 12,
            FileName = "合同.pdf",
            OriginalFileName = "合同.pdf",
            FileUrl = "upload://track-a/%E5%90%88%E5%90%8C.pdf",
            OriginalFilePath = Path.Combine("documents", "12", "original.pdf")
        });
        await db.SaveChangesAsync();

        var resolver = new DocumentReferencePreviewResolver(db);

        var result = await resolver.ResolveAsync(
            [new ReferenceItem { ReferenceId = "2", FilePath = "upload://track-a/%E5%90%88%E5%90%8C.pdf" }],
            CreateRequest(),
            CancellationToken.None);

        var reference = result.Should().ContainSingle().Subject;
        reference.FileName.Should().Be("合同.pdf");
        reference.PreviewUrl.Should().Be("http://localhost/document-preview/12");
        reference.OpenKind.Should().Be(ReferenceOpenKind.OriginalArtifact);
    }

    [Fact]
    public async Task ResolveAsync_UnmatchedReference_ReturnsPlainReference()
    {
        await using var db = CreateDb();
        var resolver = new DocumentReferencePreviewResolver(db);

        var result = await resolver.ResolveAsync(
            [new ReferenceItem { ReferenceId = "3", FilePath = "../secrets.txt" }],
            CreateRequest(),
            CancellationToken.None);

        var reference = result.Should().ContainSingle().Subject;
        reference.FileName.Should().Be("secrets.txt");
        reference.PreviewUrl.Should().BeNull();
        reference.OpenKind.Should().Be(ReferenceOpenKind.ExternalOrUnresolved);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new AppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static HttpRequest CreateRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        return context.Request;
    }
}
```

Update `tests/LightRAGNet.Server.Tests/RagQueryRequestMapperTests.cs` with a DTO shape assertion:

```csharp
[Fact]
public void RagQueryReferenceDto_ExposesPreviewFields()
{
    typeof(RagQueryReferenceDto).GetProperty("FileName").Should().NotBeNull();
    typeof(RagQueryReferenceDto).GetProperty("PreviewUrl").Should().NotBeNull();
    typeof(RagQueryReferenceDto).GetProperty("OpenKind").Should().NotBeNull();
}
```

- [ ] **Step 2: Run failing tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "DocumentReferencePreviewResolverTests|RagQueryReferenceDto_ExposesPreviewFields" --no-restore --verbosity minimal
```

Expected: fail because DTO and resolver do not exist.

- [ ] **Step 3: Extend `RagQueryReferenceDto`**

Modify `src/LightRAGNet.Share/Models/RagQueryEvent.cs`:

```csharp
public sealed class RagQueryReferenceDto
{
    public string ReferenceId { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string? PreviewUrl { get; init; }
    public string OpenKind { get; init; } = "ExternalOrUnresolved";
}
```

- [ ] **Step 4: Add `ReferenceOpenKind` constants**

Create `src/LightRAGNet.Server/Services/DocumentPreview/ReferenceOpenKind.cs`:

```csharp
namespace LightRAGNet.Server.Services.DocumentPreview;

public static class ReferenceOpenKind
{
    public const string UploadedFile = nameof(UploadedFile);
    public const string DocumentPreview = nameof(DocumentPreview);
    public const string ConvertedMarkdown = nameof(ConvertedMarkdown);
    public const string OriginalArtifact = nameof(OriginalArtifact);
    public const string ExternalOrUnresolved = nameof(ExternalOrUnresolved);
}
```

- [ ] **Step 5: Add resolver implementation**

Create `src/LightRAGNet.Server/Services/DocumentPreview/DocumentReferencePreviewResolver.cs`:

```csharp
using LightRAGNet.Core.Models;
using LightRAGNet.Server.Data;
using LightRAGNet.Share.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LightRAGNet.Server.Services.DocumentPreview;

public sealed class DocumentReferencePreviewResolver(AppDbContext context)
{
    private sealed record PreviewDocument(
        int Id,
        string? FileName,
        string? OriginalFileName,
        string? FileUrl,
        string? OriginalFilePath,
        string? ConvertedMarkdownPath);

    public async Task<IReadOnlyList<RagQueryReferenceDto>> ResolveAsync(
        IReadOnlyList<ReferenceItem> references,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(request);

        if (references.Count == 0)
        {
            return [];
        }

        var documents = await context.MarkdownDocuments
            .AsNoTracking()
            .Select(document => new PreviewDocument(
                document.Id,
                document.FileName,
                document.OriginalFileName,
                document.FileUrl,
                document.OriginalFilePath,
                document.ConvertedMarkdownPath))
            .ToListAsync(cancellationToken);

        return references
            .Select(reference => ResolveReference(reference, documents, request))
            .ToList();
    }

    private static RagQueryReferenceDto ResolveReference(
        ReferenceItem reference,
        IEnumerable<PreviewDocument> documents,
        HttpRequest request)
    {
        var normalizedReference = Normalize(reference.FilePath);
        var document = documents.FirstOrDefault(candidate => Matches(candidate, normalizedReference));

        if (document is null)
        {
            return new RagQueryReferenceDto
            {
                ReferenceId = reference.ReferenceId,
                FilePath = reference.FilePath,
                FileName = ExtractDisplayName(reference.FilePath),
                OpenKind = ReferenceOpenKind.ExternalOrUnresolved
            };
        }

        return new RagQueryReferenceDto
        {
            ReferenceId = reference.ReferenceId,
            FilePath = reference.FilePath,
            FileName = SelectFileName(document.FileName, document.OriginalFileName),
            PreviewUrl = BuildPreviewUrl(request, (int)document.Id),
            OpenKind = SelectOpenKind(document.FileUrl, document.OriginalFilePath, document.ConvertedMarkdownPath)
        };
    }

    private static bool Matches(dynamic document, string normalizedReference)
    {
        return MatchesValue(document.FileUrl, normalizedReference)
            || MatchesValue(document.FileName, normalizedReference)
            || MatchesValue(document.OriginalFileName, normalizedReference)
            || MatchesValue(document.OriginalFilePath, normalizedReference)
            || MatchesValue(document.ConvertedMarkdownPath, normalizedReference);
    }

    private static bool MatchesValue(string? value, string normalizedReference)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalizedValue = Normalize(value);
        return string.Equals(normalizedValue, normalizedReference, StringComparison.OrdinalIgnoreCase)
            || normalizedReference.EndsWith("/" + normalizedValue, StringComparison.OrdinalIgnoreCase)
            || normalizedReference.EndsWith("\\" + normalizedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string SelectFileName(string fileName, string? originalFileName)
    {
        return string.IsNullOrWhiteSpace(originalFileName)
            ? fileName
            : originalFileName;
    }

    private static string SelectOpenKind(string? fileUrl, string? originalFilePath, string? convertedMarkdownPath)
    {
        if (!string.IsNullOrWhiteSpace(convertedMarkdownPath))
        {
            return ReferenceOpenKind.ConvertedMarkdown;
        }

        if (!string.IsNullOrWhiteSpace(originalFilePath))
        {
            return ReferenceOpenKind.OriginalArtifact;
        }

        if (!string.IsNullOrWhiteSpace(fileUrl) && fileUrl.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceOpenKind.UploadedFile;
        }

        return ReferenceOpenKind.DocumentPreview;
    }

    private static string BuildPreviewUrl(HttpRequest request, int documentId)
    {
        return $"{request.Scheme}://{request.Host}/document-preview/{documentId}";
    }

    private static string Normalize(string value)
    {
        return Uri.UnescapeDataString(value.Replace('\\', '/').Trim());
    }

    private static string ExtractDisplayName(string value)
    {
        var normalized = Normalize(value);
        var trimmed = normalized.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 && lastSlash + 1 < trimmed.Length
            ? trimmed[(lastSlash + 1)..]
            : trimmed;
    }
}
```

- [ ] **Step 6: Register resolver**

Modify `src/LightRAGNet.Server/Program.cs`:

```csharp
using LightRAGNet.Server.Services.DocumentPreview;
```

and add:

```csharp
builder.Services.AddScoped<DocumentReferencePreviewResolver>();
```

- [ ] **Step 7: Enrich query metadata in controller**

Modify `RagQueryController` constructor to inject resolver:

```csharp
public class RagQueryController(
    LightRAG lightRAG,
    DocumentReferencePreviewResolver referencePreviewResolver,
    ILogger<RagQueryController> logger) : ControllerBase
```

Change `WrapQueryResultAsEventsAsync` signature:

```csharp
private static async IAsyncEnumerable<RagQueryEvent> WrapQueryResultAsEventsAsync(
    RagQueryRequest request,
    QueryResult queryResult,
    DocumentReferencePreviewResolver referencePreviewResolver,
    HttpRequest httpRequest,
    [EnumeratorCancellation] CancellationToken cancellationToken)
```

Replace metadata yield with:

```csharp
var references = request.IncludeReferences
    ? await referencePreviewResolver.ResolveAsync(queryResult.ReferenceList, httpRequest, cancellationToken)
        .ConfigureAwait(false)
    : [];

yield return RagQueryRequestMapper.ToMetadataEvent(request, queryResult, references);
```

Update `RagQueryRequestMapper.ToMetadataEvent` signature:

```csharp
public static QueryMetadataEvent ToMetadataEvent(
    RagQueryRequest request,
    QueryResult result,
    IReadOnlyList<RagQueryReferenceDto>? references = null)
```

and use:

```csharp
References = request.IncludeReferences ? references ?? result.ReferenceList.Select(ToReferenceDto).ToArray() : [],
```

Update `ToReferenceDto` to populate fallback fields:

```csharp
private static RagQueryReferenceDto ToReferenceDto(ReferenceItem item)
{
    return new RagQueryReferenceDto
    {
        ReferenceId = item.ReferenceId,
        FilePath = item.FilePath,
        FileName = Path.GetFileName(item.FilePath.Replace('\\', '/')),
        OpenKind = "ExternalOrUnresolved"
    };
}
```

- [ ] **Step 8: Run tests**

```powershell
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "DocumentReferencePreviewResolverTests|RagQueryRequestMapperTests|RagQueryControllerTests" --no-restore --verbosity minimal
```

Expected: all selected tests pass.

- [ ] **Step 9: Commit**

```powershell
git add src/LightRAGNet.Share/Models/RagQueryEvent.cs src/LightRAGNet.Server/Services/DocumentPreview src/LightRAGNet.Server/Program.cs src/LightRAGNet.Server/Controllers/RagQueryController.cs src/LightRAGNet.Server/Services/RagQueryRequestMapper.cs tests/LightRAGNet.Server.Tests
git commit -m "feat: enrich rag references with preview metadata"
```

---

### Task 4: Document Preview Routes And Prompt Cleanup

**Files:**
- Create: `src/LightRAGNet.Server/Controllers/DocumentPreviewController.cs`
- Modify: `src/LightRAGNet/LightRAG.cs`
- Test: `tests/LightRAGNet.Server.Tests/DocumentPreviewControllerTests.cs`
- Test: `tests/LightRAGNet.Tests/Query/RagPromptReferenceContractTests.cs`

- [ ] **Step 1: Write failing prompt cleanup test**

Create `tests/LightRAGNet.Tests/Query/RagPromptReferenceContractTests.cs`:

```csharp
using FluentAssertions;

namespace LightRAGNet.Tests.Query;

public sealed class RagPromptReferenceContractTests
{
    [Fact]
    public void LightRagPrompt_DoesNotRequireModelGeneratedReferencesSection()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet", "LightRAG.cs");

        source.Should().NotContain("### References");
        source.Should().NotContain("References Section Format");
        source.Should().NotContain("Reference list entries should adhere to the format");
        source.Should().NotContain("Do not generate anything after the reference section");
        source.Should().Contain("DO NOT invent");
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
```

- [ ] **Step 2: Write failing preview controller source test**

Create `tests/LightRAGNet.Server.Tests/DocumentPreviewControllerTests.cs`:

```csharp
using FluentAssertions;

namespace LightRAGNet.Server.Tests;

public sealed class DocumentPreviewControllerTests
{
    [Fact]
    public void DocumentPreviewController_ExposesPreviewAndContentRoutes()
    {
        var source = ReadRepositoryFile("src", "LightRAGNet.Server", "Controllers", "DocumentPreviewController.cs");

        source.Should().Contain("[Route(\"document-preview\")]");
        source.Should().Contain("[HttpGet(\"{documentId:int}\")]");
        source.Should().Contain("[HttpGet(\"/api/document-preview/{documentId:int}/content\")]");
        source.Should().Contain("[HttpGet(\"/api/document-preview/{documentId:int}/original\")]");
        source.Should().Contain("IDocumentArtifactStore");
        source.Should().Contain("FileExtensionContentTypeProvider");
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
```

- [ ] **Step 3: Run failing tests**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "RagPromptReferenceContractTests" --no-restore --verbosity minimal
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "DocumentPreviewControllerTests" --no-restore --verbosity minimal
```

Expected: prompt test fails because old prompt still contains references instructions; controller test fails because controller is absent.

- [ ] **Step 4: Add `DocumentPreviewController`**

Create `src/LightRAGNet.Server/Controllers/DocumentPreviewController.cs`:

```csharp
using System.Net;
using System.Text;
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
        var body = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{title}}</title>
              <style>
                :root { color-scheme: dark; }
                body { margin: 0; background: #0d1117; color: #edf2f7; font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif; }
                header { padding: 16px 20px; border-bottom: 1px solid #303946; background: #151b23; }
                h1 { margin: 0; font-size: 20px; }
                main { padding: 16px 20px; }
                iframe { width: 100%; height: calc(100vh - 96px); border: 1px solid #303946; border-radius: 8px; background: #151b23; }
                pre { white-space: pre-wrap; overflow-wrap: anywhere; border: 1px solid #303946; border-radius: 8px; background: #151b23; padding: 16px; }
                a { color: #c7f3ff; }
              </style>
            </head>
            <body>
              <header><h1>{{title}}</h1></header>
              <main id="preview-root">Loading...</main>
              <script>
                const root = document.getElementById('preview-root');
                const contentUrl = '/api/document-preview/{{documentId}}/content';
                const originalUrl = '/api/document-preview/{{documentId}}/original';
                fetch(contentUrl)
                  .then(async response => {
                    if (response.ok) {
                      const text = await response.text();
                      root.innerHTML = '';
                      const pre = document.createElement('pre');
                      pre.textContent = text;
                      root.appendChild(pre);
                      return;
                    }
                    root.innerHTML = '<iframe title="Document preview" src="' + originalUrl + '"></iframe>';
                  })
                  .catch(() => {
                    root.innerHTML = '<iframe title="Document preview" src="' + originalUrl + '"></iframe>';
                  });
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

        if (!string.IsNullOrWhiteSpace(document.ConvertedMarkdownPath) &&
            artifactStore.Exists(document.ConvertedMarkdownPath))
        {
            var converted = await artifactStore.ReadConvertedMarkdownAsync(document.ConvertedMarkdownPath, cancellationToken);
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

        if (document is null || string.IsNullOrWhiteSpace(document.OriginalFilePath) || !artifactStore.Exists(document.OriginalFilePath))
        {
            return NotFound();
        }

        var fileInfo = artifactStore.GetFileInfo(document.OriginalFilePath);
        if (!ContentTypeProvider.TryGetContentType(fileInfo.Name, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var stream = fileInfo.OpenRead();
        return File(stream, contentType, fileDownloadName: fileInfo.Name, enableRangeProcessing: true);
    }
}
```

- [ ] **Step 5: Clean prompt references section**

Modify `BuildRAGResponsePrompt` in `src/LightRAGNet/LightRAG.cs`.

Replace the reference-heavy instructions with this shorter contract:

```csharp
                1. Step-by-Step Instruction:
                  - Carefully determine the user's query intent in the context of the conversation history to fully understand the user's information need.
                  - Scrutinize both `Knowledge Graph Data` (Entity and Relationship) and `Document Chunks` in the **Context**. The Knowledge Graph Data uses concise text format: entities as "Name (Type): Description" and relationships as "Source -> Target: Keywords - Description". Document Chunks include reference identifiers for system use.
                  - Identify and extract all pieces of information that are directly relevant to answering the user query.
                  - Weave the extracted facts into a coherent and logical response. Your own knowledge must ONLY be used to formulate fluent sentences and connect ideas, NOT to introduce any external information.
                  - Do not invent citations, file paths, links, or source names. Source references are rendered separately by the system UI from structured metadata.

                2. Content & Grounding:
                  - Strictly adhere to the provided context from the **Context**; DO NOT invent, assume, or infer any information not explicitly stated.
                  - If the answer cannot be found in the **Context**, state that you do not have enough information to answer. Do not attempt to guess.

                3. Formatting & Language:
                  - The response MUST be in the same language as the user query.
                  - The response MUST utilize Markdown formatting for enhanced clarity and structure (e.g., headings, bold text, bullet points).
                  - The response should be presented in {responseType}.

                4. Additional Instructions: {userPrompt}
```

- [ ] **Step 6: Run prompt and preview tests**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "RagPromptReferenceContractTests" --no-restore --verbosity minimal
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "DocumentPreviewControllerTests|DocumentReferencePreviewResolverTests|RagQueryControllerTests" --no-restore --verbosity minimal
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src/LightRAGNet.Server/Controllers/DocumentPreviewController.cs src/LightRAGNet/LightRAG.cs tests/LightRAGNet.Server.Tests/DocumentPreviewControllerTests.cs tests/LightRAGNet.Tests/Query/RagPromptReferenceContractTests.cs
git commit -m "feat: add document preview and clean rag references prompt"
```

---

### Task 5: React RAG Chat API And State Model

**Files:**
- Create: `src/LightRAGNet.Web/ClientApp/src/types/ragChat.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/api/ragChatApi.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/api/ragChatApi.test.ts`
- Create: `src/LightRAGNet.Web/ClientApp/src/rag-chat/ragChatSettings.ts`
- Test: `src/LightRAGNet.Web/ClientApp/src/rag-chat/ragChatSettings.test.ts`

- [ ] **Step 1: Write failing settings tests**

Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/ragChatSettings.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { buildRagQueryRequest, defaultQuerySettings, parseKeywords } from "./ragChatSettings";

describe("rag chat settings", () => {
  it("parses comma and newline separated keywords", () => {
    expect(parseKeywords("alpha, beta\nalpha，gamma")).toEqual(["alpha", "beta", "gamma"]);
  });

  it("builds a request and disables references in bypass mode", () => {
    const request = buildRagQueryRequest("hello", {
      ...defaultQuerySettings,
      mode: "Bypass",
      includeReferences: true,
      highLevelKeywordsText: "system",
      lowLevelKeywordsText: "queue",
      debugOutputMode: "PromptOnly"
    });

    expect(request).toMatchObject({
      query: "hello",
      mode: "Bypass",
      includeReferences: false,
      onlyNeedContext: false,
      onlyNeedPrompt: true,
      highLevelKeywords: ["system"],
      lowLevelKeywords: ["queue"]
    });
  });
});
```

- [ ] **Step 2: Write failing API tests**

Create `src/LightRAGNet.Web/ClientApp/src/api/ragChatApi.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";
import { queryRagStream } from "./ragChatApi";
import type { RagQueryRequest } from "../types/ragChat";

function createSseResponse(events: string[]): Response {
  const body = events.map((event) => `data: ${event}\n\n`).join("");
  return new Response(body, {
    status: 200,
    headers: { "content-type": "text/event-stream" }
  });
}

describe("ragChatApi", () => {
  it("posts query request and emits chunks and metadata", async () => {
    const fetchMock = vi.fn().mockResolvedValue(createSseResponse([
      JSON.stringify({ type: "text_chunk", chunk: "hello" }),
      JSON.stringify({
        type: "metadata",
        mode: "Mix",
        stream: true,
        includeReferences: true,
        responseType: "Multiple Paragraphs",
        cachePolicy: "Streaming request",
        references: [{ referenceId: "1", fileName: "doc.md", filePath: "/uploads/doc.md", previewUrl: "http://localhost/document-preview/1", openKind: "DocumentPreview" }],
        highLevelKeywords: ["system"],
        lowLevelKeywords: ["queue"],
        diagnostics: { query_mode: "Mix" }
      }),
      JSON.stringify({ type: "done" })
    ]));

    const chunks: string[] = [];
    let metadataMode = "";
    const request: RagQueryRequest = {
      query: "hello",
      mode: "Mix",
      stream: true,
      includeReferences: true,
      responseType: "Multiple Paragraphs",
      topK: 40,
      chunkTopK: 20,
      enableRerank: true,
      highLevelKeywords: [],
      lowLevelKeywords: [],
      onlyNeedContext: false,
      onlyNeedPrompt: false
    };

    await queryRagStream("http://localhost", request, {
      fetchImpl: fetchMock,
      onChunk: (chunk) => chunks.push(chunk),
      onMetadata: (metadata) => {
        metadataMode = metadata.mode;
      }
    });

    expect(fetchMock).toHaveBeenCalledWith("http://localhost/api/RagQuery/query", expect.objectContaining({ method: "POST" }));
    expect(chunks).toEqual(["hello"]);
    expect(metadataMode).toBe("Mix");
  });
});
```

- [ ] **Step 3: Run failing tests**

```powershell
npm test -- --run src/rag-chat/ragChatSettings.test.ts src/api/ragChatApi.test.ts
```

Expected: fail because files do not exist.

- [ ] **Step 4: Add RAG chat types**

Create `src/LightRAGNet.Web/ClientApp/src/types/ragChat.ts`:

```ts
export type QueryMode = "Local" | "Global" | "Hybrid" | "Naive" | "Mix" | "Bypass";
export type DebugOutputMode = "Answer" | "ContextOnly" | "PromptOnly";

export type RagQueryRequest = {
  query: string;
  mode: QueryMode;
  stream: boolean;
  includeReferences: boolean;
  responseType: string;
  topK: number;
  chunkTopK: number;
  enableRerank: boolean;
  highLevelKeywords: string[];
  lowLevelKeywords: string[];
  onlyNeedContext: boolean;
  onlyNeedPrompt: boolean;
};

export type RagQueryReference = {
  referenceId: string;
  filePath: string;
  fileName: string;
  previewUrl?: string | null;
  openKind: string;
};

export type QueryMetadataEvent = {
  type: "metadata";
  mode: QueryMode;
  stream: boolean;
  includeReferences: boolean;
  responseType: string;
  cachePolicy: string;
  references: RagQueryReference[];
  highLevelKeywords: string[];
  lowLevelKeywords: string[];
  diagnostics: Record<string, string>;
};

export type RagQueryEvent =
  | { type: "text_chunk"; chunk: string }
  | { type: "error"; error: string; message?: string | null }
  | { type: "done" }
  | QueryMetadataEvent;

export type RagQueryDataResponse = {
  status: string;
  message: string;
  data: Record<string, unknown>;
  metadata: Record<string, unknown>;
};

export type ChatMessage = {
  id: string;
  role: "User" | "Assistant";
  text: string;
  request?: RagQueryRequest;
  metadata?: QueryMetadataEvent;
  retrievalData?: RagQueryDataResponse;
  isComplete: boolean;
  isStreaming: boolean;
  isLoadingRetrievalData: boolean;
  errorMessage?: string;
};
```

- [ ] **Step 5: Add settings builder**

Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/ragChatSettings.ts`:

```ts
import type { DebugOutputMode, QueryMode, RagQueryRequest } from "../types/ragChat";

export type QuerySettings = {
  mode: QueryMode;
  streamResponse: boolean;
  includeReferences: boolean;
  enableRerank: boolean;
  topK: number;
  chunkTopK: number;
  responseType: string;
  highLevelKeywordsText: string;
  lowLevelKeywordsText: string;
  debugOutputMode: DebugOutputMode;
};

export const defaultQuerySettings: QuerySettings = {
  mode: "Mix",
  streamResponse: true,
  includeReferences: true,
  enableRerank: true,
  topK: 40,
  chunkTopK: 20,
  responseType: "Multiple Paragraphs",
  highLevelKeywordsText: "",
  lowLevelKeywordsText: "",
  debugOutputMode: "Answer"
};

export function parseKeywords(value: string): string[] {
  const seen = new Set<string>();
  return value
    .split(/[,\n\r，]/)
    .map((item) => item.trim())
    .filter(Boolean)
    .filter((item) => {
      const key = item.toLocaleLowerCase();
      if (seen.has(key)) {
        return false;
      }
      seen.add(key);
      return true;
    });
}

export function buildRagQueryRequest(query: string, settings: QuerySettings): RagQueryRequest {
  const isBypass = settings.mode === "Bypass";
  return {
    query,
    mode: settings.mode,
    stream: settings.streamResponse,
    includeReferences: !isBypass && settings.includeReferences,
    responseType: settings.responseType,
    topK: settings.topK,
    chunkTopK: settings.chunkTopK,
    enableRerank: settings.enableRerank,
    highLevelKeywords: parseKeywords(settings.highLevelKeywordsText),
    lowLevelKeywords: parseKeywords(settings.lowLevelKeywordsText),
    onlyNeedContext: settings.debugOutputMode === "ContextOnly",
    onlyNeedPrompt: settings.debugOutputMode === "PromptOnly"
  };
}
```

- [ ] **Step 6: Add SSE API client**

Create `src/LightRAGNet.Web/ClientApp/src/api/ragChatApi.ts`:

```ts
import type { QueryMetadataEvent, RagQueryDataResponse, RagQueryEvent, RagQueryRequest } from "../types/ragChat";

type QueryHandlers = {
  fetchImpl?: typeof fetch;
  signal?: AbortSignal;
  onChunk?: (chunk: string) => void;
  onMetadata?: (metadata: QueryMetadataEvent) => void;
};

export async function queryRagStream(apiBase: string, request: RagQueryRequest, handlers: QueryHandlers = {}): Promise<void> {
  const fetchImpl = handlers.fetchImpl ?? fetch;
  const response = await fetchImpl(`${apiBase}/api/RagQuery/query`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(request),
    signal: handlers.signal
  });

  if (!response.ok) {
    throw new Error(`Query failed: ${response.status} ${response.statusText}`);
  }

  if (!response.body) {
    throw new Error("Query response body is empty.");
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) {
      break;
    }

    buffer += decoder.decode(value, { stream: true });
    const parts = buffer.split("\n\n");
    buffer = parts.pop() ?? "";

    for (const part of parts) {
      const event = parseSsePart(part);
      if (!event) {
        continue;
      }

      if (event.type === "text_chunk") {
        handlers.onChunk?.(event.chunk);
      } else if (event.type === "metadata") {
        handlers.onMetadata?.(event);
      } else if (event.type === "error") {
        throw new Error(event.message || event.error);
      }
    }
  }
}

export async function getRagQueryData(apiBase: string, request: RagQueryRequest, signal?: AbortSignal): Promise<RagQueryDataResponse> {
  const response = await fetch(`${apiBase}/api/RagQuery/data`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(request),
    signal
  });

  if (!response.ok) {
    throw new Error(`Query data failed: ${response.status} ${response.statusText}`);
  }

  return await response.json() as RagQueryDataResponse;
}

function parseSsePart(part: string): RagQueryEvent | null {
  const dataLines = part
    .split("\n")
    .map((line) => line.trimEnd())
    .filter((line) => line.startsWith("data:"))
    .map((line) => line.slice("data:".length).trimStart());

  if (dataLines.length === 0) {
    return null;
  }

  return JSON.parse(dataLines.join("\n")) as RagQueryEvent;
}
```

- [ ] **Step 7: Run tests**

```powershell
npm test -- --run src/rag-chat/ragChatSettings.test.ts src/api/ragChatApi.test.ts
```

Expected: tests pass.

- [ ] **Step 8: Commit**

```powershell
git add src/LightRAGNet.Web/ClientApp/src/types/ragChat.ts src/LightRAGNet.Web/ClientApp/src/api/ragChatApi.ts src/LightRAGNet.Web/ClientApp/src/api/ragChatApi.test.ts src/LightRAGNet.Web/ClientApp/src/rag-chat/ragChatSettings.ts src/LightRAGNet.Web/ClientApp/src/rag-chat/ragChatSettings.test.ts
git commit -m "feat: add react rag chat api model"
```

---

### Task 6: React RAG Chat Workbench UI

**Files:**
- Create: `src/LightRAGNet.Web/ClientApp/src/rag-chat/main.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/rag-chat/RagChatWorkbench.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/rag-chat/ChatPane.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/rag-chat/AssistantMessage.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/rag-chat/QuerySettingsPanel.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/rag-chat/QueryDetailsDialog.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/rag-chat/RagChatWorkbench.test.tsx`
- Create: `src/LightRAGNet.Web/ClientApp/src/styles/rag-chat.css`
- Modify: `src/LightRAGNet.Web/ClientApp/package.json`

- [ ] **Step 1: Add markdown and test DOM dependencies**

Run from `src/LightRAGNet.Web/ClientApp`:

```powershell
npm install react-markdown remark-gfm
npm install -D happy-dom
```

Expected: `package.json` and `package-lock.json` update.

- [ ] **Step 2: Write failing workbench component tests**

Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/RagChatWorkbench.test.tsx`:

```tsx
// @vitest-environment happy-dom

import React from "react";
import { describe, expect, it, vi } from "vitest";
import { createRoot } from "react-dom/client";
import { act } from "react";
import { RagChatWorkbench } from "./RagChatWorkbench";

describe("RagChatWorkbench", () => {
  it("renders chat pane, settings panel, composer and details action area", async () => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);

    await act(async () => {
      root.render(<RagChatWorkbench apiBase="http://localhost" />);
    });

    expect(host.textContent).toContain("RAG Chat");
    expect(host.textContent).toContain("Query settings");
    expect(host.textContent).toContain("Mode");
    expect(host.textContent).toContain("References");
    expect(host.querySelector("[data-testid='rag-chat-composer']")).not.toBeNull();

    root.unmount();
    host.remove();
  });

  it("renders preview links with new-tab attributes", async () => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);

    await act(async () => {
      root.render(<RagChatWorkbench apiBase="http://localhost" initialAssistantReferenceUrl="http://localhost/document-preview/1" />);
    });

    const link = host.querySelector("a[href='http://localhost/document-preview/1']");
    expect(link).not.toBeNull();
    expect(link?.getAttribute("target")).toBe("_blank");
    expect(link?.getAttribute("rel")).toBe("noreferrer");

    root.unmount();
    host.remove();
  });
});
```

- [ ] **Step 3: Run failing test**

```powershell
npm test -- --run src/rag-chat/RagChatWorkbench.test.tsx
```

Expected: fail because components do not exist.

- [ ] **Step 4: Add `main.tsx` mount entry**

Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/main.tsx`:

```tsx
import React from "react";
import { createRoot, type Root } from "react-dom/client";
import "../styles/theme.css";
import "../styles/rag-chat.css";
import { RagChatWorkbench } from "./RagChatWorkbench";

const roots = new Map<string, Root>();

export function mountRagChat(rootElementId: string, apiBase: string): void {
  const element = document.getElementById(rootElementId);
  if (!element) {
    return;
  }

  const root = createRoot(element);
  roots.set(rootElementId, root);
  root.render(<RagChatWorkbench apiBase={apiBase} />);
}

export function unmountRagChat(rootElementId: string): void {
  roots.get(rootElementId)?.unmount();
  roots.delete(rootElementId);
}
```

- [ ] **Step 5: Add page component**

Create `src/LightRAGNet.Web/ClientApp/src/rag-chat/RagChatWorkbench.tsx`:

```tsx
import { useCallback, useMemo, useRef, useState } from "react";
import { queryRagStream } from "../api/ragChatApi";
import type { ChatMessage, RagQueryReference } from "../types/ragChat";
import { AssistantMessage } from "./AssistantMessage";
import { ChatPane } from "./ChatPane";
import { QueryDetailsDialog } from "./QueryDetailsDialog";
import { QuerySettingsPanel } from "./QuerySettingsPanel";
import { buildRagQueryRequest, defaultQuerySettings, type QuerySettings } from "./ragChatSettings";

type Props = {
  apiBase: string;
  initialAssistantReferenceUrl?: string;
};

function createId(): string {
  return typeof crypto !== "undefined" && "randomUUID" in crypto
    ? crypto.randomUUID()
    : `msg-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

export function RagChatWorkbench({ apiBase, initialAssistantReferenceUrl }: Props) {
  const [settings, setSettings] = useState<QuerySettings>(defaultQuerySettings);
  const [input, setInput] = useState("");
  const [messages, setMessages] = useState<ChatMessage[]>(() => createInitialMessages(initialAssistantReferenceUrl));
  const [activeDetailsMessageId, setActiveDetailsMessageId] = useState<string | null>(null);
  const [isRunning, setIsRunning] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  const activeDetailsMessage = useMemo(
    () => messages.find((message) => message.id === activeDetailsMessageId) ?? null,
    [activeDetailsMessageId, messages]
  );

  const send = useCallback(async () => {
    const query = input.trim();
    if (!query || isRunning) {
      return;
    }

    const request = buildRagQueryRequest(query, settings);
    const userMessage: ChatMessage = {
      id: createId(),
      role: "User",
      text: query,
      isComplete: true,
      isStreaming: false,
      isLoadingRetrievalData: false
    };
    const assistantMessage: ChatMessage = {
      id: createId(),
      role: "Assistant",
      text: "",
      request,
      isComplete: false,
      isStreaming: request.stream,
      isLoadingRetrievalData: false
    };

    setInput("");
    setMessages((current) => [...current, userMessage, assistantMessage]);
    setIsRunning(true);
    abortRef.current = new AbortController();

    try {
      await queryRagStream(apiBase, request, {
        signal: abortRef.current.signal,
        onChunk: (chunk) => {
          setMessages((current) => current.map((message) =>
            message.id === assistantMessage.id ? { ...message, text: message.text + chunk } : message));
        },
        onMetadata: (metadata) => {
          setMessages((current) => current.map((message) =>
            message.id === assistantMessage.id ? { ...message, metadata } : message));
        }
      });
      setMessages((current) => current.map((message) =>
        message.id === assistantMessage.id ? { ...message, isComplete: true } : message));
    } catch (error) {
      const message = error instanceof Error ? error.message : "Query failed.";
      setMessages((current) => current.map((item) =>
        item.id === assistantMessage.id
          ? { ...item, isComplete: true, errorMessage: message, text: item.text || `Error: ${message}` }
          : item));
    } finally {
      setIsRunning(false);
      abortRef.current = null;
    }
  }, [apiBase, input, isRunning, settings]);

  return (
    <main className="rag-chat lrn-app">
      <div className="rag-chat__inner">
        <header className="lrn-page-head rag-chat__head">
          <div>
            <h1>RAG Chat</h1>
            <div className="lrn-page-meta">
              <span>React workbench</span>
              <span>{settings.mode}</span>
            </div>
          </div>
          <button className="lrn-button lrn-button--danger" type="button" disabled={isRunning} onClick={() => setMessages([])}>
            Clear History
          </button>
        </header>

        <div className="rag-chat__layout">
          <ChatPane
            input={input}
            isRunning={isRunning}
            messages={messages}
            onInputChange={setInput}
            onOpenDetails={(message) => setActiveDetailsMessageId(message.id)}
            onSend={() => void send()}
          />
          <QuerySettingsPanel settings={settings} disabled={isRunning} onChange={setSettings} />
        </div>
      </div>

      {activeDetailsMessage && (
        <QueryDetailsDialog
          apiBase={apiBase}
          message={activeDetailsMessage}
          onClose={() => setActiveDetailsMessageId(null)}
          onUpdateMessage={(updated) => setMessages((current) => current.map((message) => message.id === updated.id ? updated : message))}
        />
      )}
    </main>
  );
}

function createInitialMessages(initialAssistantReferenceUrl?: string): ChatMessage[] {
  if (!initialAssistantReferenceUrl) {
    return [];
  }

  const reference: RagQueryReference = {
    referenceId: "1",
    filePath: "/uploads/doc.md",
    fileName: "doc.md",
    previewUrl: initialAssistantReferenceUrl,
    openKind: "DocumentPreview"
  };

  return [{
    id: "initial-assistant",
    role: "Assistant",
    text: "Preview reference",
    metadata: {
      type: "metadata",
      mode: "Mix",
      stream: false,
      includeReferences: true,
      responseType: "Multiple Paragraphs",
      cachePolicy: "Cacheable request",
      references: [reference],
      highLevelKeywords: [],
      lowLevelKeywords: [],
      diagnostics: {}
    },
    isComplete: true,
    isStreaming: false,
    isLoadingRetrievalData: false
  }];
}
```

- [ ] **Step 6: Add focused child components**

Create the following components with these responsibilities:

`ChatPane.tsx`:

```tsx
import type { ChatMessage } from "../types/ragChat";
import { AssistantMessage } from "./AssistantMessage";

type Props = {
  messages: ChatMessage[];
  input: string;
  isRunning: boolean;
  onInputChange: (value: string) => void;
  onSend: () => void;
  onOpenDetails: (message: ChatMessage) => void;
};

export function ChatPane({ messages, input, isRunning, onInputChange, onSend, onOpenDetails }: Props) {
  return (
    <section className="rag-chat__chat lrn-panel">
      <div className="rag-chat__messages">
        {messages.length === 0 && <div className="rag-chat__empty">Start chatting with RAG</div>}
        {messages.map((message) => message.role === "Assistant"
          ? <AssistantMessage key={message.id} message={message} onOpenDetails={() => onOpenDetails(message)} />
          : <div key={message.id} className="rag-chat__message rag-chat__message--user">{message.text}</div>)}
      </div>
      <div className="rag-chat__composer" data-testid="rag-chat-composer">
        <textarea
          className="lrn-textarea rag-chat__input"
          value={input}
          disabled={isRunning}
          placeholder="Enter your question..."
          onChange={(event) => onInputChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              onSend();
            }
          }}
        />
        <button className="lrn-button lrn-button--accent" type="button" disabled={isRunning || !input.trim()} onClick={onSend}>
          Send
        </button>
      </div>
    </section>
  );
}
```

`AssistantMessage.tsx`:

```tsx
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { ChatMessage } from "../types/ragChat";

type Props = {
  message: ChatMessage;
  onOpenDetails: () => void;
};

export function AssistantMessage({ message, onOpenDetails }: Props) {
  const metadata = message.metadata;
  return (
    <article className="rag-chat__message rag-chat__message--assistant">
      <ReactMarkdown remarkPlugins={[remarkGfm]}>{message.text || (message.isComplete ? "No content returned." : "")}</ReactMarkdown>
      {!message.isComplete && <div className="rag-chat__loading">Generating...</div>}
      {message.errorMessage && <div className="rag-chat__error">{message.errorMessage}</div>}
      {metadata && (
        <div className="rag-chat__message-meta">
          <span className="lrn-chip">{metadata.mode}</span>
          <span className="lrn-chip">{metadata.stream ? "Streaming" : "Cacheable"}</span>
        </div>
      )}
      {metadata?.references.length ? (
        <div className="rag-chat__references">
          {metadata.references.map((reference) => reference.previewUrl ? (
            <a key={reference.referenceId} href={reference.previewUrl} target="_blank" rel="noreferrer">
              {reference.fileName || reference.filePath}
            </a>
          ) : (
            <span key={reference.referenceId}>{reference.fileName || reference.filePath}</span>
          ))}
        </div>
      ) : null}
      {message.isComplete && message.request && (
        <button className="lrn-button" type="button" onClick={onOpenDetails}>
          View query details
        </button>
      )}
    </article>
  );
}
```

`QuerySettingsPanel.tsx`:

```tsx
import type { QueryMode } from "../types/ragChat";
import type { QuerySettings } from "./ragChatSettings";

type Props = {
  settings: QuerySettings;
  disabled: boolean;
  onChange: (settings: QuerySettings) => void;
};

const modes: QueryMode[] = ["Mix", "Naive", "Bypass", "Local", "Global", "Hybrid"];
const responseTypes = ["Multiple Paragraphs", "Single Paragraph", "Bullet Points", "Concise"];

export function QuerySettingsPanel({ settings, disabled, onChange }: Props) {
  return (
    <aside className="rag-chat__settings lrn-panel">
      <div className="lrn-panel__head">
        <div>
          <h2>Query settings</h2>
          <p>Settings for the next message</p>
        </div>
      </div>
      <div className="rag-chat__settings-body">
        <label>Mode<select className="lrn-select" disabled={disabled} value={settings.mode} onChange={(event) => onChange({ ...settings, mode: event.target.value as QueryMode })}>{modes.map((mode) => <option key={mode}>{mode}</option>)}</select></label>
        <label>Response<select className="lrn-select" disabled={disabled} value={settings.responseType} onChange={(event) => onChange({ ...settings, responseType: event.target.value })}>{responseTypes.map((item) => <option key={item}>{item}</option>)}</select></label>
        <label><input type="checkbox" disabled={disabled} checked={settings.streamResponse} onChange={(event) => onChange({ ...settings, streamResponse: event.target.checked })} /> Streaming</label>
        <label><input type="checkbox" disabled={disabled || settings.mode === "Bypass"} checked={settings.includeReferences} onChange={(event) => onChange({ ...settings, includeReferences: event.target.checked })} /> References</label>
        <label><input type="checkbox" disabled={disabled || settings.mode === "Bypass"} checked={settings.enableRerank} onChange={(event) => onChange({ ...settings, enableRerank: event.target.checked })} /> Rerank</label>
        <label>TopK<input className="lrn-input" type="number" min={1} max={200} disabled={disabled || settings.mode === "Bypass"} value={settings.topK} onChange={(event) => onChange({ ...settings, topK: Number(event.target.value) })} /></label>
        <label>ChunkTopK<input className="lrn-input" type="number" min={1} max={200} disabled={disabled || settings.mode === "Bypass"} value={settings.chunkTopK} onChange={(event) => onChange({ ...settings, chunkTopK: Number(event.target.value) })} /></label>
        <label>High keywords<input className="lrn-input" disabled={disabled || settings.mode === "Bypass"} value={settings.highLevelKeywordsText} onChange={(event) => onChange({ ...settings, highLevelKeywordsText: event.target.value })} /></label>
        <label>Low keywords<input className="lrn-input" disabled={disabled || settings.mode === "Bypass"} value={settings.lowLevelKeywordsText} onChange={(event) => onChange({ ...settings, lowLevelKeywordsText: event.target.value })} /></label>
      </div>
    </aside>
  );
}
```

`QueryDetailsDialog.tsx`:

```tsx
import { getRagQueryData } from "../api/ragChatApi";
import type { ChatMessage } from "../types/ragChat";

type Props = {
  apiBase: string;
  message: ChatMessage;
  onClose: () => void;
  onUpdateMessage: (message: ChatMessage) => void;
};

export function QueryDetailsDialog({ apiBase, message, onClose, onUpdateMessage }: Props) {
  const loadRetrievalData = async () => {
    if (!message.request || message.retrievalData) {
      return;
    }

    const retrievalData = await getRagQueryData(apiBase, message.request);
    onUpdateMessage({ ...message, retrievalData });
  };

  return (
    <div className="rag-chat__dialog-backdrop" role="dialog" aria-modal="true">
      <div className="rag-chat__dialog lrn-dialog">
        <div className="lrn-panel__head">
          <div>
            <h2>Query details</h2>
            <p>Request, metadata, retrieval data and raw diagnostics.</p>
          </div>
          <button className="lrn-icon-button" type="button" onClick={onClose}>×</button>
        </div>
        <div className="rag-chat__dialog-body">
          <button className="lrn-button lrn-button--accent" type="button" onClick={() => void loadRetrievalData()}>Load retrieval data</button>
          <h3>Request</h3>
          <pre className="lrn-code-surface">{JSON.stringify(message.request, null, 2)}</pre>
          <h3>Metadata</h3>
          <pre className="lrn-code-surface">{JSON.stringify(message.metadata, null, 2)}</pre>
          <h3>Retrieval Data</h3>
          <pre className="lrn-code-surface">{JSON.stringify(message.retrievalData ?? { message: "Not loaded" }, null, 2)}</pre>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 7: Add RAG chat CSS**

Create `src/LightRAGNet.Web/ClientApp/src/styles/rag-chat.css` with token-backed layout:

```css
.rag-chat {
  min-height: 100vh;
}

.rag-chat__inner {
  width: min(1420px, 100%);
  margin: 0 auto;
  padding: 20px 24px 28px;
}

.rag-chat__layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(300px, 360px);
  gap: 14px;
  min-height: calc(100vh - 132px);
}

.rag-chat__chat {
  min-width: 0;
  display: grid;
  grid-template-rows: minmax(0, 1fr) auto;
  overflow: hidden;
}

.rag-chat__messages {
  display: grid;
  align-content: start;
  gap: 12px;
  min-height: 0;
  overflow-y: auto;
  padding: 14px;
}

.rag-chat__message {
  max-width: min(780px, 88%);
  border: 1px solid var(--panel-border);
  border-radius: var(--radius-panel);
  padding: 12px;
  overflow-wrap: anywhere;
}

.rag-chat__message--user {
  justify-self: end;
  border-color: var(--accent-border);
  background: var(--accent-soft);
}

.rag-chat__message--assistant {
  justify-self: start;
  background: var(--panel-bg-elevated);
}

.rag-chat__message-meta,
.rag-chat__references {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 10px;
}

.rag-chat__references a,
.rag-chat__references span {
  color: #c7f3ff;
  border: 1px solid var(--panel-border);
  border-radius: 999px;
  background: var(--panel-bg);
  padding: 4px 9px;
  font-size: 13px;
  text-decoration: none;
}

.rag-chat__composer {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 10px;
  border-top: 1px solid var(--panel-border);
  padding: 12px;
}

.rag-chat__settings {
  min-width: 0;
  overflow: hidden;
}

.rag-chat__settings-body {
  display: grid;
  gap: 12px;
  padding: 14px;
}

.rag-chat__settings-body label {
  display: grid;
  gap: 6px;
  color: var(--text-secondary);
  font-size: 12px;
  font-weight: 800;
}

.rag-chat__dialog-backdrop {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: grid;
  place-items: center;
  background: rgba(0, 0, 0, .58);
  padding: 22px;
}

.rag-chat__dialog {
  width: min(980px, 100%);
  max-height: min(820px, 94vh);
  overflow: hidden;
}

.rag-chat__dialog-body {
  display: grid;
  gap: 12px;
  max-height: calc(94vh - 84px);
  overflow: auto;
  padding: 14px;
}

@media (max-width: 900px) {
  .rag-chat__layout {
    grid-template-columns: 1fr;
  }
}
```

- [ ] **Step 8: Run workbench tests and build**

```powershell
npm test -- --run src/rag-chat/RagChatWorkbench.test.tsx src/rag-chat/ragChatSettings.test.ts src/api/ragChatApi.test.ts
```

Expected: tests pass. Full Vite emission for the RAG Chat entry happens in Task 7 after the entry is wired into `vite.config.ts`.

- [ ] **Step 9: Commit**

```powershell
git add src/LightRAGNet.Web/ClientApp/package.json src/LightRAGNet.Web/ClientApp/package-lock.json src/LightRAGNet.Web/ClientApp/src/rag-chat src/LightRAGNet.Web/ClientApp/src/styles/rag-chat.css
git commit -m "feat: add react rag chat workbench"
```

---

### Task 7: Vite And Blazor Host Integration

**Files:**
- Modify: `src/LightRAGNet.Web/ClientApp/vite.config.ts`
- Modify: `src/LightRAGNet.Web/Components/Pages/RagChat.razor`
- Modify: `tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs`
- Modify: `src/LightRAGNet.Web/wwwroot`

- [ ] **Step 1: Write failing host source test**

Update `tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs` to replace old MudBlazor component assertions with React host assertions:

```csharp
[Fact]
public void RagChat_HostsReactWorkbench()
{
    var source = ReadRepositoryFile("src", "LightRAGNet.Web", "Components", "Pages", "RagChat.razor");

    source.Should().Contain("@page \"/\"");
    source.Should().Contain("rag-chat/assets/rag-chat.css");
    source.Should().Contain("rag-chat-root");
    source.Should().Contain("./rag-chat/assets/rag-chat.js");
    source.Should().Contain("mountRagChat");
    source.Should().Contain("unmountRagChat");
    source.Should().Contain("ApiBase");
}

[Fact]
public void ViteConfig_EmitsRagChatEntry()
{
    var source = ReadRepositoryFile("src", "LightRAGNet.Web", "ClientApp", "vite.config.ts");

    source.Should().Contain("ragChat");
    source.Should().Contain("src/rag-chat/main.tsx");
    source.Should().Contain("rag-chat/assets/rag-chat.js");
    source.Should().Contain("rag-chat/assets/rag-chat.css");
}
```

Remove assertions that require `_querySettings`, `MudSelect`, `RagQueryDataDialog`, and `RagChat.razor.css`.

- [ ] **Step 2: Run failing host tests**

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "RagChatSourceTests" --no-restore --verbosity minimal
```

Expected: fails because Blazor page still contains old implementation and Vite lacks `ragChat`.

- [ ] **Step 3: Add Vite rag chat entry**

Modify `src/LightRAGNet.Web/ClientApp/vite.config.ts`:

```ts
if (existsSync("src/rag-chat/main.tsx")) {
  input.ragChat = "src/rag-chat/main.tsx";
}
```

Add output mapping:

```ts
if (chunkInfo.name === "ragChat") {
  return "rag-chat/assets/rag-chat.js";
}
```

Add CSS mapping:

```ts
if (normalizedAssetNames.some((name) => name.includes("rag-chat") || name.includes("ragchat"))) {
  return "rag-chat/assets/rag-chat.css";
}
```

- [ ] **Step 4: Replace `RagChat.razor` with React host**

Replace `src/LightRAGNet.Web/Components/Pages/RagChat.razor` with:

```razor
@page "/"
@using Microsoft.JSInterop
@implements IAsyncDisposable
@inject IConfiguration Configuration
@inject IJSRuntime JSRuntime

<PageTitle>RAG Chat</PageTitle>

<link rel="stylesheet" href="rag-chat/assets/rag-chat.css" />

<div id="rag-chat-root" data-api-base="@ApiBase"></div>

@code {
    private const string RootElementId = "rag-chat-root";
    private IJSObjectReference? ragChatModule;
    private string ApiBase => Configuration["ApiBaseUrl"] ?? "http://localhost:5261";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        ragChatModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./rag-chat/assets/rag-chat.js");
        await ragChatModule.InvokeVoidAsync("mountRagChat", RootElementId, ApiBase);
    }

    public async ValueTask DisposeAsync()
    {
        if (ragChatModule is null)
        {
            return;
        }

        try
        {
            await ragChatModule.InvokeVoidAsync("unmountRagChat", RootElementId);
            await ragChatModule.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit can be gone while Blazor is tearing down the page.
        }
        catch (ObjectDisposedException)
        {
            // The JS runtime may already be disposed during host shutdown.
        }
    }
}
```

- [ ] **Step 5: Build assets**

```powershell
npm run build
```

Expected: generated assets include:

- `../wwwroot/rag-chat/assets/rag-chat.css`
- `../wwwroot/rag-chat/assets/rag-chat.js`

- [ ] **Step 6: Run host and frontend tests**

```powershell
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "RagChatSourceTests|GraphWorkbenchHostSourceTests|SystemStatusHostSourceTests|CacheManagementHostSourceTests" --no-restore --verbosity minimal
npm test
npm run build
```

Expected: tests pass and build succeeds.

- [ ] **Step 7: Commit**

```powershell
git add src/LightRAGNet.Web/ClientApp/vite.config.ts src/LightRAGNet.Web/Components/Pages/RagChat.razor tests/LightRAGNet.Web.Tests/RagChatSourceTests.cs src/LightRAGNet.Web/wwwroot
git commit -m "feat: host react rag chat workbench"
```

---

### Task 8: Verification, Visual QA, And Closeout Assets

**Files:**
- Create: `docs/superpowers/archives/2026-05/2026-05-24-react-ui-standardization-rag-chat-workbench-archives.md`
- Modify: `docs/superpowers/archives/INDEX.md`
- Optional create/update: `docs/superpowers/problems/2026-05/*.md` only if implementation reveals a reusable failure mode.

- [ ] **Step 1: Run backend targeted tests**

```powershell
dotnet test .\tests\LightRAGNet.Tests\LightRAGNet.Tests.csproj --filter "RagPromptReferenceContractTests|QueryCache|DocumentProcessingServiceTests|DescriptionMergerTests" --no-restore --verbosity minimal
dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "DocumentReferencePreviewResolverTests|DocumentPreviewControllerTests|RagQueryControllerTests|RagQueryRequestMapperTests|CacheManagement" --no-restore --verbosity minimal
dotnet test .\tests\LightRAGNet.Web.Tests\LightRAGNet.Web.Tests.csproj --filter "RagChatSourceTests|GraphWorkbenchHostSourceTests|SystemStatusHostSourceTests|CacheManagementHostSourceTests" --no-restore --verbosity minimal
```

Expected: all selected tests pass.

- [ ] **Step 2: Run frontend tests and build**

```powershell
npm test
npm run build
```

from `src/LightRAGNet.Web/ClientApp`.

Expected: all Vitest tests pass and Vite build emits all four React entries.

- [ ] **Step 3: Run diff and conflict checks**

```powershell
git diff --check
rg -n "^(<<<<<<<|=======|>>>>>>>)" src tests docs
```

Expected:

- `git diff --check` has exit code 0.
- conflict marker search returns no matches.

- [ ] **Step 4: Run visual verification**

Start the apps in separate terminals or background sessions:

```powershell
dotnet run --project src/LightRAGNet.Server
dotnet run --project src/LightRAGNet.Web
```

Use Playwright or manual browser checks for:

- `/` RAG Chat at desktop width.
- `/` RAG Chat at narrow width.
- `/graph-view` controls and properties panel.
- `/system-status` dark operations readability.
- `/cache-management` still visually stable.
- a document preview route for a Markdown/text document.
- a document preview route for a converted PDF/DOCX document if local sample data exists.

Expected:

- no unreadable dark text.
- no overlapping controls.
- no button text overflow.
- chat references are clickable when `previewUrl` exists.
- query details dialog raw JSON is readable.
- graph controls remain usable above the canvas.

- [ ] **Step 5: Run full solution test**

```powershell
dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal
```

Expected:

- Record exact result.
- If the known Neo4j source-string test still fails, verify it is the same unrelated failure before closeout.

- [ ] **Step 6: Archive completed requirement**

Create `docs/superpowers/archives/2026-05/2026-05-24-react-ui-standardization-rag-chat-workbench-archives.md`:

```markdown
# React UI Standardization and RAG Chat Workbench Archive

- Date: 2026-05-24
- Topic slug: `react-ui-standardization-rag-chat-workbench`
- Status: Completed

## Delivered

- Shared `dark-ops` React theme tokens.
- Existing React pages aligned to the shared dark operations style.
- React RAG Chat Workbench hosted at `/`.
- Message-level query details preserved in React.
- Safe reference preview metadata in RAG query metadata.
- New-tab document preview routes.
- RAG prompt no longer requires model-generated final references sections.

## Verification

Copy the exact evidence gathered in Task 8 Steps 1-5 before committing this archive.
Do not commit the archive while any evidence line is missing the command, result, and date.

- Backend targeted tests:
- Web host tests:
- Frontend tests/build:
- Visual verification:
- Full solution:

## Notes

- `dark-ops` is the only implemented skin in this phase.
- Theme switching UI is intentionally deferred.
```

Update `docs/superpowers/archives/INDEX.md` under `2026-05`:

```markdown
- [2026-05-24-react-ui-standardization-rag-chat-workbench-archives.md](./2026-05/2026-05-24-react-ui-standardization-rag-chat-workbench-archives.md): 统一 React 页面到 `dark-ops` 设计标准，并将 RAG Chat 迁移为带安全引用预览和消息级诊断的 React workbench。
```

- [ ] **Step 7: Validate assets**

```powershell
$env:PYTHONIOENCODING='utf-8'
python 'C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\archive-superpowers-feature\scripts\validate_archive_asset.py' docs\superpowers\archives\2026-05\2026-05-24-react-ui-standardization-rag-chat-workbench-archives.md
python 'C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\check_indexes.py' .
python 'C:\Users\10062\.codex\plugins\cache\local-home\superpowers-asset-compounding\0.1.4\skills\compound-development-asset\scripts\check_completion_gate.py' . --completed-topic "react-ui-standardization-rag-chat-workbench" --json
```

Expected: archive validation, index check, and completion gate pass.

- [ ] **Step 8: Commit closeout**

```powershell
git add docs/superpowers/archives docs/superpowers/problems
git commit -m "docs: archive react chat workbench standardization"
```

---

## Plan Self-Review

- Spec coverage:
  - Shared `dark-ops` token skin: Task 1.
  - Existing React page standardization: Task 2.
  - RAG Chat React migration: Tasks 5, 6, 7.
  - Safe reference preview contract: Tasks 3, 4.
  - PDF/DOCX artifact preview: Task 4.
  - Prompt references cleanup: Task 4.
  - Message-level diagnostics dialog: Task 6.
  - Verification and archive: Task 8.
- Completion scan:
  - The plan avoids deferred implementation markers and undefined task references.
  - Archive evidence is intentionally described as a pre-commit requirement in Task 8 instead of fake sample output.
- Type consistency:
  - `RagQueryReferenceDto.FileName`, `PreviewUrl`, and `OpenKind` are introduced in Task 3 and used by React in Tasks 5 and 6.
  - `mountRagChat` and `unmountRagChat` are introduced in Task 6 and used by Blazor host in Task 7.
  - `dark-ops` tokens are introduced in Task 1 and consumed by pages in Tasks 2 and 6.
