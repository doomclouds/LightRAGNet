# Test Verification Transient Signals

- Date: `2026-05-26`
- Topic slug: `test-verification-transient-signals`
- Status: `Inbox`
- Lifecycle: `Open`
- Revisit trigger: `当 full solution 或 reviewer 验证再次出现同一 Server 并发测试抖动，或同一 test project 被并发 dotnet test 写锁阻塞时复查。`
- Scope: `Test`
- Confidence: `Medium`
- Route candidate: `update-existing`

## Signal

本轮 `offline-retrieval-json-dataset-oracle` 收尾验证中观察到两个弱但可能复现的测试验证信号：

- 两个 reviewer 并行跑同一个 `LightRAGNet.Tests.csproj` 时，出现测试 DLL 写入锁；顺序重跑同样命令后通过。
- 首次 `dotnet test .\LightRAGNet.slnx --verbosity minimal` 中 `DocumentConversionProcessorTests.ProcessNextBatchAsync_WhenConcurrentProcessorsRace_ClaimsDocumentOnce` 一次性失败，单独复跑该测试通过，随后 full solution 顺序复跑通过。

## Why It Might Matter

这两个信号都发生在验证阶段，不属于本轮 JSON oracle 代码改动范围，但会影响未来 close-out 对测试结果的判断。并行写锁已经出现两次 reviewer 复现；Server 并发测试抖动目前只有一次，证据还不足以升级为 formal problem。

## What Is Missing

- 需要确认 `DocumentConversionProcessorTests.ProcessNextBatchAsync_WhenConcurrentProcessorsRace_ClaimsDocumentOnce` 是否会在 full solution 中稳定或周期性复现。
- 需要确认 DLL 写锁只来自同时跑同一 test project，还是 solution/build 输出目录共享导致。
- 需要明确是否已有 test runner 配置可以限制同一 project 的并发构建/运行。

## Likely Next Route

如果 Server 并发测试再次在 full solution 中失败但单测通过，优先检查并更新既有 Server 并行测试问题资产；如果 DLL 写锁再次影响 reviewer/subagent 验证流程，考虑沉淀为独立的 test verification workflow problem，规则是同一 test project 的 `dotnet test` 不要并发执行。

## Related Assets

- Spec: [Offline Retrieval JSON Dataset and Oracle Design](../../specs/2026-05-26-offline-retrieval-json-dataset-oracle-design.md)
- Plan: [Offline Retrieval JSON Dataset and Oracle Implementation Plan](../../plans/2026-05-26-offline-retrieval-json-dataset-oracle-implementation-plan.md)
- Archive: [Offline Retrieval JSON Dataset and Oracle](../../archives/2026-05/2026-05-26-offline-retrieval-json-dataset-oracle-archives.md)
- Problems:
  - [Server Filesystem Test Parallelism](../../problems/2026-05/2026-05-19-server-filesystem-test-parallelism-problem.md)
  - [Server Tests Real RAG Storage Isolation](../../problems/2026-05/2026-05-20-server-tests-real-rag-storage-isolation-problem.md)
