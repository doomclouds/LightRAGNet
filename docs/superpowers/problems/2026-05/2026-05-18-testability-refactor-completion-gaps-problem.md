# Testability And Asset Completion Gaps

- Date: `2026-05-18`
- Topic slug: `testability-refactor-completion-gaps`
- Status: `Captured`
- Scope: `Repo`
- Tags: `testability`, `structural-refactor`, `completion-gate`, `asset-compounding`

## Symptom

测试、构建和合并都通过后，用户仍然看到仓库根目录残留旧项目文件夹，并且 IDE 解决方案视图没有按 `src` / `tests` 分组。后续 document lifecycle alignment 完成后也出现同类收口缺口：代码、合并、push 都完成了，但 completed requirement 没有及时写入 `docs/superpowers/archives`，问题门也没有更新相关 problem/inbox 资产。这个交付从命令行角度是可运行的，但从项目导航和知识资产角度仍然不像一次完成的工程闭环。

## Trigger / Context

- 在把 .NET 多项目解决方案迁移到 `src/` 和 `tests/` 后出现。
- 合并前验证重点放在 `dotnet restore/build/test`、Git tracked 文件和子代理审查上。
- 合并后用户从仓库根目录和解决方案视图检查结构，发现旧目录残留和 `.slnx` 仍是扁平项目列表。
- 在 document lifecycle alignment 结束后再次出现：主线开发、审查、验证、合并、push 都完成，但 archive/problem 资产没有同步收口。
- `check_completion_gate.py` 返回 `status=pass` 且无 candidates，被误当成“不需要 archive/problem 判断”的充分证据。

## Root Cause

完成门禁只验证了“Git 跟踪项目已经移动且解决方案可构建”，没有额外验证“本机 ignored 残留是否影响项目导航”和“解决方案文件夹是否与磁盘结构一致”。旧根目录里的 `bin/`、`obj/`、`.csproj.user`、本地 `appsettings.Development.json` 都是 ignored 文件，所以不会出现在普通 `git status` 里；`.slnx` 也可以在不声明 `<Folder>` 的情况下正常构建，但 IDE 视图仍然扁平。

同一类问题也会出现在资产复利上：脚本 completion gate 只能证明结构检查和候选扫描没有报错，不能替代主代理对“完整需求是否需要 archive”“同线程失败模式是否需要 problem/inbox”的判断。把 `status=pass` 当成“无需资产化”，会漏掉已经完成的 requirement archive。

## Fix

- 用 `git status --ignored` 和目录枚举识别根目录旧项目文件夹是 ignored 残留，而不是 tracked 项目未移动。
- 将旧目录里的本地开发配置移动到对应的 `src/` 项目目录。
- 安全删除旧根项目目录，只保留 `src/` 和 `tests/` 下的项目结构。
- 在 `LightRAGNet.slnx` 中增加 `/src/` 和 `/tests/` solution folders，让 IDE 视图与磁盘结构一致。
- 用 `dotnet sln .\LightRAGNet.slnx list` 和 `dotnet test .\LightRAGNet.slnx` 复验。
- 对完成的 Superpowers requirement 额外执行 archive/problem gate，而不是只看 completion gate 脚本输出。
- 为 document lifecycle alignment 补写 archive，并把这次遗漏作为完成门禁问题更新到本 problem 资产。

## Why This Fix

只改 Git tracked 文件不能解决用户看到的本机导航问题；直接删除旧目录又可能误删本地开发配置。先分类 ignored 残留、迁移非构建产物，再删除旧目录，能同时保住本机配置和最终结构。`.slnx` 使用官方可解析的 `<Folder Name="/src/">` / `<Folder Name="/tests/">` 语法，比依赖 IDE 自动按路径分组更稳定。

资产复利同理：自动脚本适合做证据和结构检查，但不能替代人的路由判断。completed requirement 应优先写 archive；同线程稳定失败模式应更新 problem，较弱信号才进 inbox。这个分层能避免“脚本 pass 但知识资产空缺”的假完成。

## Recognition Clues

- `LightRAGNet.slnx` 已指向 `src\...`，但仓库根目录仍出现同名项目文件夹。
- `git ls-files <old-project-dir>` 为空，而 `git status --ignored <old-project-dir>` 显示 `!!`。
- 旧目录内容主要是 `bin/`、`obj/`、`.csproj.user` 或本地 `appsettings.Development.json`。
- `dotnet sln list` 能列项目，但 `.slnx` 里没有 `<Folder Name="/src/">` 或 `<Folder Name="/tests/">`。
- `docs/superpowers/specs/` 和 `docs/superpowers/plans/` 已有同 topic 文件，但 `docs/superpowers/archives/` 没有对应归档。
- 子代理或 completion gate 输出 `asset_candidates: none`，但本轮实际完成了可被未来检索的完整需求。
- 用户在完成后追问“没有做资产归档/问题归档”，说明工程闭环漏了知识资产层。

## Applicability / Non-Applicability

### Applies When

- 对 .NET 多项目仓库做 `src/` / `tests/` 结构迁移。
- 用户会通过 Rider、Visual Studio 或资源管理器检查项目结构。
- 迁移前已经运行过旧路径项目，根目录可能有 ignored `bin/obj` 输出。
- 使用 Superpowers spec/plan/implementation 流程完成了一个可命名需求。
- 仓库已有 `docs/superpowers/archives`、`problems` 或 `inbox`，用户期望资产复利成为收尾的一部分。

### Does Not Apply When

- 仓库只做代码移动，不要求 IDE 解决方案视图分组。
- 旧目录仍包含 tracked 文件；这时应先修正 Git move，而不是清理 ignored 残留。
- 目录是外部参考实现或明确保留的本地资料，例如本仓库的 ignored Python `LightRAG/`。
- 工作只是一次低价值机械改动，没有 spec/plan，也没有可复用交付历史或失败模式。

## Related Artifacts

- Spec: [testability foundation design](../../specs/2026-05-17-testability-foundation-design.md)
- Plan: [testability foundation implementation plan](../../plans/2026-05-17-testability-foundation-implementation-plan.md)
- Archive: [testability foundation archive](../../archives/2026-05/2026-05-17-testability-foundation-archives.md)
- Archive: [document lifecycle alignment archive](../../archives/2026-05/2026-05-18-document-lifecycle-alignment-archives.md)
- Related Problems:
  - `None yet.`
- Code or Test:
  - [LightRAGNet.slnx](../../../../LightRAGNet.slnx)
  - [task queue tests](../../../../tests/LightRAGNet.Tests/TaskQueue/RagTaskQueueServiceTests.cs)
