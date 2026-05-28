import { AlertTriangle, Database, Gauge, PhoneOff, Server } from "lucide-react";

import type { CacheSummaryDto } from "@/types/cacheManagement";
import { formatHitRate, formatLatencySaved, formatNumber } from "./formatters";

type Props = {
  summary: CacheSummaryDto;
};

export function CacheSummaryCards({ summary }: Props) {
  const savingsNote =
    summary.estimatedLatencySavedMs === null
      ? "Estimated API calls saved"
      : `${formatLatencySaved(summary.estimatedLatencySavedMs)} latency saved`;

  return (
    <section className="cache-summary-grid" aria-label="Cache summary">
      <article className="cache-metric-card">
        <div className="cache-metric-card__head">
          <span>Cache Hit Rate</span>
          <Gauge aria-hidden="true" size={18} />
        </div>
        <strong className="cache-tone-good">{formatHitRate(summary.overallHitRate)}</strong>
        <p>{summary.measured ? "Backend read outcomes" : "No reads measured"}</p>
        <span className="cache-metric-card__spark cache-metric-card__spark--good" aria-hidden="true" />
      </article>

      <article className="cache-metric-card">
        <div className="cache-metric-card__head">
          <span>Total Requests</span>
          <Server aria-hidden="true" size={18} />
        </div>
        <strong>{formatNumber(summary.providerCallsAvoided + summary.staleOrRiskyEntries)}</strong>
        <p>Measured cache activity</p>
        <span className="cache-metric-card__spark cache-metric-card__spark--good" aria-hidden="true" />
      </article>

      <article className="cache-metric-card">
        <div className="cache-metric-card__head">
          <span>Cache Entries</span>
          <Database aria-hidden="true" size={18} />
        </div>
        <strong>{formatNumber(summary.staleOrRiskyEntries)}</strong>
        <p>Stale or review-needed</p>
        <span className="cache-metric-card__spark cache-metric-card__spark--warn" aria-hidden="true" />
      </article>

      <article className="cache-metric-card">
        <div className="cache-metric-card__head">
          <span>Risky Entries (24h)</span>
          <AlertTriangle aria-hidden="true" size={18} />
        </div>
        <strong className={summary.staleOrRiskyEntries > 0 ? "cache-tone-warn" : "cache-tone-good"}>
          {formatNumber(summary.staleOrRiskyEntries)}
        </strong>
        <p>Review clear policy before deleting</p>
        <span className="cache-metric-card__spark cache-metric-card__spark--danger" aria-hidden="true" />
      </article>

      <article className="cache-metric-card">
        <div className="cache-metric-card__head">
          <span>Savings (24h)</span>
          <PhoneOff aria-hidden="true" size={18} />
        </div>
        <strong className="cache-tone-good">{formatNumber(summary.providerCallsAvoided)}</strong>
        <p>{savingsNote}</p>
        <span className="cache-metric-card__spark cache-metric-card__spark--good" aria-hidden="true" />
      </article>
    </section>
  );
}
