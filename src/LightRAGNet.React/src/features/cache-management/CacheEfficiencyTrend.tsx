import type { CacheTrendPointDto } from "@/types/cacheManagement";

type Props = {
  trend: CacheTrendPointDto[];
  window: string;
};

export function CacheEfficiencyTrend({ trend, window }: Props) {
  const polyline = buildTrendPolyline(trend);
  const area = polyline ? `${polyline} 408,172 8,172` : "";

  return (
    <section className="cache-panel">
      <header className="cache-panel__head">
        <div>
          <h2>Hit Rate Trend ({formatWindowLabel(window)})</h2>
          <p>Hourly average across all cache families</p>
        </div>
      </header>

      {trend.length === 0 ? (
        <div className="cache-empty-state">No trend data</div>
      ) : (
        <div className="cache-line-chart">
          <svg viewBox="0 0 420 190" preserveAspectRatio="none" aria-label="Cache hit rate trend">
            <line className="cache-line-chart__grid" x1="0" y1="36" x2="420" y2="36" />
            <line className="cache-line-chart__grid" x1="0" y1="82" x2="420" y2="82" />
            <line className="cache-line-chart__grid" x1="0" y1="128" x2="420" y2="128" />
            <polygon className="cache-line-chart__area" points={area} />
            <polyline className="cache-line-chart__line" points={polyline} />
            <text className="cache-line-chart__label" x="6" y="185">00:00</text>
            <text className="cache-line-chart__label" x="174" y="185">12:00</text>
            <text className="cache-line-chart__label" x="366" y="185">Now</text>
          </svg>
        </div>
      )}
    </section>
  );
}

function buildTrendPolyline(trend: CacheTrendPointDto[]): string {
  if (trend.length === 0) {
    return "";
  }

  const maxIndex = Math.max(1, trend.length - 1);

  return trend
    .map((point, index) => {
      const hitRate = point.hitRate ?? 0;
      const x = 8 + (index / maxIndex) * 400;
      const y = 172 - Math.max(0.05, Math.min(1, hitRate)) * 146;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(" ");
}

function formatWindowLabel(window: string): string {
  if (window.toLowerCase().endsWith("h")) {
    return window.toLowerCase();
  }

  return window.toUpperCase();
}
