import { RefreshCw } from "lucide-react";

import { useGraphSettingsStore } from "../../stores/graphSettingsStore";

type GraphQueryControlsProps = {
  isFetching: boolean;
  onLoad: () => void;
};

const maxDepthBounds = { min: 1, max: 5 };
const maxNodesBounds = { min: 1, max: 1000 };

function clampNumber(value: number, min: number, max: number): number {
  if (!Number.isFinite(value)) {
    return min;
  }

  return Math.min(max, Math.max(min, Math.round(value)));
}

export function GraphQueryControls({ isFetching, onLoad }: GraphQueryControlsProps) {
  const settings = useGraphSettingsStore();

  return (
    <form
      className="graph-workbench__query-card"
      onSubmit={(event) => {
        event.preventDefault();
        onLoad();
      }}
    >
      <button
        className="graph-workbench__icon-button graph-workbench__icon-button--primary"
        disabled={isFetching}
        title={isFetching ? "Loading graph" : "Refresh graph"}
        type="submit"
      >
        <RefreshCw aria-hidden="true" className={isFetching ? "graph-workbench__spin" : undefined} size={16} />
      </button>

      <label className="graph-workbench__compact-field graph-workbench__compact-field--label">
        <span>Label</span>
        <input
          type="text"
          value={settings.queryLabel}
          onChange={(event) => useGraphSettingsStore.setQueryLabel(event.currentTarget.value.trim() || "*")}
        />
      </label>

      <label className="graph-workbench__compact-field">
        <span>Depth</span>
        <input
          type="number"
          min={maxDepthBounds.min}
          max={maxDepthBounds.max}
          value={settings.maxDepth}
          onChange={(event) =>
            useGraphSettingsStore.setMaxDepth(
              clampNumber(event.currentTarget.valueAsNumber, maxDepthBounds.min, maxDepthBounds.max)
            )
          }
        />
      </label>

      <label className="graph-workbench__compact-field">
        <span>Nodes</span>
        <input
          type="number"
          min={maxNodesBounds.min}
          max={maxNodesBounds.max}
          value={settings.maxNodes}
          onChange={(event) =>
            useGraphSettingsStore.setMaxNodes(
              clampNumber(event.currentTarget.valueAsNumber, maxNodesBounds.min, maxNodesBounds.max)
            )
          }
        />
      </label>
    </form>
  );
}
