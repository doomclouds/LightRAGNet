import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, test } from "vitest";

const projectRoot = resolve(__dirname, "../../../..");
const srcRoot = resolve(projectRoot, "src");

function readSource(relativePath: string) {
  return readFileSync(resolve(srcRoot, relativePath), "utf8");
}

describe("Graph workbench migration guard", () => {
  test("GraphWorkbench preserves the existing graph panels and query controls", () => {
    const source = readSource("features/graph-workbench/GraphWorkbench.tsx");

    expect(source).toContain("GraphQueryControls");
    expect(source).toContain("GraphSearchBox");
    expect(source).toContain("PropertiesPanel");
  });

  test("GraphViewportControls preserves zoom and fullscreen controls", () => {
    const source = readSource("components/graph/GraphViewportControls.tsx");

    expect(source).toContain("Zoom");
    expect(source).toContain("Fullscreen");
  });

  test("GraphLayoutControls preserves Force Atlas and Circular controls", () => {
    const source = readSource("components/graph/GraphLayoutControls.tsx");

    expect(source).toContain("Force Atlas");
    expect(source).toContain("Circular");
  });
});
