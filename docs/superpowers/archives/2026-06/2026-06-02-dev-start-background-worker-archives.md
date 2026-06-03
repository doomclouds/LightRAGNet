# Dev Start Background Worker

- Date: `2026-06-02`
- Topic slug: `dev-start-background-worker`
- Status: `Archived`
- Scope: `Environment`
- Tags: `dev-start`, `powershell`, `background-worker`, `developer-experience`, `react`

## Summary

本次交付最初把开发启动脚本从“当前控制台同步准备服务”改为默认后台启动：隐藏 worker 继续完成 React/Server 准备、ready 等待和状态写入，现有 `dev-stop.ps1` 作为统一停止入口。2026-06-03 追加修正后，默认行为恢复为当前控制台等待 ready 并打印可访问 URL；后台 worker 能力保留为显式 `-Background` 模式。

## Delivered Scope

- `scripts/dev-start.ps1` 最初默认拉起隐藏的 starter worker，并把 worker stdout/stderr 写入 `artifacts/dev-runtime/logs/`。
- 新增 `-Foreground` 兼容模式，保留当前控制台同步启动流程用于排障；2026-06-03 后同步等待成为默认行为。
- 新增 `dev-start-worker.json` 运行状态，避免重复启动多个 starter worker。
- `scripts/dev-stop.ps1` 支持停止尚未完成启动流程的 starter worker，并继续清理已写入状态的开发服务。
- `README.md` 与 `README.EN.md` 最初补充默认后台启动、日志位置和 `-Foreground` 用法说明。
- Follow-up: `scripts/dev-start.ps1` 默认行为改回同步等待 ready 并在当前控制台打印 Server / React URL；隐藏 worker 改为显式 `-Background`，`-Foreground` 仅保留兼容。
- Follow-up: `README.md` 与 `README.EN.md` 改为推荐默认等待 ready，只有需要立即返回控制台时才使用 `-Background`。

## Out of Scope

- 不改变 Server、React、Qdrant 或 Neo4j 的默认端口。
- 不引入新的 PowerShell 依赖或 Pester 测试框架。
- 不改变 `dev-start.sh` / `dev-stop.sh` 的包装入口；它们继续转发到 PowerShell 脚本。

## Verification Snapshot

- Red check: current default `dev-start.ps1 -Target React -SkipNpmInstall -SkipClientBuild` did not create background worker state and held the current script until React ready.
- Background check: updated default `dev-start.ps1 -Target React -SkipNpmInstall -SkipClientBuild -ReadyTimeoutSeconds 20` returned in `128 ms`, created `dev-start-worker.json`, and `dev-stop.ps1` stopped both starter worker and React.
- Foreground compatibility: `dev-start.ps1 -Foreground -Target React -SkipNpmInstall -SkipClientBuild -ReadyTimeoutSeconds 20` kept the synchronous flow and `http://127.0.0.1:5173/documents` returned `200`.
- Full background path: default background start made `http://127.0.0.1:5173/documents` return `200`, then `dev-stop.ps1` cleaned the running React process.
- Follow-up red check: `ReactDevCorsSourceTests.DevStart_DefaultPathWaitsForReadyAndPrintsUrls` failed against the default-background script because `[switch]$Background` was absent and the old `if (-not $Foreground -and -not $Worker)` branch still intercepted default startup.
- Follow-up green check: default `.\scripts\dev-start.ps1 -Target React -SkipNpmInstall -SkipClientBuild -ReadyTimeoutSeconds 30` waited until `http://127.0.0.1:5173/documents` was ready, printed the React URL list in the current console, and returned after `Invoke-WebRequest` confirmed `200 OK`.
- Follow-up compatibility check: explicit `.\scripts\dev-start.ps1 -Background -Target React -SkipNpmInstall -SkipClientBuild -ReadyTimeoutSeconds 30` returned in `187 ms`, created `dev-start-worker.json`, and `dev-stop.ps1` stopped React.

## Source Documents

- Spec: None found for this topic.
- Visual: None found for this topic.
- Plan: None found for this topic.
- Code: [dev-start.ps1](../../../../scripts/dev-start.ps1), [dev-stop.ps1](../../../../scripts/dev-stop.ps1)
- Docs: [README.md](../../../../README.md), [README.EN.md](../../../../README.EN.md)

## Related Problems

- None yet.

## Notes

- 这条 archive 是用户临时开发体验需求的收口记录；没有单独 spec/plan 家族。
