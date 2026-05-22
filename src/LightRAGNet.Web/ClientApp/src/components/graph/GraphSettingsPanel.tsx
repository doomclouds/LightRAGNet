import { Settings } from "lucide-react";
import { useState } from "react";

import { useGraphSettingsStore } from "../../stores/graphSettingsStore";

function clampNumber(value: number, min: number, max: number): number {
  if (!Number.isFinite(value)) {
    return min;
  }

  return Math.min(max, Math.max(min, value));
}

export function GraphSettingsPanel() {
  const settings = useGraphSettingsStore();
  const [isOpen, setIsOpen] = useState(false);

  return (
    <div className="graph-workbench__settings-control">
      <button
        className={`graph-workbench__icon-button${isOpen ? " graph-workbench__icon-button--active" : ""}`}
        title="Graph settings"
        type="button"
        onClick={() => setIsOpen((value) => !value)}
      >
        <Settings aria-hidden="true" size={17} />
      </button>
      {isOpen ? (
        <section className="graph-workbench__settings-panel" aria-label="Graph settings">
          <label>
            <input
              checked={settings.showNodeLabels}
              type="checkbox"
              onChange={(event) => useGraphSettingsStore.setShowNodeLabels(event.currentTarget.checked)}
            />
            <span>Node labels</span>
          </label>
          <label>
            <input
              checked={settings.showEdgeLabels}
              type="checkbox"
              onChange={(event) => useGraphSettingsStore.setShowEdgeLabels(event.currentTarget.checked)}
            />
            <span>Edge labels</span>
          </label>
          <label>
            <input
              checked={settings.enableEdgeEvents}
              type="checkbox"
              onChange={(event) => useGraphSettingsStore.setEnableEdgeEvents(event.currentTarget.checked)}
            />
            <span>Edge events</span>
          </label>
          <label>
            <input
              checked={settings.hideUnselectedEdges}
              type="checkbox"
              onChange={(event) => useGraphSettingsStore.setHideUnselectedEdges(event.currentTarget.checked)}
            />
            <span>Hide unrelated edges</span>
          </label>
          <label className="graph-workbench__range-field">
            <span>Min edge size</span>
            <input
              max="8"
              min="0.5"
              step="0.25"
              type="number"
              value={settings.minEdgeSize}
              onChange={(event) =>
                useGraphSettingsStore.setMinEdgeSize(clampNumber(event.currentTarget.valueAsNumber, 0.5, 8))
              }
            />
          </label>
          <label className="graph-workbench__range-field">
            <span>Max edge size</span>
            <input
              max="12"
              min="1"
              step="0.25"
              type="number"
              value={settings.maxEdgeSize}
              onChange={(event) =>
                useGraphSettingsStore.setMaxEdgeSize(clampNumber(event.currentTarget.valueAsNumber, 1, 12))
              }
            />
          </label>
          <label className="graph-workbench__range-field">
            <span>Layout iterations</span>
            <input
              max="800"
              min="20"
              step="10"
              type="number"
              value={settings.layoutIterations}
              onChange={(event) =>
                useGraphSettingsStore.setLayoutIterations(Math.round(clampNumber(event.currentTarget.valueAsNumber, 20, 800)))
              }
            />
          </label>
          <label className="graph-workbench__range-field">
            <span>Max nodes</span>
            <input
              max="1000"
              min="1"
              step="1"
              type="number"
              value={settings.maxNodes}
              onChange={(event) =>
                useGraphSettingsStore.setMaxNodes(Math.round(clampNumber(event.currentTarget.valueAsNumber, 1, 1000)))
              }
            />
          </label>
        </section>
      ) : null}
    </div>
  );
}
