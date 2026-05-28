import { ListChecks, ShieldCheck } from "lucide-react";

type Props = {
  planCount: number;
};

export function CacheClearPolicy({ planCount }: Props) {
  return (
    <section className="cache-panel">
      <header className="cache-panel__head">
        <div>
          <h2>Clear Policy</h2>
          <p>Generate a safe clear preview before execution</p>
        </div>
      </header>

      <div className="cache-policy">
        <label className="cache-policy__field">
          <span>Strategy</span>
          <select defaultValue="latest">
            <option value="latest">Keep Latest</option>
            <option value="least-hit">Remove Least Hit</option>
            <option value="expired">Expired Only</option>
          </select>
        </label>

        <label className="cache-policy__field">
          <span>Keep Duration</span>
          <input readOnly value="7 Days" />
        </label>

        <label className="cache-policy__field">
          <span>Minimum Hit Rate</span>
          <input readOnly value="70%" />
        </label>

        <div className="cache-policy__note">
          <ShieldCheck aria-hidden="true" size={16} />
          <p>
            <strong>Preview first</strong>
            {planCount} families match this policy. Execution remains manual.
          </p>
        </div>

        <button className="cache-button cache-button--primary cache-policy__preview" type="button">
          <ListChecks aria-hidden="true" size={16} />
          Preview Plan ({planCount})
        </button>
      </div>
    </section>
  );
}
