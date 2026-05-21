import { useMemo, useState } from "react";

import { deleteEntity, deleteRelation, editEntity, editRelation, queryGraph } from "../../api/graphApi";
import { useGraphSettingsStore } from "../../stores/graphSettingsStore";
import { useGraphStore } from "../../stores/graphStore";
import type { GraphEdgeDto, GraphNodeDto, GraphNodeProperties, JsonValue } from "../../types/graph";
import { ConfirmDialog } from "./ConfirmDialog";
import { MergeDialog } from "./MergeDialog";
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
  const settings = useGraphSettingsStore();
  const selectedNode = useGraphStore((state) => state.selectedNode);
  const selectedEdge = useGraphStore((state) => state.selectedEdge);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [isReloading, setIsReloading] = useState(false);
  const [confirmTarget, setConfirmTarget] = useState<PropertyEditTarget | null>(null);
  const [mergeState, setMergeState] = useState<{ sourceEntity: string; targetEntity: string } | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const target: PropertyEditTarget | null = selectedNode ? "node" : selectedEdge ? "edge" : null;
  const title = selectedNode ? getNodeTitle(selectedNode) : selectedEdge ? getEdgeTitle(selectedEdge) : "No selection";
  const properties = selectedNode?.properties ?? selectedEdge?.properties ?? {};
  const initialValues = useMemo<PropertyEditValues>(
    () => propertyValuesFromProperties(target ?? "node", properties, selectedNode?.id ?? ""),
    [properties, selectedNode?.id, target]
  );

  async function reloadGraph(labelOverride?: string) {
    setIsReloading(true);
    setErrorMessage(null);

    try {
      const queryLabel = labelOverride ?? settings.queryLabel;
      if (labelOverride) {
        useGraphSettingsStore.setQueryLabel(labelOverride);
      }

      const graph = await queryGraph(apiBase, queryLabel, settings.maxDepth, settings.maxNodes);
      useGraphStore.setRawGraph(graph);
      useGraphStore.resetSelection();
      setMergeState(null);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Failed to refresh graph.");
    } finally {
      setIsReloading(false);
    }
  }

  async function saveProperties(values: PropertyEditValues) {
    if (isSaving) {
      return;
    }

    if (!target) {
      return;
    }

    setIsSaving(true);
    setErrorMessage(null);

    try {
      const updatedData = propertyValuesToUpdatedData(target, values);

      if (target === "node" && selectedNode) {
        const originalEntityName = selectedNode.id;
        const updatedEntityName = formatValue(updatedData.entity_name);
        const response = await editEntity(apiBase, originalEntityName, updatedData, true, true);
        if (!response.succeeded) {
          throw new Error(response.message || "Failed to edit entity.");
        }

        if (updatedEntityName !== originalEntityName && response.operationSummary?.merged === true) {
          setIsDialogOpen(false);
          setMergeState({
            sourceEntity: originalEntityName,
            targetEntity:
              response.operationSummary.targetEntity ??
              response.operationSummary.finalEntity ??
              updatedEntityName
          });
          return;
        }

        if (updatedEntityName && updatedEntityName !== originalEntityName) {
          useGraphStore.renameNode(originalEntityName, updatedEntityName);
        } else {
          useGraphStore.updateNodeProperty(originalEntityName, "entity_id", updatedData.entity_name);
          useGraphStore.updateNodeProperty(originalEntityName, "entity_name", updatedData.entity_name);
        }

        const currentEntityName = updatedEntityName || originalEntityName;
        useGraphStore.updateNodeProperty(currentEntityName, "entity_type", updatedData.entity_type);
        useGraphStore.updateNodeProperty(currentEntityName, "description", updatedData.description);
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

  async function confirmDelete() {
    if (isDeleting || !confirmTarget) {
      return;
    }

    setIsDeleting(true);
    setErrorMessage(null);

    try {
      if (confirmTarget === "node" && selectedNode) {
        await deleteEntity(apiBase, selectedNode.id);
        useGraphStore.removeNode(selectedNode.id);
      }

      if (confirmTarget === "edge" && selectedEdge) {
        await deleteRelation(apiBase, selectedEdge.source, selectedEdge.target);
        useGraphStore.removeEdge(selectedEdge.id);
      }

      useGraphStore.resetSelection();
      setConfirmTarget(null);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Failed to delete selection.");
    } finally {
      setIsDeleting(false);
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

          <div className="graph-workbench__panel-actions">
            <button className="graph-workbench__primary-button" type="button" onClick={() => setIsDialogOpen(true)}>
              Edit properties
            </button>
            <button className="graph-workbench__danger-button" type="button" onClick={() => setConfirmTarget(target)}>
              {target === "node" ? "Delete entity" : "Delete relation"}
            </button>
          </div>
        </>
      ) : (
        <p className="graph-workbench__muted">Click a node or edge in the graph to inspect and edit its properties.</p>
      )}

      {errorMessage ? <p className="graph-workbench__dialog-error graph-workbench__panel-error">{errorMessage}</p> : null}

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
      <ConfirmDialog
        open={confirmTarget !== null}
        title={confirmTarget === "node" ? "Delete entity" : "Delete relation"}
        message={
          confirmTarget === "node" && selectedNode
            ? `Delete entity ${getNodeTitle(selectedNode)} from the graph? This action cannot be undone.`
            : selectedEdge
              ? `Delete relation ${getEdgeTitle(selectedEdge)} from the graph? This action cannot be undone.`
              : "Delete this graph item? This action cannot be undone."
        }
        confirmText={confirmTarget === "node" ? "Delete entity" : "Delete relation"}
        isConfirming={isDeleting}
        onCancel={() => {
          if (!isDeleting) {
            setConfirmTarget(null);
          }
        }}
        onConfirm={() => void confirmDelete()}
      />
      <MergeDialog
        open={mergeState !== null}
        sourceEntity={mergeState?.sourceEntity ?? ""}
        targetEntity={mergeState?.targetEntity ?? ""}
        onCancel={() => setMergeState(null)}
        onKeepCurrentStart={() => void reloadGraph()}
        onUseMergedStart={() => void reloadGraph(mergeState?.targetEntity)}
        isWorking={isReloading}
      />
    </aside>
  );
}
