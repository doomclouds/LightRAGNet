# Neo4j Labels Unwind Filter Problem

- Date: `2026-05-22`
- Topic slug: `neo4j-labels-unwind-filter`
- Status: `Captured`
- Scope: `Feature`
- Tags: `knowledge-graph`, `neo4j`, `cypher`, `labels`, `api`

## Symptom

Knowledge Graph tab 可以加载主体图谱，但 label 控件请求 `/api/graph/labels` 返回 500。服务端日志显示 Neo4j `ClientException`，错误位置靠近 `WHERE label <> 'base'`。前端表现可能是 label 下拉为空、刷新异常，用户会误以为图谱功能整体不稳定。

## Trigger / Context

- `Neo4jGraphStore.GetPopularLabelsAsync` 查询热门标签。
- Cypher 先 `MATCH` workspace 节点，再 `UNWIND labels(n) as label`。
- 旧查询直接在 `UNWIND` 后写 `WHERE label <> '{_workspaceLabel}'`，并用字符串插值拼 workspace label。

## Root Cause

Neo4j 对 `UNWIND` 后的过滤需要通过 `WITH label` 明确传递变量，再执行 `WHERE`。旧查询把 `WHERE` 直接接在 `UNWIND` 后，导致 Cypher 解析失败。

同时，workspace label 作为字符串插值进入 Cypher 条件，不如参数化清晰；虽然 label 名称本身来自配置，但过滤值不需要拼进查询文本。

## Fix

- 将查询改为：
  - `UNWIND labels(n) as label`
  - `WITH label`
  - `WHERE label <> $workspaceLabel`
  - `WITH label, count(*) as degree`
- 调用 `RunAsync` 时传入 `{ limit, workspaceLabel = _workspaceLabel }`。
- 添加 source regression test，断言 `UNWIND labels(n) as label` 后存在 `WITH label` 再过滤。
- 运行时验证 `/api/graph/labels` 返回 labels，而不是 500。

## Why This Fix

这个修法保留了热门标签查询的原有语义，只修正 Cypher 变量传递边界，并把 workspace label 过滤参数化。相比前端吞错、隐藏 label 控件或改成客户端过滤，它在数据源处解决了接口 500，且更容易用源码测试守住。

## Recognition Clues

- `/api/graph/labels` 500，但 `/api/graph/query` 仍能返回图谱。
- 服务端日志含 `Neo4j.Driver.ClientException` 和 `Invalid input 'WHERE'`。
- 查询文本里出现 `UNWIND labels(n) as label` 后紧接 `WHERE label <> ...`。
- 修复后 label 接口返回类似 `Entity, concept, data...` 的列表。

## Applicability / Non-Applicability

### Applies When

- Neo4j Cypher 在 `UNWIND` 派生变量后要继续过滤、聚合或排序。
- 图谱 label/tag/category 查询在服务端报 Cypher parse error。
- 需要排除 workspace/internal label。

### Does Not Apply When

- Neo4j 连接、认证或 database name 配置错误。
- labels 返回空数组但没有 500；那应检查图数据是否真的包含业务 label。
- 查询不是从 `UNWIND labels(n)` 派生变量。

## Related Artifacts

- Spec: [graph workbench python parity design](../../specs/2026-05-22-graph-workbench-python-parity-design.md)
- Plan: [graph workbench python parity implementation plan](../../plans/2026-05-22-graph-workbench-python-parity-implementation-plan.md)
- Archive: [graph workbench python parity archives](../../archives/2026-05/2026-05-22-graph-workbench-python-parity-archives.md)
- Related Problems:
  - None.
- Code or Test:
  - [Neo4jGraphStore.cs](../../../../src/LightRAGNet.Storage/Neo4jGraphStore.cs)
  - [Neo4jGraphStoreSourceTests.cs](../../../../tests/LightRAGNet.Tests/Storage/Neo4jGraphStoreSourceTests.cs)
