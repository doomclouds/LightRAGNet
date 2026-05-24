import type { CacheFamilyDto } from "@/types/cacheManagement";
import { formatHitRate, formatLatencySaved, formatNumber, getRiskTone, getValueTone } from "./formatters";

type Props = {
  families: CacheFamilyDto[];
};

export function CacheFamilyTable({ families }: Props) {
  return (
    <section className="cache-panel cache-family-panel">
      <header className="cache-panel__head">
        <div>
          <h2>Cache families</h2>
          <p>Hit rate, value, risk, and retained entries by cache type.</p>
        </div>
      </header>

      {families.length === 0 ? (
        <div className="cache-empty-state">No cache family data</div>
      ) : (
        <div className="cache-table-wrap">
          <table className="cache-table">
            <thead>
              <tr>
                <th>Cache type</th>
                <th>Hit rate</th>
                <th>Hits / attempts</th>
                <th>Entries</th>
                <th>Value</th>
                <th>Risk</th>
                <th>Latency</th>
              </tr>
            </thead>
            <tbody>
              {families.map((family) => {
                const hitPercent = family.hitRate === null ? 0 : Math.max(0, Math.min(100, family.hitRate * 100));
                const riskTone = getRiskTone(family.riskLevel);
                const valueTone = getValueTone(family.valueLevel);

                return (
                  <tr key={family.cacheType}>
                    <td>
                      <div className="cache-family-name">
                        <span className={`cache-dot cache-dot--${valueTone}`} />
                        <div>
                          <strong>{family.displayName}</strong>
                          <small>{family.cacheType}</small>
                        </div>
                      </div>
                    </td>
                    <td>
                      <div className="cache-rate-cell">
                        <div className="cache-rate-bar" aria-hidden="true">
                          <span style={{ width: `${hitPercent}%` }} />
                        </div>
                        <span>{formatHitRate(family.hitRate)}</span>
                      </div>
                    </td>
                    <td>
                      {formatNumber(family.hits)} / {formatNumber(family.attempts)}
                      <small>{formatNumber(family.providerCallsAvoided)} avoided</small>
                    </td>
                    <td>{formatNumber(family.entryCount)}</td>
                    <td>
                      <span className={`cache-pill cache-pill--${valueTone}`}>{family.valueLevel}</span>
                    </td>
                    <td>
                      <span className={`cache-pill cache-pill--${riskTone}`}>{family.riskLevel}</span>
                    </td>
                    <td>{formatLatencySaved(family.estimatedLatencySavedMs)}</td>
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
