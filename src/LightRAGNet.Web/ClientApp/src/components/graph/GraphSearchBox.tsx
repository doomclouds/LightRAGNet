import { Search } from "lucide-react";
import { useMemo, useState } from "react";

import { useGraphStore } from "../../stores/graphStore";
import type { GraphNodeDto } from "../../types/graph";

function getNodeSearchText(node: GraphNodeDto): string {
  return [
    node.id,
    node.label,
    node.type,
    node.properties.entity_id,
    node.properties.entity_name,
    node.properties.description
  ]
    .filter((value): value is string => typeof value === "string" && value.trim().length > 0)
    .join(" ")
    .toLowerCase();
}

export function GraphSearchBox() {
  const rawGraph = useGraphStore((state) => state.rawGraph);
  const [query, setQuery] = useState("");
  const trimmedQuery = query.trim().toLowerCase();
  const matches = useMemo(() => {
    if (!rawGraph || trimmedQuery.length === 0) {
      return [];
    }

    return rawGraph.nodes.filter((node) => getNodeSearchText(node).includes(trimmedQuery)).slice(0, 8);
  }, [rawGraph, trimmedQuery]);

  return (
    <div className="graph-workbench__search-card">
      <Search aria-hidden="true" size={15} />
      <input
        aria-label="Search graph nodes"
        placeholder="Search nodes"
        type="search"
        value={query}
        onBlur={() => useGraphStore.focusNode(null)}
        onChange={(event) => setQuery(event.currentTarget.value)}
      />
      {matches.length > 0 ? (
        <div className="graph-workbench__search-results">
          {matches.map((node) => (
            <button
              key={node.id}
              type="button"
              onMouseEnter={() => useGraphStore.focusNode(node.id)}
              onFocus={() => useGraphStore.focusNode(node.id)}
              onClick={() => {
                useGraphStore.selectNode(node.id, true);
                setQuery(node.label || node.id);
              }}
            >
              <span style={{ backgroundColor: node.color }} />
              <strong>{node.label || node.id}</strong>
              {node.type ? <em>{node.type}</em> : null}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
