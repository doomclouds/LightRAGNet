import { Activity, Database, ShieldCheck } from "lucide-react";

export function CacheMeasurementContract() {
  return (
    <section className="cache-panel">
      <header className="cache-panel__head">
        <div>
          <h2>Measurement</h2>
          <p>Metric source and visibility boundaries.</p>
        </div>
      </header>
      <dl className="cache-contract-list">
        <div>
          <dt>
            <Activity aria-hidden="true" size={16} />
            Hit rate
          </dt>
          <dd>read hit / read attempt</dd>
        </div>
        <div>
          <dt>
            <Database aria-hidden="true" size={16} />
            Entries
          </dt>
          <dd>llm_cache inventory snapshot</dd>
        </div>
        <div>
          <dt>
            <ShieldCheck aria-hidden="true" size={16} />
            Samples
          </dt>
          <dd>prefix and state only</dd>
        </div>
      </dl>
    </section>
  );
}
