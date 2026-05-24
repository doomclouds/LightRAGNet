import { resolve } from "node:path";
import { readFileSync } from "node:fs";
import { describe, expect, test } from "vitest";

const systemStatusWorkbenchPath = resolve(process.cwd(), "src/features/system-status/SystemStatusWorkbench.tsx");

describe("SystemStatusWorkbench source guard", () => {
  test("uses server-provided health aggregation fields without local aggregation", () => {
    const source = readFileSync(systemStatusWorkbenchPath, "utf8");

    expect(source).toContain("health.status");
    expect(source).toContain("health.fixFirst");
    expect(source).toContain("health.featureImpacts");
    expect(source).not.toMatch(/\b(?:const|let|var)\s+fixFirst\s*=/);
    expect(source).not.toMatch(/\b(?:const|let|var)\s+overallStatus\s*=/);
  });
});
