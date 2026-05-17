# Repository Guidelines

## Project Structure & Module Organization

LightRAGNet is a multi-project .NET 10 solution in `LightRAGNet.slnx`. Core contracts and shared models live in `LightRAGNet.Core/` and `LightRAGNet.Share/`. The main RAG orchestration code is in `LightRAGNet/`, with service areas under `Services/DocumentProcessing`, `Services/KnowledgeGraphMerge`, `Services/RetrievalContext`, and `Services/TaskQueue`. Provider implementations are split into `LightRAGNet.LLM/`, `LightRAGNet.Embedding/`, `LightRAGNet.Rerank/`, and `LightRAGNet.Storage/`. `LightRAGNet.Hosting/` contains dependency-injection setup. `LightRAGNet.Server/` is the ASP.NET Core API, SignalR hub, EF Core migrations, and SQLite-backed document metadata service. `LightRAGNet.Web/` is the Blazor Server UI with MudBlazor components and static assets under `wwwroot/`. `LightRAGNet.Example/` contains sample usage and local skill examples.

## Build, Test, and Development Commands

- `dotnet restore LightRAGNet.slnx` restores NuGet packages.
- `dotnet build LightRAGNet.slnx` builds all projects.
- `docker compose up -d` starts Qdrant and Neo4j for local RAG storage.
- `dotnet run --project LightRAGNet.Server` runs the API server.
- `dotnet run --project LightRAGNet.Web` runs the Blazor UI.
- `dotnet test LightRAGNet.slnx` is the expected test command once test projects are added.

## Coding Style & Naming Conventions

Use C# with nullable reference types and implicit usings enabled. Follow standard .NET naming: `PascalCase` for public types and members, `camelCase` for locals and parameters, and interface names prefixed with `I`. Keep services focused by provider or pipeline stage; prefer dependency injection through constructors. Use 4-space indentation in C# and Razor files. Run `dotnet format LightRAGNet.slnx` before broad formatting-only changes.

## Testing Guidelines

No dedicated test project is currently present. Add tests under a sibling project such as `LightRAGNet.Tests/` or `LightRAGNet.Server.Tests/`, and name test files after the subject under test, for example `RagTaskQueueServiceTests.cs`. Prefer xUnit-style `MethodName_State_ExpectedResult` test names and cover queue processing, retrieval strategy behavior, storage adapters, and API contracts.

## Commit & Pull Request Guidelines

Recent history uses short English imperative messages such as `Add docker-compose.yml...`, `Fix: ...`, and `Refactor ...`. Keep that style, but avoid vague subjects like `Remove` or `Delete`; prefer `fix: correct rag task notification timing` or `refactor: simplify http client setup`. Pull requests should include a concise summary, verification commands run, linked issues when available, and screenshots or recordings for Blazor UI changes.

## Security & Configuration Tips

Do not commit real API keys in `appsettings*.json`. Keep local credentials in user secrets, environment variables, or untracked development settings. Review `docker-compose.yml` before sharing because it contains machine-specific volume paths and default Neo4j credentials.

<!-- asset-compounding-guidance:start -->
## Asset Compounding Retrieval Guide

This section is managed by `compound-development-asset`. Keep generic asset-compounding workflow rules in the skill system; keep repository-specific retrieval anchors here.

### Asset Directories

- Specs: `docs/superpowers/specs/`
- Plans: `docs/superpowers/plans/`
- Archives: `docs/superpowers/archives/`
- Problems: `docs/superpowers/problems/`
- Inbox: `docs/superpowers/inbox/`

If one of these directories does not exist, do not assume there is no asset. Search the existing directories first, then decide whether the missing area should be created.

### Retrieval Order

When continuing feature work, explaining prior decisions, or checking whether a requirement is already delivered:

1. Search `docs/superpowers/specs/` and `docs/superpowers/plans/` for the intended behavior and implementation plan.
2. Search `docs/superpowers/archives/` for completed delivery history.
3. Search `docs/superpowers/problems/` for reusable failure modes, root causes, and recovery rules.
4. Search `docs/superpowers/inbox/` for uncertain but possibly reusable signals that have not been promoted yet.
5. If no asset answers the question, inspect current code and tests before guessing.

Preferred keyword search:

```powershell
rg -n "<topic-keyword>" docs/superpowers/specs docs/superpowers/plans docs/superpowers/archives docs/superpowers/problems docs/superpowers/inbox
```

