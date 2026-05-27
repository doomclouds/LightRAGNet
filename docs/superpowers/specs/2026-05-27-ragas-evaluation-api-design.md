# RAGAS Evaluation API Design

- Date: `2026-05-27`
- Topic slug: `ragas-evaluation-api`
- Status: `Ready for review`
- Scope: `Server evaluation API + .NET native evaluator + persisted run state`
- Tags: `evaluation`, `ragas`, `llm-as-judge`, `api`, `operations`, `python-parity`

## Purpose

LightRAGNet now has deterministic retrieval regression coverage through the offline JSON oracle and a Server `/api/RagQuery/data` smoke layer. Those layers protect retrieval contracts, but they do not answer whether the generated answer is faithful, relevant, and backed by useful context.

Python LightRAG has `lightrag/evaluation/eval_rag_quality.py`, which runs a RAGAS-style quality evaluation against a running API and evaluator models. LightRAGNet should add an equivalent operational capability, but as a .NET-native Server API rather than a Python worker. The goal is a real, opt-in, LLM-as-judge evaluation path for the current indexed workspace, without adding UI or default-test dependency on real evaluator keys.

## Decision

Build a development/operations-only RAGAS-compatible evaluation API:

```http
POST /api/evaluation/ragas/runs
GET  /api/evaluation/ragas/runs/{runId}
POST /api/evaluation/ragas/runs/{runId}/cancel
```

The API creates asynchronous evaluation runs. Each run reads the built-in evaluation dataset, queries the current LightRAGNet workspace, extracts `answer + retrieved contexts + ground_truth`, asks an evaluator LLM to score four RAGAS-compatible metrics, and persists run state to a JSON file under `LightRAG:WorkingDir`.

This is RAGAS-compatible, not a promise of byte-for-byte or score-for-score equivalence with Python RAGAS. The first implementation is `.NET native` and reports that evaluator backend explicitly.

## Scope Boundary

In scope:

- New Server API endpoints for create/get/cancel RAGAS evaluation runs.
- Async background execution with one active run at a time.
- WorkingDir JSON persistence for run state and results.
- Built-in dataset loading from packaged evaluation data.
- Case filtering via `caseNames` and `maxCases`.
- Query parameter snapshot: `mode`, `topK`, `chunkTopK`, `enableRerank`.
- Real answer generation through `LightRAG.QueryAsync`.
- Retrieved context extraction from `QueryResult.RawData["data"]["chunks"][].content`.
- .NET-native evaluator LLM call for all four metrics in one JSON response per case.
- Admin-token protected, opt-in API.
- Default test coverage with fake evaluator and fake model/storage dependencies.

Out of scope:

- UI.
- Python RAGAS worker.
- Full Python RAGAS score parity claim.
- Automatic sample document seeding into the current workspace.
- Arbitrary dataset path, uploaded dataset, or remote dataset URL.
- Multiple concurrent evaluation runs.
- Run listing, deletion, export, or dashboard.
- Default tests requiring real API keys or real evaluator models.

## Data Source

The first version evaluates the current already-indexed workspace. It does not build a temporary evaluation workspace and does not insert sample documents automatically.

Dataset source is fixed to the built-in evaluation data packaged with the Server:

```text
Evaluation/Data/
  sample_dataset.json
  sample_retrieval_oracle.json
  sample_documents/
```

`sample_dataset.json` provides `question`, `ground_truth`, and `project`. The RAGAS API only needs dataset questions and ground truth; retrieval oracle files remain useful for future correlation and sanity checks.

Requests may filter by case name/question key, but cannot supply arbitrary paths:

```json
{
  "caseNames": [],
  "maxCases": 3
}
```

Rules:

- Empty `caseNames` means use the default dataset order up to `maxCases`.
- Unknown case names fail the run creation request with `400`.
- `maxCases` must be positive and cannot exceed configured `MaxCasesPerRun`.
- The request cannot reference a local file path, uploaded file, or URL.

## API Contract

### Create Run

```http
POST /api/evaluation/ragas/runs
X-Evaluation-Token: <configured token>
Content-Type: application/json
```

Request:

```json
{
  "caseNames": [],
  "maxCases": 3,
  "includeFullText": false,
  "query": {
    "mode": "Mix",
    "topK": 40,
    "chunkTopK": 20,
    "enableRerank": true
  }
}
```

