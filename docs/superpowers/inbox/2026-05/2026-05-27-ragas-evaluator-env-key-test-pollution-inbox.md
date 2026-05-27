# RAGAS Evaluator Environment Key Test Pollution

- Date: `2026-05-27`
- Topic slug: `ragas-evaluator-env-key-test-pollution`
- Status: `Inbox`
- Lifecycle: `Open`
- Revisit trigger: `当 RAGAS evaluator、provider key fallback、或配置缺失测试再次出现受本机环境变量影响的结果漂移时复查。`
- Scope: `Test`
- Confidence: `Medium`
- Route candidate: `update-existing`

## Signal

RAGAS evaluator 改为默认 `deepseek-v4-flash` 并支持 `DEEPSEEK_API_KEY` fallback 后，缺 evaluator API key 的 controller/coordinator 测试在本机真实存在 `DEEPSEEK_API_KEY` 时不再返回 misconfigured，而是排队成功。这不是生产逻辑错误，而是测试没有隔离环境变量导致的误判。

## Why It Might Matter

LightRAGNet 已有多个 provider 支持从环境变量兜底读取 key。后续如果测试直接依赖真实 `Environment.GetEnvironmentVariable(...)`，开发机、CI、子代理会因为本地 secret 状态不同得到不同结果，尤其是“缺配置应报错”的负向测试。

## What Is Missing

- 还没有确认其它 provider 缺 key 测试是否同样会受真实环境变量污染。
- 还没有形成统一测试 helper，用来在 provider/key fallback 测试中显式注入 fake environment。
- 需要观察这个模式是否在 RAGAS 以外的配置健康检查、LLM、Embedding、Rerank 测试中复现。

## Likely Next Route

如果同类问题再次出现，优先更新既有 testability 或 provider 配置测试资产；若复现范围扩大，再提升为 formal problem，规则是 provider key fallback 必须通过可注入 secret/env provider 测试，负向测试不能读取真实开发机环境变量。

## Related Assets

- Spec: [RAGAS Evaluation API Design](../../specs/2026-05-27-ragas-evaluation-api-design.md)
- Plan: [RAGAS Evaluation API Implementation Plan](../../plans/2026-05-27-ragas-evaluation-api-implementation-plan.md)
- Archive: [RAGAS Evaluation API](../../archives/2026-05/2026-05-27-ragas-evaluation-api-archives.md)
- Problems:
  - [Testability And Asset Completion Gaps](../../problems/2026-05/2026-05-18-testability-refactor-completion-gaps-problem.md)
