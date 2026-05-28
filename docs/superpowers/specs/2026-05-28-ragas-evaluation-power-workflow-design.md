# RAGAS Evaluation Power Workflow Design

- Date: `2026-05-28`
- Topic slug: `ragas-evaluation-power-workflow`
- Status: `Ready for review`
- Scope: `Server evaluation operations workflow + reproducible result assets`
- Tags: `evaluation`, `ragas`, `workflow`, `export`, `baseline`, `python-parity`, `operations`

## Purpose

The first RAGAS evaluation API made LightRAGNet capable of creating, reading, and cancelling asynchronous `.NET-native` evaluation runs. That was the right foundation, but it is still closer to an API primitive than a complete evaluation workflow.

Python LightRAG's evaluation path is useful because it creates repeatable evidence: it can run against a configured endpoint, save timestamped JSON/CSV result files, compute benchmark statistics, and let developers compare score movement while tuning retrieval, rerank, prompt, or context-building behavior.

LightRAGNet should now add a "Power Workflow" around the existing RAGAS API: a small, operations-oriented workflow that turns individual runs into reusable evaluation evidence, without introducing a UI or making default tests depend on real evaluator keys.

## Decision

Build a Server-side RAGAS evaluation workflow layer with four capabilities:

1. list prior evaluation runs,
2. export one run as safe JSON or CSV,
3. enrich run summaries with benchmark statistics,
4. compare a run against a baseline run.

Also add a documented opt-in real evaluator smoke workflow so a developer can run one paid/external model evaluation intentionally and preserve the evidence path.

The feature remains API-first. A React dashboard is not part of this slice.

## Workflow Shape

The intended developer workflow is:

1. Prepare or confirm the current workspace has indexed evaluation sample documents.
2. Enable `Evaluation:Ragas` locally and provide an admin token plus evaluator API key.
3. Create a small run, usually `maxCases=1`, to validate the real evaluator path.
4. Poll or fetch the run until it reaches a terminal status.
5. Export JSON/CSV artifacts for the run.
6. Promote one trusted run as a manual baseline by preserving its `runId`.
7. Compare later runs against that baseline before accepting retrieval or prompt changes.
8. Archive implementation evidence in `docs/superpowers/archives` only after the feature is implemented and verified.

This workflow intentionally uses explicit run ids instead of hidden "latest successful baseline" magic. Hidden baseline selection is convenient, but it can make evaluation regressions look mysterious. Evaluation systems should be boring in the best possible way.

## API Contract

Existing endpoints remain unchanged:

```http
POST /api/evaluation/ragas/runs
GET  /api/evaluation/ragas/runs/{runId}
POST /api/evaluation/ragas/runs/{runId}/cancel
```

Add:

```http
GET /api/evaluation/ragas/runs
GET /api/evaluation/ragas/runs/{runId}/export?format=json
GET /api/evaluation/ragas/runs/{runId}/export?format=csv
GET /api/evaluation/ragas/runs/{runId}/compare/{baselineRunId}
```

All endpoints keep the existing `X-Evaluation-Token` protection.

### List Runs

`GET /api/evaluation/ragas/runs` returns lightweight summaries only:

```json
{
  "runs": [
    {
      "runId": "ragas-20260528101530-abcd",
      "status": "Completed",
      "createdAt": "2026-05-28T10:15:30Z",
      "completedAt": "2026-05-28T10:16:40Z",
      "total": 5,
      "succeeded": 5,
      "failed": 0,
      "ragasScore": 0.86,
      "durationSeconds": 70.3
    }
  ]
}
```

Rules:

- Sort by `createdAt` descending.
- Do not include per-case answer/context previews in the list response.
- Do not include judge prompt or response diagnostics in the list response.
- Return an empty list when no run file exists.

### Export Run

`GET /api/evaluation/ragas/runs/{runId}/export?format=json` returns a safe export payload with the same privacy policy as the stored run. It must not add full text that was not persisted.

`GET /api/evaluation/ragas/runs/{runId}/export?format=csv` returns `text/csv; charset=utf-8`.

CSV columns:

```text
run_id,case_name,status,faithfulness,answer_relevance,context_recall,context_precision,ragas_score,context_count,answer_hash
```

Rules:

- Unknown run returns `404 run_not_found`.
- Unknown format returns `400 unsupported_export_format`.
- CSV exports per-case rows, not aggregate-only rows.
- CSV values are RFC4180-style escaped.
- Export must not include admin token, evaluator API key, raw provider headers, or hidden full text.

### Benchmark Summary

Extend `RagasEvaluationSummaryDto` with:

- `SuccessRate`
- `ElapsedTimeSeconds`
- `AverageSecondsPerCase`
- `MinRagasScore`
- `MaxRagasScore`
- `FailureReasons`

`FailureReasons` is a dictionary keyed by diagnostic code:

```json
{
  "no_contexts": 2,
  "invalid_json": 1
}
```

Rules:

- Average/min/max scores use succeeded cases only.
- If no case succeeds, score fields remain `null`.
- `ElapsedTimeSeconds` is available only after `StartedAt` and `CompletedAt` are both known.
- `AverageSecondsPerCase` divides elapsed seconds by total case count when total is greater than zero.