Response:

```json
{
  "runId": "ragas-20260527-abcdef",
  "status": "Queued",
  "createdAt": "2026-05-27T10:00:00Z",
  "message": "RAGAS evaluation run queued."
}
```

### Get Run

```http
GET /api/evaluation/ragas/runs/{runId}
X-Evaluation-Token: <configured token>
```

Response:

```json
{
  "runId": "ragas-20260527-abcdef",
  "status": "Completed",
  "evaluationType": "ragas-compatible",
  "evaluatorBackend": "dotnet-native",
  "createdAt": "2026-05-27T10:00:00Z",
  "startedAt": "2026-05-27T10:00:01Z",
  "completedAt": "2026-05-27T10:01:10Z",
  "request": {
    "caseNames": [],
    "maxCases": 3,
    "includeFullText": false,
    "query": {
      "mode": "Mix",
      "topK": 40,
      "chunkTopK": 20,
      "enableRerank": true
    },
    "previewMaxChars": 500
  },
  "summary": {
    "total": 3,
    "succeeded": 3,
    "failed": 0,
    "cancelled": 0,
    "averageMetrics": {
      "faithfulness": 0.82,
      "answerRelevance": 0.88,
      "contextRecall": 0.76,
      "contextPrecision": 0.81,
      "ragasScore": 0.8175
    }
  },
  "cases": []
}
```

### Cancel Run

```http
POST /api/evaluation/ragas/runs/{runId}/cancel
X-Evaluation-Token: <configured token>
```

Rules:

- `Queued` or `Running` runs move toward `Cancelled` and trigger their cancellation token.
- `Completed`, `Failed`, or `Cancelled` runs return their current terminal status.
- Disconnecting from `POST /runs` does not cancel the background run.

## Query Behavior

The evaluator queries the current workspace with `LightRAG.QueryAsync`.

Defaults:

- `mode = Mix`
- `topK = 40`
- `chunkTopK = 20`
- `enableRerank = true`

Forced values:

- `stream = false`
- `includeReferences = true`
- `onlyNeedContext = false`
- `onlyNeedPrompt = false`

`RawData` is used to extract contexts:

- Preferred: `data.chunks[].content`
- Context metadata: `chunk_id`, `file_path`, `reference_id`
- If no chunks are returned, the case fails with diagnostics rather than asking the judge to score empty context as if it were valid.

## Evaluator Design

The first evaluator is .NET native and calls an OpenAI-compatible chat completion endpoint through a dedicated evaluator client. It does not reuse the normal application `ILLMService` implicitly, because evaluation should be configured and audited separately from answer generation.

Each case uses one judge call that must return strict JSON:

```json
{
  "faithfulness": {
    "score": 0.8,
    "reason": "The answer is mostly supported by retrieved context."
  },
  "answer_relevance": {
    "score": 0.9,
    "reason": "The answer addresses the question directly."
  },
  "context_recall": {
    "score": 0.7,
    "reason": "Most ground-truth facts are present in context."
  },
  "context_precision": {
    "score": 0.85,
    "reason": "Retrieved context is mostly relevant with limited noise."
  }
}
```

Parsing rules:

- All four metric objects are required.
- Every `score` must be between `0` and `1`.
- Every `reason` must be non-empty text.
- Missing fields, invalid JSON, non-numeric scores, or out-of-range scores mark the case failed.
- The system must not silently coerce parse failures to `0`.
- `ragasScore` is the mean of the four metrics only when the case succeeds.

Diagnostics should include:

- judge prompt preview/hash,
- judge response preview/hash,
- parse failure reason when applicable,
- evaluator model and base URL label,
- answer/context preview metadata.

## Configuration

Add:

```json
{
  "Evaluation": {
    "Ragas": {
      "Enabled": false,
      "AdminToken": "",
      "EvaluatorModel": "gpt-4o-mini",
      "ApiKey": "",
      "BaseUrl": "",
      "TimeoutSeconds": 180,
      "MaxConcurrentCases": 1,
      "MaxCasesPerRun": 5,
      "AllowPersistFullText": false,
      "PreviewMaxChars": 500,
      "PersistJudgePrompts": true,
      "PersistJudgeResponses": true
    }
  }
}
```

Rules:

