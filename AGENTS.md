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
