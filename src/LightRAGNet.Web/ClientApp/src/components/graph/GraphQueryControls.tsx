import { RefreshCw } from "lucide-react";
import { useEffect, useId, useState } from "react";

import { getGraphLabels } from "../../api/graphApi";
import { useGraphSettingsStore } from "../../stores/graphSettingsStore";

type GraphQueryControlsProps = {
  apiBase: string;
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

export function GraphQueryControls({ apiBase, isFetching, onLoad }: GraphQueryControlsProps) {
  const settings = useGraphSettingsStore();
  const labelListId = useId();
  const [labels, setLabels] = useState<string[]>(["*"]);

  useEffect(() => {
    let cancelled = false;
    void getGraphLabels(apiBase)
      .then((items) => {
        if (!cancelled) {
          setLabels(["*", ...items.filter((item) => item !== "*")]);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setLabels(["*"]);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [apiBase]);

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
          list={labelListId}
          type="text"
          value={settings.queryLabel}
          onChange={(event) => useGraphSettingsStore.setQueryLabel(event.currentTarget.value.trim() || "*")}
        />
        <datalist id={labelListId}>
          {labels.map((label) => (
            <option key={label} value={label} />
          ))}
        </datalist>
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