- `Enabled=false`: create-run returns a disabled error.
- `Enabled=true` and missing `AdminToken`: endpoint returns misconfigured.
- Missing evaluator API key returns misconfigured.
- API keys and tokens must not be logged, persisted in run snapshots, or returned in diagnostics.
- `MaxConcurrentCases` is included for future expansion, but the first version should run cases serially unless implementation stays simple and deterministic.

## Security

All endpoints require:

```http
X-Evaluation-Token: <configured token>
```

Behavior:

- Missing or invalid token: `401`.
- Evaluation disabled: `403` or `409` with explicit message.
- Enabled but missing admin token or evaluator key: `503`.
- Active run conflict: `409` with current `runId`.

No UI is added in this phase. The API is intended for trusted development/operations usage.

## Persistence

Run state is stored in:

```text
{LightRAG:WorkingDir}/evaluation/ragas_runs.json
```

Use JSON file persistence with atomic write patterns consistent with existing local storage practices.

Store:

- run id,
- status,
- timestamps,
- request snapshot,
- summary,
- per-case result,
- diagnostics,
- terminal error information.

Do not store:

- API key,
- admin token,
- raw provider headers,
- unrelated application secrets.

## Text Persistence and Privacy

Default result persistence stores preview and SHA-256 hash, not full answer/context text:

```json
{
  "answerPreview": "first 500 chars",
  "answerHash": "sha256",
  "contexts": [
    {
      "preview": "first 500 chars",
      "hash": "sha256",
      "chunkId": "chunk-id",
      "filePath": "docs/example.md"
    }
  ]
}
```

`includeFullText=true` is allowed only when `AllowPersistFullText=true`.

Judge prompts can include answer/context text. If full text is not allowed, persisted prompt diagnostics must also be preview/hash only.

## Run State Machine

Statuses:

- `Queued`
- `Running`
- `Completed`
- `Failed`
- `Cancelled`

Rules:

- Only one `Queued` or `Running` run may exist at a time.
- Create-run returns `409` when another active run exists.
- Cancellation is best-effort but must mark the run `Cancelled` if the token is observed.
- Per-case failures do not necessarily fail the whole run. A run with completed execution but failed cases should be `Completed` with `summary.failed > 0`, unless the runner itself crashed.
- Runner infrastructure exceptions should mark the run `Failed`.

## Testing Strategy

Default tests must not call real evaluator services.

Required coverage:

- Controller auth: missing token, wrong token, valid token.
- Disabled/misconfigured config behavior.
- Create/get/cancel run endpoints.
- Single active run conflict.
- JSON run store persistence and reload.
- Request validation: unknown case, max cases too high, full text disallowed.
- Runner full path with fake query/evaluator.
- Judge JSON parser success.
- Judge JSON parser failures: invalid JSON, missing metric, out-of-range score.
- Result privacy: preview/hash by default, full text only when enabled.

Manual/opt-in real evaluator smoke can be documented later, but must not be part of default `dotnet test`.

## Alternatives Considered

### Python RAGAS Worker

Pros:

- Closest to Python LightRAG reference.
- Can use the real RAGAS package directly.

Cons:

- Adds Python runtime/process orchestration to Server.
- More difficult to package and monitor in the .NET application.

Decision: reject for first implementation because the selected direction is .NET-native.

### Synchronous API

Pros:

- Simpler endpoint.

Cons:

- Real judge calls are slow and failure-prone.
- HTTP timeout and cancellation behavior become fragile.

Decision: reject. Use async runs.

### SQLite Persistence

Pros:

- Better for product-grade history.

Cons:

- Requires schema and migration work.
- More than needed for an operations-only first version.

Decision: reject for first version. Use WorkingDir JSON.

## Acceptance Criteria

- `POST /api/evaluation/ragas/runs`, `GET /api/evaluation/ragas/runs/{runId}`, and cancel endpoint exist.
- Endpoints require `X-Evaluation-Token` and respect `Evaluation:Ragas:Enabled`.
- Runs persist to `{WorkingDir}/evaluation/ragas_runs.json`.
- Only one run can be active at a time.
- Built-in dataset questions can be selected by case names and `maxCases`.
- The runner generates non-streaming LightRAG answers and extracts retrieved contexts.
- .NET-native evaluator parses strict four-metric judge JSON.
- Run responses include summary, per-case metrics, diagnostics, evaluator backend, and request snapshot.
- Default tests pass without real evaluator keys or external RAG storage.
- No UI is added.
