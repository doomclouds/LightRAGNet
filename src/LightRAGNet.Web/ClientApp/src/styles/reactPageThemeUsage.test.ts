/// <reference types="vite/client" />
import { describe, expect, it } from "vitest";

const importNodeModule = <T>(specifier: string) => import(/* @vite-ignore */ specifier) as Promise<T>;

const [{ readFileSync }, { dirname, resolve }, { fileURLToPath }] = await Promise.all([
  importNodeModule<{ readFileSync: (path: string, encoding: "utf8") => string }>("node:fs"),
  importNodeModule<{ dirname: (path: string) => string; resolve: (...pathSegments: string[]) => string }>("node:path"),
  importNodeModule<{ fileURLToPath: (url: string) => string }>("node:url")
]);

function css(name: string): string {
  return readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), name), "utf8");
}

function source(relativePath: string): string {
  return readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), "..", relativePath), "utf8");
}

describe("React page styles use dark-ops tokens", () => {
  it("cache management uses shared tokens for core surfaces", () => {
    const source = css("cache-management.css");
    expect(source).toContain("var(--app-bg)");
    expect(source).toContain("var(--panel-bg)");
    expect(source).toContain("var(--panel-border)");
  });

  it("graph workbench no longer uses the old light shell colors", () => {
    const source = css("graph-workbench.css");
    expect(source).toContain("var(--app-bg)");
    expect(source).toContain("var(--panel-bg)");
    expect(source).not.toContain("background: #eef3f1;");
    expect(source).not.toContain("rgb(255 255 255 / 72%)");
    expect(source).not.toContain("rgba(21, 27, 35, .94)");
    expect(source).not.toContain("rgba(21, 27, 35, .86)");
  });

  it("system status no longer uses the old light shell colors", () => {
    const source = css("system-status.css");
    expect(source).toContain("var(--app-bg)");
    expect(source).toContain("var(--panel-bg)");
    expect(source).not.toContain("background: #f4f6f8;");
    expect(source).not.toContain("background: #fff;");
    expect(source).not.toContain("#0f151d");
  });

  it("graph runtime labels use dark canvas colors", () => {
    const graphCanvas = source("components/graph/GraphCanvas.tsx");
    const graphologyAdapter = source("components/graph/graphologyAdapter.ts");
    const graphRuntimeSource = `${graphCanvas}\n${graphologyAdapter}`;

    expect(graphRuntimeSource).toContain("#edf2f7");
    expect(graphRuntimeSource).not.toContain('color: "#172026"');
    expect(graphRuntimeSource).not.toContain('labelColor: "#172026"');
  });

  it("graph workbench keeps Sigma containers dark without covering render layers", () => {
    const source = css("graph-workbench.css");

    expect(source).toMatch(/--sigma-background-color\s*:\s*var\(--app-bg\)/);
    expect(source).toMatch(/--sigma-controls-background-color\s*:\s*var\(--panel-bg\)/);
    expect(source).toMatch(/\.graph-workbench__sigma\.react-sigma,[^}]*\.graph-workbench__sigma\s+\.sigma-container\s*\{[^}]*background\s*:\s*var\(--app-bg\)/s);
    expect(source).toMatch(/\.graph-workbench__sigma\s+canvas\s*\{[^}]*background\s*:\s*transparent/s);
  });
});
