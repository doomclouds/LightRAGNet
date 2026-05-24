import type { DebugOutputMode, QueryMode, RagQueryRequest } from "@/types/ragChat";

export type QuerySettings = {
  mode: QueryMode;
  streamResponse: boolean;
  includeReferences: boolean;
  enableRerank: boolean;
  topK: number;
  chunkTopK: number;
  responseType: string;
  highLevelKeywordsText: string;
  lowLevelKeywordsText: string;
  debugOutputMode: DebugOutputMode;
};

export const defaultQuerySettings: QuerySettings = {
  mode: "Mix",
  streamResponse: true,
  includeReferences: true,
  enableRerank: true,
  topK: 40,
  chunkTopK: 20,
  responseType: "Multiple Paragraphs",
  highLevelKeywordsText: "",
  lowLevelKeywordsText: "",
  debugOutputMode: "Answer"
};

export function parseKeywords(value: string): string[] {
  const seen = new Set<string>();

  return value
    .split(/[,\n\r，]/)
    .map((item) => item.trim())
    .filter(Boolean)
    .filter((item) => {
      const key = item.toLowerCase();
      if (seen.has(key)) {
        return false;
      }

      seen.add(key);
      return true;
    });
}

export function buildRagQueryRequest(query: string, settings: QuerySettings): RagQueryRequest {
  const isBypassMode = settings.mode === "Bypass";

  return {
    query,
    mode: settings.mode,
    stream: settings.streamResponse,
    includeReferences: !isBypassMode && settings.includeReferences,
    responseType: settings.responseType,
    topK: isBypassMode ? 0 : settings.topK,
    chunkTopK: isBypassMode ? 0 : settings.chunkTopK,
    enableRerank: !isBypassMode && settings.enableRerank,
    highLevelKeywords: isBypassMode ? [] : parseKeywords(settings.highLevelKeywordsText),
    lowLevelKeywords: isBypassMode ? [] : parseKeywords(settings.lowLevelKeywordsText),
    onlyNeedContext: settings.debugOutputMode === "ContextOnly",
    onlyNeedPrompt: settings.debugOutputMode === "PromptOnly"
  };
}
