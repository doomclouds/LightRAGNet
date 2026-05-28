import { useEffect, useState } from "react";
import { ShieldAlert, Trash2, X } from "lucide-react";

import type { CacheClearPlanDto } from "@/types/cacheManagement";
import { formatNumber, getRiskTone } from "./formatters";

type Props = {
  plans: CacheClearPlanDto[];
  pendingPlanId: string | null;
  confirmingPlanId: string | null;
  onBeginClear: (plan: CacheClearPlanDto) => void;
  onCancelClear: () => void;
  onConfirmClear: (plan: CacheClearPlanDto) => void;
};

export function CacheClearPlan({
  plans,
  pendingPlanId,
  confirmingPlanId,
  onBeginClear,
  onCancelClear,
  onConfirmClear
}: Props) {
  const [confirmedPlanId, setConfirmedPlanId] = useState<string | null>(null);

  useEffect(() => {
    setConfirmedPlanId(null);
  }, [confirmingPlanId]);

  return (
    <section className="cache-panel">
      <header className="cache-panel__head">
        <div>
          <h2>Clear Plan</h2>
          <p>Previewed entries that match the current policy</p>
        </div>
      </header>

      {plans.length === 0 ? (
        <div className="cache-empty-state">No clear plan</div>
      ) : (
        <div className="cache-table-wrap">
          <table className="cache-table cache-clear-table">
            <thead>
              <tr>
                <th>Cache Family</th>
                <th>Items</th>
                <th>Impact</th>
                <th>Clear Before</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {plans.map((plan) => {
                const tone = getRiskTone(plan.risk);
                const isPending = pendingPlanId === plan.id;
                const isConfirming = confirmingPlanId === plan.id;
                const destructiveClearConfirmed = confirmedPlanId === plan.id;

                return (
                  <tr key={plan.id}>
                    <td>
                      <strong>{plan.title}</strong>
                      <small>{plan.cacheTypes.join(", ")}</small>
                    </td>
                    <td>{formatNumber(plan.entryCount)}</td>
                    <td>
                      <span className={`cache-pill cache-pill--${tone}`}>{plan.risk}</span>
                      <small>{plan.impact}</small>
                    </td>
                    <td>7 days</td>
                    <td>
                      {isConfirming ? (
                        <div className="cache-clear-actions">
                          <label className="cache-clear-confirm">
                            <input
                              type="checkbox"
                              checked={destructiveClearConfirmed}
                              onChange={(event) => setConfirmedPlanId(event.target.checked ? plan.id : null)}
                              disabled={isPending}
                            />
                            <span>Confirm destructive clear</span>
                          </label>
                          <button
                            className="cache-button cache-button--danger"
                            type="button"
                            onClick={() => onConfirmClear(plan)}
                            disabled={isPending || !destructiveClearConfirmed}
                          >
                            <ShieldAlert aria-hidden="true" size={16} />
                            Confirm
                          </button>
                          <button className="cache-icon-button cache-icon-button--small" type="button" onClick={onCancelClear} disabled={isPending}>
                            <X aria-hidden="true" size={16} />
                            <span className="cache-sr-only">Cancel</span>
                          </button>
                        </div>
                      ) : (
                        <button
                          className={tone === "bad" ? "cache-button cache-button--danger" : "cache-button"}
                          type="button"
                          onClick={() => onBeginClear(plan)}
                          disabled={isPending || plan.entryCount === 0}
                        >
                          <Trash2 aria-hidden="true" size={16} />
                          {isPending ? "Clearing" : plan.requiresConfirmation ? "Review" : "Clear"}
                        </button>
                      )}
                    </td>
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
