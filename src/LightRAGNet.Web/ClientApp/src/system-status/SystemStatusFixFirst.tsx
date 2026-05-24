import type { SystemHealthFixFirstItem } from "../api/systemStatusApi";
import { statusClass } from "./SystemStatusSummary";

type SystemStatusFixFirstProps = {
  items: SystemHealthFixFirstItem[];
};

export function SystemStatusFixFirst({ items }: SystemStatusFixFirstProps) {
  return (
    <section className="system-status__panel system-status__fix-first" aria-label="Fix first">
      <div className="system-status__panel-heading">
        <p className="system-status__eyebrow">Fix first</p>
        <h2>Priority actions</h2>
      </div>

      {items.length === 0 ? (
        <p className="system-status__empty">No action required.</p>
      ) : (
        <ol className="system-status__priority-list">
          {items.map((item) => (
            <li key={item.checkId}>
              <div className="system-status__priority-header">
                <div>
                  <h3>{item.title}</h3>
                  <p>{item.checkId}</p>
                </div>
                <span className={statusClass(item.status)}>{item.status}</span>
              </div>
              <p className="system-status__remediation">{item.remediation}</p>
              <p className="system-status__muted">{item.affects.length > 0 ? item.affects.join(", ") : "None"}</p>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
