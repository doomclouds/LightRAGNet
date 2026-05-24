# Cache Trend Hour Boundary Test Flake

- Date: `2026-05-24`
- Topic slug: `cache-trend-hour-boundary-test-flake`
- Status: `Captured`
- Scope: `Test`
- Tags: `cache-management`, `time-boundary`, `flaky-test`, `verification`

## Symptom

`dotnet test LightRAGNet.slnx` can fail in `CacheManagementServiceTests.GetOverviewAsync_IgnoresNonHitMissReadOutcomesAndSaveMetricsForAttempts` with `overview.Trend.Should().ContainSingle()` reporting two trend points instead of one.

## Trigger / Context

- The cache management tests create metric timestamps from `DateTimeOffset.UtcNow.AddMinutes(...)`.
- `CacheManagementService.CreateTrend` groups `24h` trend data by UTC hour.
- The failure appears when the current UTC minute is close enough to the hour boundary that `now.AddMinutes(-6)` and `now.AddMinutes(-5)` land in different hours.

## Root Cause

The test expected a single hourly trend bucket while generating test data relative to wall-clock time. Near an hour boundary, the generated metrics naturally span two hourly buckets, so production grouping is correct and the test fixture is unstable.

## Fix

- Replaced `DateTimeOffset.UtcNow` in the affected cache management tests with a fixed `StableMetricHour`.
- Kept all tested offsets inside the same UTC hour for tests that assert a single trend point.

## Why This Fix

Anchoring the fixture time tests the intended cache trend behavior without weakening production logic or changing the hourly grouping contract. Relaxing the assertion to accept multiple points would hide the specific behavior those tests were written to prove.

## Recognition Clues

- A trend test that usually passes fails only around certain wall-clock minutes.
- The failure shows adjacent hourly timestamps and otherwise correct hit/miss counts.
- The production code groups by `metric.Timestamp.UtcDateTime.Hour`, while the test uses `DateTimeOffset.UtcNow.AddMinutes(...)`.

## Applicability / Non-Applicability

### Applies When

- Tests assert bucket counts, daily/hourly grouping, retention windows, or boundary-sensitive time math.
- Test data is generated from `UtcNow` / `Now` and then shifted with small minute offsets.
- The intended behavior is not "current time" itself but deterministic aggregation.

### Does Not Apply When

- The system under test explicitly needs real current time behavior.
- The test already injects a clock or uses fixed fixture timestamps.
- Multiple buckets are the expected behavior being asserted.

## Related Artifacts

- Spec: [Cache Management Workbench Design](../../specs/2026-05-24-cache-management-workbench-design.md)
- Plan: [Cache Management Workbench Implementation Plan](../../plans/2026-05-24-cache-management-workbench-implementation-plan.md)
- Archive: [Cache Management Workbench](../../archives/2026-05/2026-05-24-cache-management-workbench-archives.md)
- Related Problems:
  - None.
- Code or Test:
  - [CacheManagementServiceTests.cs](../../../../tests/LightRAGNet.Server.Tests/CacheManagementServiceTests.cs)
  - [CacheManagementService.cs](../../../../src/LightRAGNet.Server/Services/CacheManagement/CacheManagementService.cs)
