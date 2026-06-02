# Repository Guidelines

## Project Structure & Module Organization

LightRAGNet is a multi-project .NET 10 solution in `LightRAGNet.slnx`. Production .NET projects live under `src/`, and test projects live under `tests/`.

Core contracts, interfaces, IO helpers, tokenizer assets, and shared models live in `src/LightRAGNet.Core/` and `src/LightRAGNet.Share/`. The main RAG orchestration library is `src/LightRAGNet/`, with service areas under `Services/DocumentDeletion`, `Services/DocumentLifecycle`, `Services/DocumentProcessing`, `Services/GraphCuration`, `Services/KnowledgeGraphMerge`, `Services/Query`, `Services/QueryCache`, `Services/RetrievalContext`, `Services/TaskQueue`, and `Services/Utilities`. Provider implementations are split into `src/LightRAGNet.LLM/`, `src/LightRAGNet.Embedding/`, `src/LightRAGNet.Rerank/`, and `src/LightRAGNet.Storage/`. `src/LightRAGNet.Hosting/` contains dependency-injection composition.

`src/LightRAGNet.Server/` is the ASP.NET Core API host, including controllers, SignalR hubs, EF Core migrations, SQLite-backed document metadata, document intake/preview services, system health, cache management, graph APIs, and RAGAS/evaluation endpoints.

`src/LightRAGNet.React/` is the standalone React/Vite workbench app, with API clients under `src/api`, routing and shell code under `src/app`, feature modules for documents, document preview, RAG chat, graph workbench, cache management, and system status under `src/features`, shared components/styles under `src/shared`, stores under `src/stores`, and Vitest suites under `tests/`. `src/LightRAGNet.Example/` contains sample usage and local example code. Project knowledge assets live under `docs/superpowers/`, including specs, plans, archives, problems, inbox notes, and visual artifacts.

## Build, Test, and Development Commands

- `dotnet restore LightRAGNet.slnx` restores NuGet packages.
- `dotnet build LightRAGNet.slnx` builds all projects.
- `docker compose up -d` starts Qdrant and Neo4j for local RAG storage.
- `dotnet run --project src/LightRAGNet.Server` runs the API server.
- `dotnet test LightRAGNet.slnx` runs the .NET test projects.
- `Push-Location src/LightRAGNet.React; npm ci; npm test; npm run build; Pop-Location` restores, tests, and builds the standalone React app.

## Coding Style & Naming Conventions

Use C# with nullable reference types and implicit usings enabled. Follow standard .NET naming: `PascalCase` for public types and members, `camelCase` for locals and parameters, and interface names prefixed with `I`. Keep services focused by provider or pipeline stage; prefer dependency injection through constructors. Use 4-space indentation in C# and Razor files. Run `dotnet format LightRAGNet.slnx` before broad formatting-only changes.

## Testing Guidelines

Core behavior tests live under `tests/LightRAGNet.Tests/`; server host and API-oriented tests live under `tests/LightRAGNet.Server.Tests/`; standalone React unit and integration tests live under `src/LightRAGNet.React/tests/`. Name test files after the subject under test, for example `RagTaskQueueServiceTests.cs` or `RagChatWorkbench.test.tsx`. Prefer xUnit-style `MethodName_State_ExpectedResult` test names for .NET tests and focused Vitest suites for React behavior, API clients, stores, and design-system guardrails.

Server/API tests must not use real developer databases or external RAG storage by default. Isolate Qdrant, Neo4j, hosted background workers, and destructive clear-all paths behind test doubles, no-op cleaners, temporary stores, or explicit opt-in integration tests with uniquely owned resources. A full `dotnet test` run must never delete or mutate local development Qdrant/Neo4j data.

## Commit & Pull Request Guidelines

Recent history uses short English imperative messages such as `Add docker-compose.yml...`, `Fix: ...`, and `Refactor ...`. Keep that style, but avoid vague subjects like `Remove` or `Delete`; prefer `fix: correct rag task notification timing` or `refactor: simplify http client setup`. Pull requests should include a concise summary, verification commands run, linked issues when available, and screenshots or recordings for React UI changes.

## Security & Configuration Tips

Do not commit real API keys in `appsettings*.json`. Keep local credentials in user secrets, environment variables, or untracked development settings. Review `docker-compose.yml` before sharing because it contains machine-specific volume paths and default Neo4j credentials.

<!-- asset-compounding-guidance:start -->
## Asset Compounding Retrieval Guide

This repository uses hook-assisted asset compounding from the `superpowers-asset-compounding` plugin. Keep this `AGENTS.md` block as repository-specific retrieval anchors only; generic routing, plan-boundary checkpoints, closeout reminders, and `asset_gate` nudges belong to the plugin hooks and skills.

If the plugin was just installed or upgraded, review and trust the bundled hooks with `/hooks` before relying on lifecycle automation.

### Asset Directories

- Specs: `docs/superpowers/specs/`
- Plans: `docs/superpowers/plans/`
- Archives: `docs/superpowers/archives/`
- Problems: `docs/superpowers/problems/`
- Inbox: `docs/superpowers/inbox/`

If one of these directories does not exist, do not assume there is no asset. Search the existing directories first, then inspect current code and tests before guessing.

### Retrieval Order

When continuing feature work, explaining prior decisions, or checking whether a requirement is already delivered:

1. Search `docs/superpowers/specs/` and `docs/superpowers/plans/` for the intended behavior and implementation plan.
2. Search `docs/superpowers/archives/` for completed delivery history.
3. Search `docs/superpowers/problems/` for stable reusable failure modes, root causes, and recovery rules.
4. Search `docs/superpowers/inbox/` for uncertain but possibly reusable signals.
5. If no asset answers the question, inspect current code and tests before guessing.

Preferred keyword search:

```powershell
rg -n "<topic-keyword>" docs/superpowers/specs docs/superpowers/plans docs/superpowers/archives docs/superpowers/problems docs/superpowers/inbox
```

### Hook-Owned Workflow

- `SessionStart` injects a short asset protocol when `docs/superpowers/` exists.
- `PostToolUse` records compact signals from edits, verification, git closeout commands, and main-agent plan updates.
- `Stop` may request one more pass when meaningful work lacks an `asset_gate`.
- `PreCompact` / `PostCompact` preserve pending asset signals across compaction.

Subagent lifecycle hooks are intentionally not used for asset compounding. The main agent owns final route decisions and repository asset writes. Use the plugin skills and scripts when the hook-provided context indicates an archive, problem, inbox, or update is needed.
<!-- asset-compounding-guidance:end -->
