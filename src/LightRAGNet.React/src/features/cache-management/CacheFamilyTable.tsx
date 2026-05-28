import { Braces, Cpu, Database, FileText, MoreVertical, ScanSearch } from "lucide-react";
import type { ComponentType } from "react";
import type { LucideProps } from "lucide-react";

import type { CacheFamilyDto } from "@/types/cacheManagement";
import { formatHitRate, formatLatencySaved, formatNumber, getRiskTone, getValueTone } from "./formatters";

type Props = {
  families: CacheFamilyDto[];
};

const cacheFamilyIcons: Record<string, ComponentType<LucideProps>> = {
  embedding: Cpu,
  rerank: ScanSearch,
  extract: Braces,
  query: Database,
  keywords: Braces,
  summary: FileText
};

export function CacheFamilyTable({ families }: Props) {
  return (
    <section className="cache-panel cache-family-panel">
      <header className="cache-panel__head">
        <div>
          <h2>Cache Families</h2>
          <p>Operational view across hot cache surfaces</p>
        </div>
        <button className="cache-mini-button" type="button">
          Columns
        </button>
      </header>

      {families.length === 0 ? (
        <div className="cache-empty-state">No cache family data</div>
      ) : (
        <div className="cache-table-wrap">
          <table className="cache-table cache-family-table">
            <thead>
              <tr>
                <th>Cache Family</th>
                <th>Type</th>
                <th>Hit Rate</th>
                <th>Requests (24h)</th>
                <th>Entries</th>
                <th>Trend</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {families.map((family, index) => {
                const hitPercent = family.hitRate === null ? 0 : Math.max(0, Math.min(100, family.hitRate * 100));
                const riskTone = getRiskTone(family.riskLevel);
                const valueTone = getValueTone(family.valueLevel);
                const status = getCacheStatus(riskTone, valueTone);
                const Icon = getFamilyIcon(family.cacheType);

                return (
                  <tr key={family.cacheType}>
                    <td>
                      <div className="cache-family-name">
                        <span className="cache-family-icon">
                          <Icon aria-hidden="true" size={16} />
                        </span>
                        <div>
                          <strong>{family.displayName}</strong>
                          <small>{family.cacheType}</small>
                        </div>
                      </div>
                    </td>
                    <td>
                      <span className="cache-family-type">{family.valueLevel}</span>
                    </td>
                    <td>
                      <div className="cache-rate-cell">
                        <span>{formatHitRate(family.hitRate)}</span>
                        <div className={`cache-rate-bar cache-rate-bar--${status.tone}`} aria-hidden="true">
                          <span style={{ width: `${hitPercent}%` }} />
                        </div>
                      </div>
                    </td>
                    <td>
                      {formatNumber(family.attempts)}
                      <small>{formatNumber(family.providerCallsAvoided)} avoided</small>
                    </td>
                    <td>
                      {formatNumber(family.entryCount)}
                      <small>{formatLatencySaved(family.estimatedLatencySavedMs)}</small>
                    </td>
                    <td>
                      <MiniSparkline tone={status.tone} seed={index} hitPercent={hitPercent} />
                    </td>
                    <td>
                      <span className={`cache-pill cache-pill--${status.tone}`}>{status.label}</span>
                    </td>
                    <td>
                      <button className="cache-icon-button cache-icon-button--small" type="button" aria-label={`Open ${family.displayName} actions`}>
                        <MoreVertical aria-hidden="true" size={16} />
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function getFamilyIcon(cacheType: string) {
  const normalized = cacheType.toLowerCase();
  const match = Object.entries(cacheFamilyIcons).find(([key]) => normalized.includes(key));
  return match?.[1] ?? Database;
}

function getCacheStatus(
  riskTone: "good" | "info" | "warn" | "bad" | "neutral",
  valueTone: "good" | "info" | "warn" | "bad" | "neutral"
) {
  if (riskTone === "bad") {
    return { label: "Critical", tone: "bad" as const };
  }

  if (riskTone === "warn" || valueTone === "warn") {
    return { label: "Warning", tone: "warn" as const };
  }

  return { label: "Healthy", tone: "good" as const };
}

function MiniSparkline({ tone, seed, hitPercent }: { tone: "good" | "warn" | "bad"; seed: number; hitPercent: number }) {
  const base = 32 - Math.max(8, Math.min(26, hitPercent / 4));
  const points = [0, 13, 26, 39, 52, 65]
    .map((x, index) => `${x},${Math.max(6, Math.min(30, base + Math.sin(seed + index) * 5 + index * (tone === "bad" ? 1.1 : -0.8)))}`)
    .join(" ");

  return (
    <svg className={`cache-mini-spark cache-mini-spark--${tone}`} viewBox="0 0 68 34" preserveAspectRatio="none" aria-hidden="true">
      <polyline points={points} />
    </svg>
  );
}
