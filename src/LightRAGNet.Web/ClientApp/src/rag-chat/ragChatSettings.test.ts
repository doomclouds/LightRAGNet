import { describe, expect, test } from "vitest";

import { buildRagQueryRequest, defaultQuerySettings, parseKeywords } from "./ragChatSettings";

describe("rag chat settings", () => {
  test("parseKeywords splits comma Chinese comma and newline values", () => {
    expect(parseKeywords("alpha, beta\nALPHA，gamma\r\nBeta")).toEqual(["alpha", "beta", "gamma"]);
  });

  test("buildRagQueryRequest disables references in bypass mode", () => {
    const request = buildRagQueryRequest("hello", {
      ...defaultQuerySettings,
      mode: "Bypass",
      includeReferences: true,
      highLevelKeywordsText: "system",
      lowLevelKeywordsText: "queue",
      debugOutputMode: "PromptOnly"
    });

    expect(request).toMatchObject({
      query: "hello",
      mode: "Bypass",
      stream: true,
      includeReferences: false,
      onlyNeedContext: false,
      onlyNeedPrompt: true,
      highLevelKeywords: ["system"],
      lowLevelKeywords: ["queue"]
    });
  });

  test.each([
    ["Answer", false, false],
    ["ContextOnly", true, false],
    ["PromptOnly", false, true]
  ] as const)("buildRagQueryRequest maps %s debug output mode", (debugOutputMode, onlyNeedContext, onlyNeedPrompt) => {
    const request = buildRagQueryRequest("debug", {
      ...defaultQuerySettings,
      debugOutputMode
    });

    expect(request.onlyNeedContext).toBe(onlyNeedContext);
    expect(request.onlyNeedPrompt).toBe(onlyNeedPrompt);
  });
});
