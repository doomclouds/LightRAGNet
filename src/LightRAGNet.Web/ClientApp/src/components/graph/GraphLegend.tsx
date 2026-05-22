import { useMemo } from "react";

import { useGraphStore } from "../../stores/graphStore";

type GraphLegendProps = {
  visible: boolean;
};

export function GraphLegend({ visible }: GraphLegendProps) {
  const rawGraph = useGraphStore((state) => state.rawGraph);
  const items = useMemo(() => {
    const colorByType = new Map<string, string>();

    rawGraph?.nodes.forEach((node) => {
      const type = node.type || "entity";
      if (!colorByType.has(type)) {
        colorByType.set(type, node.color);
      }
    });

    return [...colorByType.entries()].slice(0, 12);
  }, [rawGraph]);

  if (!visible || items.length === 0) {
    return null;
  }

  return (
    <aside className="graph-workbench__legend" aria-label="Graph legend">
      {items.map(([type, color]) => (
        <div key={type}>
          <span style={{ backgroundColor: color }} />
          <strong>{type}</strong>
        </div>
      ))}
    </aside>
  );
}
