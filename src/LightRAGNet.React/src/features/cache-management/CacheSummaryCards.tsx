import { AlertTriangle, Gauge, PhoneOff, TimerReset } from "lucide-react";

import type { CacheSummaryDto } from "@/types/cacheManagement";
import { formatHitRate, formatLatencySaved, formatNumber } from "./formatters";

type Props = {
  summary: CacheSummaryDto;
};

export function CacheSummaryCards({ summary }: Props) {
  return (
    <section className="cache-summary-grid" aria-label="Cache summary">
      <article className="cache-metric-card">
        <div className="cache-metric-card__head">
          <span>Overall hit rate</span>
          <Gauge aria-hidden="true" size={18} />
        </div>
        <strong className="cache-tone-good">{formatHitRate(summary.overallHitRate)}</strong>
        <p>{summary.measured ? "Backend read outcomes" : "No reads measured"}</p>
      </article>

      <article className="cache-metric-card">
        <div className="cache-metric-card__head">
          <span>Calls avoided</span>
          <PhoneOff aria-hidden="true" size={18} />
        </div>
        <strong className="cache-tone-info">{formatNumber(summary.providerCallsAvoided)}</strong>
        <p>Provider calls saved</p>
      </article>

      <article className="cache-metric-card">
        <div className="cache-metric-card__head">
          <span>Latency saved</span>
          <TimerReset aria-hidden="true" size={18} />
        </div>
        <strong className="cache-tone-good">{formatLatencySaved(summary.estimatedLatencySavedMs)}</strong>
        <p>Estimated from misses</p>
      </article>

      <article className="cache-metric-card">
        <div className="cache-metric-card__head">
          <span>Risky entries</span>
          <AlertTriangle aria-hidden="true" size={18} />
        </div>
        <strong className={summary.staleOrRiskyEntries > 0 ? "cache-tone-warn" : "cache-tone-good"}>
          {formatNumber(summary.staleOrRiskyEntries)}
        </strong>
        <p>Stale or review-needed</p>
      </article>
    </section>
  );
}
