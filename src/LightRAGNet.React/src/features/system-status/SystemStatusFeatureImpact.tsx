import type { SystemHealthFeatureImpact } from "@/api/systemStatusApi";
import { statusClass } from "./SystemStatusSummary";

type SystemStatusFeatureImpactProps = {
  items: SystemHealthFeatureImpact[];
};

export function SystemStatusFeatureImpact({ items }: SystemStatusFeatureImpactProps) {
  return (
    <section className="system-status__panel system-status__feature-impact" aria-label="Feature impact">
      <div className="system-status__panel-heading">
        <p className="system-status__eyebrow">Feature impact</p>
        <h2>User-facing effects</h2>
      </div>

      {items.length === 0 ? (
        <p className="system-status__empty">No feature impacts reported.</p>
      ) : (
        <div className="system-status__impact-list">
          {items.map((item) => (
            <article className="system-status__impact" key={item.feature}>
              <div className="system-status__impact-header">
                <h3>{item.feature}</h3>
                <span className={statusClass(item.status)}>{item.status}</span>
              </div>
              <p>{item.reason}</p>
              <dl className="system-status__meta">
                <div>
                  <dt>Affected by</dt>
                  <dd>{item.affectedBy.length > 0 ? item.affectedBy.join(", ") : "None"}</dd>
                </div>
              </dl>
              {item.links.length > 0 ? (
                <div className="system-status__links">
                  {item.links.map((link) => (
                    <a href={link.href} key={`${item.feature}-${link.href}`}>
                      {link.label}
                    </a>
                  ))}
                </div>
              ) : null}
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
