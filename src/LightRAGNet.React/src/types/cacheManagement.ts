export type CacheSummaryDto = {
  overallHitRate: number | null;
  providerCallsAvoided: number;
  estimatedLatencySavedMs: number | null;
  staleOrRiskyEntries: number;
  measured: boolean;
};

export type CacheFamilyDto = {
  cacheType: string;
  displayName: string;
  hitRate: number | null;
  hits: number;
  misses: number;
  attempts: number;
  entryCount: number;
  valueLevel: string;
  riskLevel: string;
  providerCallsAvoided: number;
  estimatedLatencySavedMs: number | null;
  message: string;
};

export type CacheTrendPointDto = {
  timestamp: string;
  hitRate: number | null;
  savedCalls: number;
};

export type CacheInsightDto = {
  title: string;
  message: string;
  level: string;
};

export type CacheClearPlanDto = {
  id: "stale-query-cache" | "summary-cache-review" | "all-llm-cache" | string;
  title: string;
  cacheTypes: string[];
  entryCount: number;
  risk: string;
  impact: string;
  requiresConfirmation: boolean;
};

export type CacheEntrySampleDto = {
  cacheKeyPrefix: string;
  cacheType: string;
  lastHit: string | null;
  state: string;
};

export type CacheOverviewResponse = {
  workspace: string;
  window: string;
  generatedAt: string;
  summary: CacheSummaryDto;
  families: CacheFamilyDto[];
  trend: CacheTrendPointDto[];
  insights: CacheInsightDto[];
  clearPlan: CacheClearPlanDto[];
  entrySamples: CacheEntrySampleDto[];
};

export type CacheClearResponse = {
  succeeded: boolean;
  deletedEntries: number;
  cacheTypes: string[];
  message: string;
  revisionAfter: number | null;
};