### Baseline Compare

`GET /api/evaluation/ragas/runs/{runId}/compare/{baselineRunId}` compares two terminal or non-terminal stored runs by their current summaries.

Response:

```json
{
  "runId": "ragas-20260528101530-abcd",
  "baselineRunId": "ragas-20260527180000-efgh",
  "status": "Comparable",
  "metrics": {
    "ragasScore": { "baseline": 0.82, "current": 0.86, "delta": 0.04, "direction": "Improved" },
    "faithfulness": { "baseline": 0.8, "current": 0.84, "delta": 0.04, "direction": "Improved" }
  },
  "caseCounts": {
    "baselineTotal": 5,
    "currentTotal": 5,
    "matchedCases": 5
  },
  "diagnostics": []
}
```

Rules:

- Compare aggregate metrics and matched case names.
- Aggregate metric direction:
  - `Improved` when delta is greater than `0.0001`,
  - `Regressed` when delta is less than `-0.0001`,
  - `Unchanged` otherwise.
- Missing metric values produce `NotMeasured` for that metric.
- Unknown current or baseline run returns `404 run_not_found`.
- Comparing a run with itself returns `400 same_run_compare`.
- Runs with different case sets are still comparable, but diagnostics must include `case_set_differs`.

## Data Model Changes

Add public DTOs in `LightRAGNet.Share`:

- `RagasEvaluationRunListResponse`
- `RagasEvaluationRunSummaryItemDto`
- `RagasEvaluationExportFormat`
- `RagasEvaluationComparisonResponse`
- `RagasEvaluationMetricComparisonDto`
- `RagasEvaluationCaseCountComparisonDto`

Extend:

- `RagasEvaluationSummaryDto`

Do not move existing RAGAS DTOs into Server-only models. The API client contract should remain in `LightRAGNet.Share`.

## Server Components

Add or extend:

- `RagasEvaluationRunStore.ListAsync(...)`
  - returns all runs sorted by `CreatedAt` descending.
- `RagasEvaluationExportService`
  - builds safe JSON export payloads and CSV output.
- `RagasEvaluationComparisonService`
  - compares aggregate and case-level metric movement.
- `RagasEvaluationRunCoordinator.ListAsync(...)`
  - maps stored records to lightweight summaries.
- `RagasEvaluationRunCoordinator.ExportAsync(...)`
  - fetches a run and delegates export formatting.
- `RagasEvaluationRunCoordinator.CompareAsync(...)`
  - fetches two runs and delegates comparison.
- `RagasEvaluationController`
  - adds list/export/compare endpoints using the existing auth guard.

## Real Evaluator Smoke Workflow

Add a short operations document:

```text
docs/evaluation/ragas-power-workflow.md
```

It must include:

- required local configuration keys,
- a warning that real evaluator calls may cost money,
- sample create-run request with `maxCases=1`,
- sample poll/get command,
- sample export commands,
- guidance to keep secrets out of committed files,
- note that default automated tests remain fake/evaluator-isolated.

The implementation plan may add a PowerShell helper script only if it stays opt-in and never stores secrets.

## Security and Privacy

- Keep `X-Evaluation-Token` on every endpoint.
- Do not log or export `AdminToken`, evaluator API key, authorization headers, or provider headers.
- CSV export must use hashes and previews already present in the stored run; it must not reconstruct hidden full text.
- JSON export must not include more than the normal `GET /runs/{runId}` returns, except export metadata such as generated time and format.
- Baseline comparison must compare scores and case names, not answer text.

## Testing Strategy

Default tests must remain isolated from Qdrant, Neo4j, real evaluator keys, and paid APIs.

Required test groups:

- run store list sorting and empty-store behavior,
- benchmark summary calculation,
- list endpoint auth and response shape,
- JSON export privacy and not-found handling,
- CSV export escaping and unsupported-format handling,
- baseline comparison improved/regressed/unchanged/not-measured cases,
- controller route coverage for list/export/compare.

Manual real evaluator smoke is documented but not part of `dotnet test`.

## Out of Scope

- React dashboard.
- Run deletion.
- Automatic sample document seeding.
- Python worker or direct Python RAGAS package integration.
- Default CI job that calls a real evaluator model.
- Automatic "latest baseline" selection.

## Acceptance Criteria

- Existing create/get/cancel behavior remains compatible.
- `GET /api/evaluation/ragas/runs` returns sorted lightweight run summaries.
- JSON and CSV export endpoints return safe artifacts for a stored run.
- Run summaries include success rate, elapsed timing, min/max score, and failure reason counts.
- Compare endpoint reports aggregate metric deltas and case-set diagnostics.
- Real evaluator smoke workflow is documented as opt-in and secret-safe.
- Focused RAGAS server tests pass.
- Full Server tests pass.
- Full solution tests pass before implementation is archived.

## Spec Self-Review

- Placeholder scan: no placeholder markers remain.
- Scope check: this is one coherent Server workflow slice; UI and deletion are explicitly out of scope.
- Ambiguity check: baseline selection is explicit by run id, export formats are fixed to JSON/CSV, and metric direction thresholds are specified.
- Privacy check: every new read/export path preserves existing full-text and secret boundaries.
