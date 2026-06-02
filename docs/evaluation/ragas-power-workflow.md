# RAGAS Evaluation Power Workflow

This workflow is an opt-in development and operations path for collecting
RAGAS-compatible evaluation evidence from the current LightRAGNet workspace.

## Safety

Real evaluator smoke runs call external model APIs and may cost money. Keep
tokens and API keys in local user secrets, environment variables, or untracked
configuration.

Default automated tests use fake query and evaluator services. They must not
require real evaluator keys, Qdrant, Neo4j, or paid model calls.

## Local Configuration

```json
{
  "Evaluation": {
    "Ragas": {
      "Enabled": true,
      "AdminToken": "<local secret>",
      "EvaluatorModel": "deepseek-v4-flash",
      "ApiKey": "<local secret or DEEPSEEK_API_KEY>",
      "BaseUrl": "https://api.deepseek.com",
      "MaxCasesPerRun": 5,
      "AllowPersistFullText": false
    }
  }
}
```

## Smoke Run

```http
POST /api/evaluation/ragas/runs
X-Evaluation-Token: <local secret>
Content-Type: application/json

{
  "caseNames": [],
  "maxCases": 1,
  "includeFullText": false,
  "query": {
    "mode": "Mix",
    "topK": 40,
    "chunkTopK": 20,
    "enableRerank": true
  }
}
```

## Inspect and Export

```http
GET /api/evaluation/ragas/runs
X-Evaluation-Token: <local secret>
```

```http
GET /api/evaluation/ragas/runs/{runId}
X-Evaluation-Token: <local secret>
```

```http
GET /api/evaluation/ragas/runs/{runId}/export?format=json
X-Evaluation-Token: <local secret>
```

```http
GET /api/evaluation/ragas/runs/{runId}/export?format=csv
X-Evaluation-Token: <local secret>
```

JSON export preserves the stored run shape but defensively removes full-text
fields when the run request did not opt into full-text persistence. CSV export
uses safe columns for spreadsheet analysis and does not include prompts,
responses, retrieved context text, or diagnostic text payloads.

## Compare Against a Baseline

```http
GET /api/evaluation/ragas/runs/{runId}/compare/{baselineRunId}
X-Evaluation-Token: <local secret>
```

Use an explicit baseline run id from a trusted prior run. Do not treat the
latest successful run as a baseline unless it was intentionally selected.

The comparison response reports metric deltas for `ragasScore`,
`faithfulness`, `answerRelevance`, `contextRecall`, and `contextPrecision`.
It also includes case-count diagnostics so regressions are not interpreted
against a silently different case set.

## Suggested Evidence Bundle

For each accepted benchmark run, keep:

- the create request body,
- the selected baseline run id,
- the list response showing both run ids,
- JSON export for audit,
- CSV export for quick metric review,
- comparison response when a baseline exists,
- the git commit or branch being evaluated.

## Operational Boundary

This workflow is for local, explicit evaluation. It does not run automatically
in normal test commands, CI, or server startup. A real evaluator smoke should be
triggered only after local secrets and external storage resources are confirmed
for the current workspace.
