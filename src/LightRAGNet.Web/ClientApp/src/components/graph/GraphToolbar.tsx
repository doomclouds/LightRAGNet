import { useGraphSettingsStore } from "../../stores/graphSettingsStore";

type GraphToolbarProps = {
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

export function GraphToolbar({ isFetching, onLoad }: GraphToolbarProps) {
  const settings = useGraphSettingsStore();

  return (
    <form
      className="graph-workbench__toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        onLoad();
      }}
    >
      <label className="graph-workbench__field graph-workbench__field--wide">
        <span>Label</span>
        <input
          type="text"
          value={settings.queryLabel}
          onChange={(event) => useGraphSettingsStore.setQueryLabel(event.currentTarget.value.trim() || "*")}
        />
      </label>

      <label className="graph-workbench__field">
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

      <label className="graph-workbench__field">
        <span>Max nodes</span>
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

      <button className="graph-workbench__primary-button" disabled={isFetching} type="submit">
        {isFetching ? "Loading" : "Load"}
      </button>
    </form>
  );
}
