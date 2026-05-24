import type { SystemHealthCheckResult } from "@/api/systemStatusApi";
import { SystemStatusEvidence } from "./SystemStatusEvidence";
import { statusClass } from "./SystemStatusSummary";

type SystemStatusChecksProps = {
  checks: SystemHealthCheckResult[];
};

export function SystemStatusChecks({ checks }: SystemStatusChecksProps) {
  const checksByCategory = checks.reduce<Map<string, SystemHealthCheckResult[]>>((groups, check) => {
    const categoryChecks = groups.get(check.category) ?? [];
    categoryChecks.push(check);
    groups.set(check.category, categoryChecks);
    return groups;
  }, new Map<string, SystemHealthCheckResult[]>());

  return (
    <section className="system-status__panel system-status__checks" aria-label="Health checks">
      <div className="system-status__panel-heading">
        <p className="system-status__eyebrow">Checks</p>
        <h2>Backend measurements</h2>
      </div>

      {[...checksByCategory.entries()].map(([category, categoryChecks]) => (
        <section className="system-status__check-group" key={category} aria-label={category}>
          <h3>{category}</h3>
          <div className="system-status__check-list">
            {categoryChecks.map((check) => (
              <article className="system-status__check" key={check.id}>
                <div className="system-status__check-header">
                  <div>
                    <h4>{check.name}</h4>
                    <p>{check.message}</p>
                  </div>
                  <span className={statusClass(check.status)}>{check.status}</span>
                </div>
                <dl className="system-status__meta">
                  <div>
                    <dt>Check ID</dt>
                    <dd>{check.id}</dd>
                  </div>
                  <div>
                    <dt>Duration</dt>
                    <dd>{check.durationMs} ms</dd>
                  </div>
                  <div>
                    <dt>Affects</dt>
                    <dd>{check.affects.length > 0 ? check.affects.join(", ") : "None"}</dd>
                  </div>
                </dl>
                {check.remediation ? <p className="system-status__remediation">{check.remediation}</p> : null}
                <SystemStatusEvidence evidence={check.evidence} />
              </article>
            ))}
          </div>
        </section>
      ))}
    </section>
  );
}
