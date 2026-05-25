# Cache Trend Hour Boundary Test Flake

- Date: `2026-05-24`
- Topic slug: `cache-trend-hour-boundary-test-flake`
- Status: `Captured`
- Scope: `Test`
- Tags: `cache-management`, `time-boundary`, `flaky-test`, `verification`

## Symptom

`dotnet test LightRAGNet.slnx` can fail in `CacheManagementServiceTests.GetOverviewAsync_IgnoresNonHitMissReadOutcomesAndSaveMetricsForAttempts` with `overview.Trend.Should().ContainSingle()` reporting two trend points instead of one.

It can also fail later in the same test class with measured cache metrics missing, for example `overview.Summary.Measured` is `false` or family latency estimates are `null`, even though the fixture seeded hit/miss metrics.

## Trigger / Context

- The cache management tests create metric timestamps from `DateTimeOffset.UtcNow.AddMinutes(...)`.
- `CacheManagementService.CreateTrend` groups `24h` trend data by UTC hour.
- The failure appears when the current UTC minute is close enough to the hour boundary that `now.AddMinutes(-6)` and `now.AddMinutes(-5)` land in different hours.
- A partial fix that only replaces metric timestamps with a fixed `StableMetricHour` still leaves `CacheManagementService.GetOverviewAsync` using real `DateTimeOffset.UtcNow` for the selected `24h` read window.
- Once the fixed metric hour becomes older than the service's real rolling 24-hour window, the metrics store correctly filters the seeded events out.

## Root Cause

The test expected deterministic hourly buckets and measured 24-hour cache metrics while part of the fixture still depended on wall-clock time. Near an hour boundary, generated metric timestamps can naturally span two hourly buckets. After fixing only the metric timestamps, the service's real `UtcNow` can advance beyond the fixed timestamps and exclude them from the rolling 24-hour window. Production grouping and filtering are correct; the test fixture did not control every clock boundary involved in the behavior under test.

## Fix

- Replaced `DateTimeOffset.UtcNow` in the affected cache management tests with a fixed `StableMetricHour`.
- Kept all tested offsets inside the same UTC hour for tests that assert a single trend point.
- Made `CacheManagementService` accept an optional `TimeProvider` and use it for `generatedAt`.
- Passed a fixed test `TimeProvider` from `CacheManagementServiceTests.CreateService`, so the metric timestamps and service read window are anchored to the same deterministic time.

## Why This Fix

Anchoring both the seeded metrics and the service clock tests the intended cache trend/window behavior without weakening production logic or changing the hourly grouping contract. Relaxing assertions or only moving the fixed date forward would hide the boundary-sensitive behavior and make the failure recur when the next wall-clock boundary arrives.

## Recognition Clues

- A trend test that usually passes fails only around certain wall-clock minutes.
- The failure shows adjacent hourly timestamps and otherwise correct hit/miss counts.
- The production code groups by `metric.Timestamp.UtcDateTime.Hour`, while the test uses `DateTimeOffset.UtcNow.AddMinutes(...)`.
- Tests with fixed historical metric timestamps fail once real current time has moved beyond their rolling `24h` or `7d` query window.
- Summary or family aggregates report no measured reads despite seeded hit/miss metrics.

## Applicability / Non-Applicability

### Applies When

- Tests assert bucket counts, daily/hourly grouping, retention windows, or boundary-sensitive time math.
- Test data is generated from `UtcNow` / `Now` and then shifted with small minute offsets.
- The intended behavior is not "current time" itself but deterministic aggregation.
- Tests seed fixed timestamps while the system under test computes rolling windows from real current time.

### Does Not Apply When

- The system under test explicitly needs real current time behavior.
- The test already injects the same fixed clock into every component that computes the time window and uses fixed fixture timestamps.
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
