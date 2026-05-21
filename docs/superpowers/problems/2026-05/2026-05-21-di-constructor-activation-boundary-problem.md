# DI Constructor Activation Boundary

- Date: `2026-05-21`
- Topic slug: `di-constructor-activation-boundary`
- Status: `Captured`
- Scope: `Repo`
- Tags: `di`, `constructor`, `internalsvisibleto`, `service-provider`, `test-coverage`

## Symptom

Rerank coordinator 接入后，局部单测可以通过，但扩大测试范围时出现两类运行时失败：

- 多个旧调用点仍然构造 `NaiveQueryService(vectorStore, rerankService, tokenizer)`，测试运行到构造器时抛出 `InvalidCastException`。
- Hosting DI 解析 `RetrievalContextService` 时失败，错误提示找不到 suitable constructor。

## Trigger / Context

这类问题通常发生在服务构造器从直接依赖切到内部协调器或内部聚合服务时：

- 生产类需要隐藏新的内部协作者，例如 `RerankCoordinator`。
- 测试项目通过 `InternalsVisibleTo` 可以直接调用 internal 构造器。
- Hosting 项目仍使用默认 `services.AddSingleton<TImplementation>()` 类型激活。
- 旧测试或旧代码还保留原构造签名的直接 new 调用。

## Root Cause

为了保持公共 API 表面不扩大，第一次修复试图用弱类型 `object` 构造器桥接旧调用点。这个做法把应当编译期暴露的迁移遗漏变成了运行时强转失败：旧调用点仍能编译，但传入的 `IRerankService` 会在构造器里被错误强转为 `RerankCoordinator`。

第二个边界是 Microsoft DI 默认类型激活只考虑可见的合适构造器。`InternalsVisibleTo("LightRAGNet.Hosting")` 能让 Hosting 代码编译访问 internal 构造器，但不能让 `AddSingleton<TImplementation>()` 的反射激活自动选择 internal 构造器。

## Fix

- 移除 `NaiveQueryService` 的弱类型 `object` 构造器，只保留强类型 `RerankCoordinator` internal 构造器。
- 迁移所有旧调用点，显式创建 `RerankCoordinator`。
- 在测试中锁住构造器表面：不能重新暴露 `IRerankService` 构造器，也不能保留 `object` 桥接构造器。
- 给 `LightRAGNet.Hosting` 增加 `InternalsVisibleTo`。
- 在 Hosting 注册中改用 factory 显式创建 `NaiveQueryService` 和 `RetrievalContextService`。
- 增加 `AddLightRag_CanResolveRetrievalContextService` 回归测试，用真实 `ServiceProvider` 覆盖 Hosting DI 图。

## Why This Fix

强类型 internal 构造器会让遗漏的调用点在编译期暴露，而不是拖到运行时失败。Hosting factory 则把 internal 构造器访问放在编译期代码里，不依赖默认 DI 激活器是否能看到 internal 构造器。最后用 ServiceProvider 级别测试固定真实注册图，避免只测单个类构造而漏掉 Hosting 运行时边界。

## Recognition Clues

- 为了保持 public API 干净，新增了 `object`、optional parameter 或兼容构造器。
- 单测项目可以 new internal 构造器，但应用启动或 ServiceProvider 解析失败。
- `AddSingleton<TImplementation>()` 注册的实现类型只有 internal 构造器。
- 改构造器后局部测试通过，扩大到旧调用点时出现 `InvalidCastException`。
- `InternalsVisibleTo` 已配置，但 DI 仍提示找不到 suitable constructor。

## Applicability / Non-Applicability

### Applies When

- 生产服务要切换到内部 coordinator / pipeline / facade。
- 构造器可见性被用于控制 API 表面。
- Hosting 或测试通过 Microsoft.Extensions.DependencyInjection 默认激活类型。
- 需要同时保护 public API、编译期迁移压力和运行时 DI 图。

### Does Not Apply When

- 服务构造器本来就是 public 且作为稳定扩展点公开。
- DI 注册始终使用显式 factory，并且有 ServiceProvider 级别回归测试覆盖。
- 失败来自缺少某个外部 provider 注册，而不是构造器可见性或弱类型桥接。

## Related Artifacts

- Spec: [rerank chunking parity design](../../specs/2026-05-21-rerank-chunking-parity-design.md)
- Plan: [rerank chunking parity implementation plan](../../plans/2026-05-21-rerank-chunking-parity-implementation-plan.md)
- Archive: [rerank chunking parity archive](../../archives/2026-05/2026-05-21-rerank-chunking-parity-archives.md)
- Code or Test:
  - [NaiveQueryService.cs](../../../../src/LightRAGNet/Services/Query/NaiveQueryService.cs)
  - [RetrievalContextService.cs](../../../../src/LightRAGNet/Services/RetrievalContext/RetrievalContextService.cs)
  - [ServiceCollectionExtensions.cs](../../../../src/LightRAGNet.Hosting/ServiceCollectionExtensions.cs)
  - [NaiveQueryServiceTests.cs](../../../../tests/LightRAGNet.Tests/Query/NaiveQueryServiceTests.cs)
  - [LightRagHostingRegistrationTests.cs](../../../../tests/LightRAGNet.Tests/Query/LightRagHostingRegistrationTests.cs)
