import type { CacheTrendPointDto } from "../types/cacheManagement";
import { formatDateTime, formatHitRate, formatNumber } from "./formatters";

type Props = {
  trend: CacheTrendPointDto[];
};

export function CacheEfficiencyTrend({ trend }: Props) {
  const maxSavedCalls = Math.max(1, ...trend.map((point) => point.savedCalls));

  return (
    <section className="cache-panel">
      <header className="cache-panel__head">
        <div>
          <h2>Efficiency trend</h2>
          <p>Saved calls and hit rate over the selected window.</p>
        </div>
      </header>

      {trend.length === 0 ? (
        <div className="cache-empty-state">No trend data</div>
      ) : (
        <div className="cache-trend" role="list">
          {trend.map((point) => {
            const savedRatio = Math.max(0.12, point.savedCalls / maxSavedCalls);
            const hitRatio = point.hitRate === null ? 0.12 : Math.max(0.12, Math.min(1, point.hitRate));

            return (
              <div className="cache-trend__item" key={point.timestamp} role="listitem">
                <div className="cache-trend__bars">
                  <span
                    className="cache-trend__bar cache-trend__bar--saved"
                    style={{ height: `${savedRatio * 100}%` }}
                    title={`${formatNumber(point.savedCalls)} saved calls`}
                  />
                  <span
                    className="cache-trend__bar cache-trend__bar--rate"
                    style={{ height: `${hitRatio * 100}%` }}
                    title={`${formatHitRate(point.hitRate)} hit rate`}
                  />
                </div>
                <small>{formatDateTime(point.timestamp)}</small>
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
}
