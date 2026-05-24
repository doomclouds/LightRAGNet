import { ShieldAlert, Trash2, X } from "lucide-react";

import type { CacheClearPlanDto } from "../types/cacheManagement";
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
  return (
    <section className="cache-panel">
      <header className="cache-panel__head">
        <div>
          <h2>Clear plan</h2>
          <p>Risk, entry count, and impact before deletion.</p>
        </div>
      </header>

      {plans.length === 0 ? (
        <div className="cache-empty-state">No clear plan</div>
      ) : (
        <div className="cache-clear-list">
          {plans.map((plan) => {
            const tone = getRiskTone(plan.risk);
            const isPending = pendingPlanId === plan.id;
            const isConfirming = confirmingPlanId === plan.id;

            return (
              <article className={`cache-clear-row cache-clear-row--${tone}`} key={plan.id}>
                <div className="cache-clear-row__body">
                  <div className="cache-clear-row__title">
                    <h3>{plan.title}</h3>
                    <span className={`cache-pill cache-pill--${tone}`}>{plan.risk}</span>
                  </div>
                  <p>{plan.impact}</p>
                  <div className="cache-clear-row__meta">
                    <span>{formatNumber(plan.entryCount)} entries</span>
                    <span>{plan.cacheTypes.join(", ")}</span>
                  </div>
                </div>

                {isConfirming ? (
                  <div className="cache-clear-row__actions">
                    <button
                      className="cache-button cache-button--danger"
                      type="button"
                      onClick={() => onConfirmClear(plan)}
                      disabled={isPending}
                    >
                      <ShieldAlert aria-hidden="true" size={16} />
                      Confirm
                    </button>
                    <button className="cache-icon-button" type="button" onClick={onCancelClear} disabled={isPending}>
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
              </article>
            );
          })}
        </div>
      )}
    </section>
  );
}
