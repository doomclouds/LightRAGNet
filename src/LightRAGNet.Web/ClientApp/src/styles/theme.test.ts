/// <reference types="vite/client" />
import { describe, expect, it } from "vitest";

const importNodeModule = <T>(specifier: string) => import(/* @vite-ignore */ specifier) as Promise<T>;

const [{ readFileSync }, { dirname, resolve }, { fileURLToPath }] = await Promise.all([
  importNodeModule<{ readFileSync: (path: string, encoding: "utf8") => string }>("node:fs"),
  importNodeModule<{ dirname: (path: string) => string; resolve: (...pathSegments: string[]) => string }>("node:path"),
  importNodeModule<{ fileURLToPath: (url: string) => string }>("node:url")
]);

const themeCss = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), "theme.css"), "utf8");

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function expectTokenDeclaration(token: string): void {
  expect(themeCss).toMatch(new RegExp(`${escapeRegExp(token)}\\s*:`));
}

function expectSelector(selector: string): void {
  expect(themeCss).toMatch(new RegExp(`(^|[\\s,{])${escapeRegExp(selector)}(?=\\s|,|\\{|:)`, "m"));
}

describe("dark-ops theme", () => {
  it("defines the shared semantic tokens used by React pages", () => {
    [
      "--app-bg",
      "--panel-bg",
      "--panel-bg-elevated",
      "--panel-border",
      "--text-primary",
      "--text-secondary",
      "--text-muted",
      "--accent",
      "--accent-soft",
      "--accent-border",
      "--danger",
      "--danger-soft",
      "--warning",
      "--warning-soft",
      "--success",
      "--success-soft",
      "--control-bg",
      "--control-border",
      "--shadow-panel",
      "--radius-panel",
      "--radius-control"
    ].forEach(expectTokenDeclaration);
  });

  it("defines reusable page primitives", () => {
    [
      ".lrn-app",
      ".lrn-page-head",
      ".lrn-page-meta",
      ".lrn-panel",
      ".lrn-panel__head",
      ".lrn-button",
      ".lrn-icon-button",
      ".lrn-button--accent",
      ".lrn-button--danger",
      ".lrn-input",
      ".lrn-textarea",
      ".lrn-select",
      ".lrn-chip",
      ".lrn-dialog",
      ".lrn-code-surface"
    ].forEach(expectSelector);
  });

  it("scopes the dark UA color scheme to themed React islands", () => {
    const rootBlock = themeCss.match(/:root\s*\{[^}]*\}/)?.[0] ?? "";
    const appBlock = themeCss.match(/\.lrn-app\s*\{[^}]*\}/)?.[0] ?? "";

    expect(rootBlock).not.toMatch(/color-scheme\s*:/);
    expect(appBlock).toMatch(/color-scheme\s*:\s*dark\s*;/);
  });
});
