import { useMemo, useState } from "react";

import { editEntity, editRelation } from "../../api/graphApi";
import { useGraphStore } from "../../stores/graphStore";
import type { GraphEdgeDto, GraphNodeDto, GraphNodeProperties, JsonValue } from "../../types/graph";
import {
  PropertyEditDialog,
  propertyValuesFromProperties,
  propertyValuesToUpdatedData,
  type PropertyEditTarget,
  type PropertyEditValues
} from "./PropertyEditDialog";

type PropertiesPanelProps = {
  apiBase: string;
};

function formatValue(value: JsonValue | undefined): string {
  if (value === undefined || value === null) {
    return "";
  }

  if (typeof value === "object") {
    return JSON.stringify(value);
  }

  return String(value);
}

function getNodeTitle(node: GraphNodeDto): string {
  return formatValue(node.properties.entity_id) || formatValue(node.properties.entity_name) || node.label || node.id;
}

function getEdgeTitle(edge: GraphEdgeDto): string {
  return `${edge.source} -> ${edge.target}`;
}

function renderProperties(properties: GraphNodeProperties) {
  const entries = Object.entries(properties);

  if (entries.length === 0) {
    return <p className="graph-workbench__muted">No editable properties loaded for this item.</p>;
  }

  return (
    <dl className="graph-workbench__property-list">
      {entries.map(([key, value]) => (
        <div key={key}>
          <dt>{key}</dt>
          <dd>{formatValue(value)}</dd>
        </div>
      ))}
    </dl>
  );
}

export function PropertiesPanel({ apiBase }: PropertiesPanelProps) {
  const selectedNode = useGraphStore((state) => state.selectedNode);
  const selectedEdge = useGraphStore((state) => state.selectedEdge);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const target: PropertyEditTarget | null = selectedNode ? "node" : selectedEdge ? "edge" : null;
  const title = selectedNode ? getNodeTitle(selectedNode) : selectedEdge ? getEdgeTitle(selectedEdge) : "No selection";
  const properties = selectedNode?.properties ?? selectedEdge?.properties ?? {};
  const initialValues = useMemo<PropertyEditValues>(
    () => propertyValuesFromProperties(target ?? "node", properties, selectedNode?.id ?? ""),
    [properties, selectedNode?.id, target]
  );

  async function saveProperties(values: PropertyEditValues) {
    if (!target) {
      return;
    }

    setIsSaving(true);
    setErrorMessage(null);

    try {
      const updatedData = propertyValuesToUpdatedData(target, values);

      if (target === "node" && selectedNode) {
        const response = await editEntity(apiBase, selectedNode.id, updatedData, true, false);
        if (!response.succeeded) {
          throw new Error(response.message || "Failed to edit entity.");
        }

        useGraphStore.updateNodeProperty(selectedNode.id, "entity_id", updatedData.entity_name);
        useGraphStore.updateNodeProperty(selectedNode.id, "entity_name", updatedData.entity_name);
        useGraphStore.updateNodeProperty(selectedNode.id, "entity_type", updatedData.entity_type);
        useGraphStore.updateNodeProperty(selectedNode.id, "description", updatedData.description);
      }

      if (target === "edge" && selectedEdge) {
        const response = await editRelation(apiBase, selectedEdge.source, selectedEdge.target, updatedData);
        if (!response.succeeded) {
          throw new Error(response.message || "Failed to edit relation.");
        }

        useGraphStore.updateEdgeProperty(selectedEdge.id, "description", updatedData.description);
        useGraphStore.updateEdgeProperty(selectedEdge.id, "keywords", updatedData.keywords);
        useGraphStore.updateEdgeProperty(selectedEdge.id, "weight", updatedData.weight);
      }

      setIsDialogOpen(false);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Failed to save properties.");
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <aside className="graph-workbench__properties" aria-label="Selection properties">
      <div className="graph-workbench__panel-heading">
        <p className="graph-workbench__eyebrow">Selection</p>
        <h2>{title}</h2>
      </div>

      {target ? (
        <>
          <div className="graph-workbench__selection-meta">
            <span>{target === "node" ? "Node" : "Edge"}</span>
            {target === "node" && selectedNode?.type ? <span>{selectedNode.type}</span> : null}
            {target === "edge" && selectedEdge?.type ? <span>{selectedEdge.type}</span> : null}
          </div>

          {renderProperties(properties)}

          <button className="graph-workbench__primary-button graph-workbench__panel-action" type="button" onClick={() => setIsDialogOpen(true)}>
            Edit properties
          </button>
        </>
      ) : (
        <p className="graph-workbench__muted">Click a node or edge in the graph to inspect and edit its properties.</p>
      )}

      <PropertyEditDialog
        open={isDialogOpen}
        target={target ?? "node"}
        initialValues={initialValues}
        errorMessage={errorMessage}
        isSaving={isSaving}
        onCancel={() => {
          if (!isSaving) {
            setIsDialogOpen(false);
            setErrorMessage(null);
          }
        }}
        onSave={(values) => void saveProperties(values)}
      />
    </aside>
  );
}
