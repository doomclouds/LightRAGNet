# RAGAS Evaluation API

- Date: `2026-05-27`
- Topic slug: `ragas-evaluation-api`
- Status: `Archived`
- Scope: `Feature`
- Tags: `evaluation`, `ragas`, `api`, `auth`, `operations`

## Summary

本归档记录 .NET-native RAGAS-compatible evaluation API 的交付闭环：Server 现在具备受 admin token 保护的 create/get/cancel 运行接口，能够通过内置数据集、当前 workspace 查询、OpenAI-compatible judge、WorkingDir JSON run store 和隐私安全快照完成异步评估运行。

## Delivered Scope

- 增加 `POST /api/evaluation/ragas/runs`、`GET /api/evaluation/ragas/runs/{runId}`、`POST /api/evaluation/ragas/runs/{runId}/cancel` 三个 Server controller endpoint。
- 使用 `X-Evaluation-Token` 做 endpoint 级鉴权；缺失、错误或未配置 admin token 时在进入 coordinator 前返回 `401`。
- 在 `Program.cs` 注册 RAGAS options、parser、snapshotter、run store、data loader、scoped query/evaluator runner 以及 singleton coordinator，避免 singleton 捕获 scoped runner。
- API 测试覆盖 missing/wrong/valid token、create/get/cancel、active run conflict、disabled endpoint、misconfigured evaluator API key，并保持 fake evaluator/query client 隔离外部存储和真实模型。

## Out of Scope

- 本次没有增加 UI、run listing、run deletion、export 或 dashboard。
- 本次没有执行真实 evaluator smoke；真实模型调用仍然是本地 opt-in，可能产生费用。
- 本次没有把默认测试改为依赖真实 Qdrant、Neo4j 或外部 evaluator key。

## Verification Snapshot

- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluationControllerTests" --no-restore --verbosity minimal`：8 passed。
- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~Evaluation" --no-restore --verbosity minimal`：74 passed。
- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --filter "FullyQualifiedName~RagasEvaluation" --no-restore --verbosity minimal`：49 passed。
- `dotnet test .\tests\LightRAGNet.Server.Tests\LightRAGNet.Server.Tests.csproj --no-restore --verbosity minimal`：296 passed。
- `dotnet test .\LightRAGNet.slnx --no-restore --verbosity minimal`：296 passed。
- Controller RED 验证先出现 8 个 `404` 失败，再实现 controller/DI 后转 GREEN。

## Manual Real-Evaluator Smoke Note

真实 evaluator smoke 需要本地显式配置，不属于默认测试套件：

```json
{
  "Evaluation": {
    "Ragas": {
      "Enabled": true,
      "AdminToken": "<local secret>",
      "EvaluatorModel": "gpt-4o-mini",
      "ApiKey": "<local secret>",
      "BaseUrl": "https://api.openai.com/v1"
    }
  }
}
```

Sample request:

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

Real evaluator smoke calls external model APIs and may cost money. It is opt-in and is not part of the default test suite.

## Source Documents

- Spec: [RAGAS Evaluation API Design](../../specs/2026-05-27-ragas-evaluation-api-design.md)
- Visual: None found for this topic.
- Plan: [RAGAS Evaluation API Implementation Plan](../../plans/2026-05-27-ragas-evaluation-api-implementation-plan.md)

## Related Problems

- None.

## Notes

- Controller 使用 coordinator operation result 的 `StatusCode(result.StatusCode, result.Value)` / `{ code, message }` 映射；401 响应不返回 secret。
- 完整 Server/solution 测试已在最终验证快照中声明。
