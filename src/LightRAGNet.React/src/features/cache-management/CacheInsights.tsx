import { CircleAlert, CircleCheck, Info } from "lucide-react";

import type { CacheInsightDto } from "@/types/cacheManagement";
import { normalizeTone } from "./formatters";

type Props = {
  insights: CacheInsightDto[];
};

function iconForLevel(level: string) {
  const tone = normalizeTone(level);

  if (tone === "good") {
    return <CircleCheck aria-hidden="true" size={18} />;
  }

  if (tone === "warn" || tone === "bad") {
    return <CircleAlert aria-hidden="true" size={18} />;
  }

  return <Info aria-hidden="true" size={18} />;
}

export function CacheInsights({ insights }: Props) {
  return (
    <section className="cache-panel">
      <header className="cache-panel__head">
        <div>
          <h2>Insights</h2>
          <p>Backend-ranked maintenance signals.</p>
        </div>
      </header>

      {insights.length === 0 ? (
        <div className="cache-empty-state">No insight data</div>
      ) : (
        <div className="cache-insight-list">
          {insights.map((insight) => {
            const tone = normalizeTone(insight.level);

            return (
              <article className={`cache-insight cache-insight--${tone}`} key={`${insight.title}-${insight.message}`}>
                <span className="cache-insight__icon">{iconForLevel(insight.level)}</span>
                <div>
                  <h3>{insight.title}</h3>
                  <p>{insight.message}</p>
                </div>
              </article>
            );
          })}
        </div>
      )}
    </section>
  );
}