### Script-Assisted Checks

When `compound-development-asset` and `write-superpowers-problem` are available, prefer bundled scripts for deterministic checks:

- `find_related_assets.py`: find matching specs, plans, archives, problems, and inbox notes before creating a new asset.
- `suggest_asset_route.py`: get a first-pass route suggestion: `none`, `inbox`, `update-existing`, `new-problem`, `archive`, or `both`.
- `check_indexes.py`: validate archive/problem/inbox index order, dead links, duplicate entries, and orphan files.
- `archive-superpowers-feature/scripts/validate_archive_asset.py`: validate formal archive assets.
- `write-superpowers-problem/scripts/validate_problem_asset.py`: validate formal problem assets and inbox notes.
- `write-superpowers-problem/scripts/inspect_inbox_lifecycle.py`: inspect related inbox lifecycle status and revisit candidates.

Scripts provide evidence, not final authority. Use the output to reduce misses and duplicates, then make the final routing decision with project context.

### Routing Boundaries

- Use `inbox` for uncertain but potentially reusable signals.
- Update an existing problem/archive when the new learning belongs to the same feature or failure class.
- Treat user validation feedback, CI/release warnings, installer/artifact warnings, and hosted automation deprecations as asset signals; update a related asset if one exists, otherwise park the signal in inbox.
- Create a new problem only for a stable, distinct failure mode with root-cause evidence and recognition clues.
- Create or update an archive only for completed or accepted requirement threads.
- Use `both` only when a completed requirement also produced stable reusable debugging knowledge.

### Completion Gates

Requirement archives and problem archives are separate gates:

- Requirement archiving records what was delivered. Run it when a coherent requirement, phase, feature, or accepted design-to-implementation thread is complete and verified.
- Problem archiving records reusable failure knowledge. Run it after the current task has been implemented, spec-reviewed, code-quality-reviewed, and verified, before starting the next task or when the overall task is ending.

For meaningful development work, the main agent must run a problem-archiving gate after:

- implementation is complete enough to review as a unit
- spec alignment has been checked against `docs/superpowers/specs/` and `docs/superpowers/plans/`
- code quality review has checked correctness, maintainability, test coverage, and integration risks
- verification commands or targeted manual checks have produced concrete evidence

This gate belongs at task boundaries, not inside every small edit. Use it before moving from one planned task to the next, before merge/PR/cleanup when applicable, or before the final response when no next task remains.

### Problem Archiving Ownership

Only the main agent should execute the problem-archiving gate. Subagents may report candidate lessons, suspicious behavior, failed approaches, review findings, or tool quirks, but they should not write or promote problem/inbox/archive assets unless the main agent explicitly delegates that asset-writing task.

During the gate, the main agent should collect candidates from:

- implementation issues and debugging paths
- failed or flaky tests
- spec review mismatches
- code quality review findings
- provider, tool, MCP, SSE, SQLite, filesystem, encoding, or Windows-specific runtime quirks
- subagent reports and unresolved observations

### Inbox-First Problem Routing

When a signal is potentially reusable but not mature enough for a formal problem asset, prefer `inbox` over dropping it.

Use `inbox` for:

- a fix that worked but whose root cause is not yet stable
- a suspicious behavior that may recur but has limited evidence
- a review finding that indicates a possible class of mistakes
- an environment/tool/provider quirk that affected the work but was not fully diagnosed
- a requirement or workflow ambiguity that may need later promotion
- a "could archive or could skip" lesson that future agents might realistically search for
- a release/CI warning that did not fail the run but may affect future builds

Use `none` only when the signal is clearly mechanical, one-off, already covered, or unlikely to help future work. If choosing `none` after meaningful development, state the concrete reason in the final handoff.

Inbox notes should track lifecycle: `Open`, `Partially promoted`, `Promoted`, or `Closed`. When a related problem/archive later covers the signal, update the inbox lifecycle instead of leaving it stale.

### Problem Gate Output

At the end of the gate, report the route decision compactly:

- `none`: no asset, with the concrete reason
- `inbox`: new or updated inbox note, with validation evidence
- `update-existing`: updated archive/problem/inbox asset, with validation evidence
- `new-problem`: formal problem asset, with validation evidence
- `archive` or `both`: only when the route also includes completed requirement history

Before final close-out on meaningful work, confirm whether any new or updated asset is needed.
<!-- asset-compounding-guidance:end -->
