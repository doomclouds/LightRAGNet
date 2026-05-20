# Chat Query UI Adaptation Design

- Date: `2026-05-20`
- Topic slug: `chat-query-ui-adaptation`
- Status: `Ready for review`
- Scope: `Blazor chat UI + RagQuery API/SSE contract + tests`
- Tags: `chat-ui`, `query-mode`, `references`, `query-cache`, `sse`, `lightrag-alignment`

## Purpose

Recent query work added explicit `QueryMode` routing, Naive/Bypass behavior, structured raw data, references, keyword metadata, and bounded query-time LLM cache. The current Blazor chat still sends only `{ query }` to `RagQueryController`, always uses `Mix + streaming`, consumes only text chunks, and hides SSE error events. Users cannot choose query mode, understand whether an answer can use cache, inspect references, or see useful query diagnostics.

This phase adapts Chat from a plain streaming textbox into a small query workbench that exposes the most valuable new capabilities without turning the page into a configuration wall.

## Current Gap

- `RagChat.razor` has one input box, a send button, and plain message bubbles.
- `ApiClient.QueryRagAsync` posts only `query` and ignores broad exceptions plus `ErrorEvent`.
- `RagQueryController.QueryAsync` creates `QueryParam` internally with `Stream = true` and default `Mix`.
- `RagQueryEvent` only has `TextChunkEvent`, `ErrorEvent`, and `DoneEvent`.
- `ChatMessageModel` only stores `Role` and `Text`, so the UI cannot persist mode, references, cache state, or errors per answer.
- Query answer cache is only available for non-streaming eligible requests, but the UI has no non-streaming or cacheable mode.

## Product Decision

Implement a bounded Chat adaptation:

- Expose query mode selection in the chat composer.
- Keep streaming as the default, but add a clear `Cacheable` non-streaming option.
- Show answer metadata on assistant messages: mode, output type, references, and query diagnostics.
- Make SSE/API errors visible in the assistant message and snackbar.
- Keep advanced knobs folded away until needed.

Do not add cache management screens, persistent chat history, multi-session chat, prompt template editing, or full raw-data inspection in this phase.

## User Experience

The first viewport remains the actual chat experience. Add a compact query toolbar above the input area:

- Mode segmented control:
  - primary options: `Mix`, `Naive`, `Bypass`
  - advanced menu: `Local`, `Global`, `Hybrid`
- Output toggle:
  - `Streaming`: default, fastest perceived response, query answer cache skipped
  - `Cacheable`: non-streaming request, eligible for query answer cache when backend rules allow it
- References toggle:
  - enabled by default for RAG modes
  - disabled or visually marked as not applicable for `Bypass`
- Advanced expander:
  - `TopK`, `ChunkTopK`, `ResponseType`, `EnableRerank`
  - optional manual high-level / low-level keywords
  - `OnlyNeedContext` and `OnlyNeedPrompt` as debug options, not primary workflow

Assistant messages should display:

- response content
- small badges for mode and output type
- a references expander when references are present
- a diagnostics expander with keywords and processing info when available
- a clear error state when query or SSE fails

## Query Mode Semantics In UI

- `Mix`: default balanced RAG mode.
- `Naive`: vector-only chunk retrieval; useful when graph context is too broad or unavailable.
- `Bypass`: direct LLM call; no references and no RAG context.
- `Local`, `Global`, `Hybrid`: available in advanced menu for KG strategy testing and parity checks.

Changing mode affects the next message only. Existing assistant messages keep their recorded metadata.

## API Contract

Move the request contract into shared models so Web and Server compile against one shape:

```csharp
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

`RagQueryController` maps this request into `QueryParam`. It should continue to default to safe `Mix + streaming` when fields are omitted.

## SSE / Response Contract

Extend `RagQueryEvent` with metadata:

```text
text_chunk -> answer content
metadata   -> mode, stream/cacheable, references, keywords, processing info
error      -> visible query failure
done       -> completion marker
```

For streaming queries:

- send text chunks as today
- send metadata near the end when `QueryResult.RawData` is available
- send `done`

For non-streaming queries:

- send one text chunk with final content
- send metadata
- send `done`

`ApiClient.QueryRagAsync` should accept callbacks or return a result object that can update:

- answer text
- metadata
- error state

It must not swallow broad exceptions. `ErrorEvent` should become a typed exception or error callback.

## Message Model

Extend `ChatMessageModel` enough for UI display:

- `Role`
- `Text`
- `QueryMode? Mode`
- `bool IsStreaming`
- `bool IsCacheable`
- `List<ReferenceItemModel> References`
- `List<string> HighLevelKeywords`
- `List<string> LowLevelKeywords`
- `Dictionary<string, string> Diagnostics`
- `string? ErrorMessage`

This stays in-memory through `ChatHistoryService` for now. Persistent chat storage is out of scope.

## Cache UX Boundary

The UI should not promise a cache hit unless the backend reports one. In this phase:

- `Streaming` mode explains that answer cache is skipped.
- `Cacheable` mode sends `Stream = false`.
- If backend metadata can report cache state, show `Live` / `Cached`.
- If backend cannot yet report cache state, show only `Cacheable request` and leave hit/miss hidden.

Do not build cache clearing, cache browsing, or cache key inspection UI here.

## Error Handling

- Empty query remains client-side blocked.
- HTTP failures surface in the assistant message and snackbar.
- SSE `ErrorEvent` surfaces in the assistant message and snackbar.
- User cancellation should leave the partial assistant message with a cancelled state and should not show a failure snackbar.
- `ApiClient.QueryRagAsync` must no longer catch and ignore all exceptions.

## Component Boundary

Keep implementation scoped:

- `RagChat.razor` may be reorganized into helper methods and small private state objects.
- Shared DTO/event types should live under `LightRAGNet.Share.Models`.
- Server controller should only map request fields into `QueryParam` and package metadata events; core query behavior should not change.
- Avoid introducing a new UI state management framework.

If `RagChat.razor` grows too large during implementation, extract a small child component for query settings or message metadata display, but do not split the whole chat page preemptively.

## Testing Strategy

Server/API tests:

- request mode maps to `QueryParam.Mode`
- `Stream = false` produces non-streaming query behavior
- `IncludeReferences` is passed through
- invalid or empty query returns a visible error contract

Web/source tests:

- chat request includes selected mode, stream/cacheable flag, and reference flag
- `ApiClient.QueryRagAsync` does not swallow broad exceptions
- SSE `ErrorEvent` is surfaced
- metadata event updates assistant message references/diagnostics

Focused UI-level tests can remain source-level unless the project gains a Blazor component test harness.

## Out of Scope

- Persistent chat sessions.
- Full cache management UI.
- Cache prefix scanning or cache deletion.
- Prompt template editor.
- Multi-agent or multi-document scoped chat.
- Full raw data JSON viewer as a primary feature.
- Changing core query ranking, cache key semantics, or workspace revision rules.

## Acceptance Criteria

- User can choose `Mix`, `Naive`, and `Bypass` from Chat before sending a message.
- User can choose streaming vs cacheable non-streaming output.
- Chat request sends selected mode and output options to `RagQueryController`.
- Assistant messages display mode and output metadata.
- References are visible when backend returns them.
- SSE and HTTP errors are visible instead of silently ignored.
- Existing default behavior remains `Mix + streaming`.
- Tests cover request mapping, error surfacing, and message metadata handling.
