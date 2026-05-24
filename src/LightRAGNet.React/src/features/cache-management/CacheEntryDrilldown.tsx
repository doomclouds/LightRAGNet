import type { CacheEntrySampleDto } from "@/types/cacheManagement";
import { formatDateTime } from "./formatters";

type Props = {
  entries: CacheEntrySampleDto[];
};

export function CacheEntryDrilldown({ entries }: Props) {
  return (
    <section className="cache-panel">
      <header className="cache-panel__head">
        <div>
          <h2>Entry samples</h2>
          <p>Key prefix, type, state, and last hit.</p>
        </div>
      </header>

      {entries.length === 0 ? (
        <div className="cache-empty-state">No entry samples</div>
      ) : (
        <div className="cache-table-wrap">
          <table className="cache-table cache-table--compact">
            <thead>
              <tr>
                <th>Key prefix</th>
                <th>Type</th>
                <th>State</th>
                <th>Last hit</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry) => (
                <tr key={`${entry.cacheType}-${entry.cacheKeyPrefix}`}>
                  <td className="cache-key-prefix">{entry.cacheKeyPrefix}</td>
                  <td>{entry.cacheType}</td>
                  <td>{entry.state}</td>
                  <td>{formatDateTime(entry.lastHit)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
