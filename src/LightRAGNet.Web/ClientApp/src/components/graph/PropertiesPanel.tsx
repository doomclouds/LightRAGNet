import { useMemo, useState } from "react";

import { deleteEntity, editEntity, queryGraph } from "../../api/graphApi";
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

export function resolvePropertiesPanelSelection(
  selectedNode: GraphNodeDto | null,
  selectedEdge: GraphEdgeDto | null,
  focusedNode: GraphNodeDto | null,
  focusedEdge: GraphEdgeDto | null
): {
  currentNode: GraphNodeDto | null;
  currentEdge: GraphEdgeDto | null;
  hasPinnedSelection: boolean;
  target: PropertyEditTarget | null;
} {
  void focusedEdge;

  const currentNode = selectedNode ?? (selectedEdge ? null : focusedNode);
  const currentEdge = null;
  const hasPinnedSelection = selectedNode !== null;
  const target: PropertyEditTarget | null = currentNode ? "node" : null;

  return {
    currentNode,
    currentEdge,
    hasPinnedSelection,
    target
  };
}

export function PropertiesPanel({ apiBase }: PropertiesPanelProps) {
  const settings = useGraphSettingsStore();
  const selectedNode = useGraphStore((state) => state.selectedNode);
  const selectedEdge = useGraphStore((state) => state.selectedEdge);
  const focusedNode = useGraphStore((state) => state.focusedNode);
  const focusedEdge = useGraphStore((state) => state.focusedEdge);
  const rawGraph = useGraphStore((state) => state.rawGraph);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [isReloading, setIsReloading] = useState(false);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const [mergeState, setMergeState] = useState<{ sourceEntity: string; targetEntity: string } | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const { currentNode, hasPinnedSelection, target } = resolvePropertiesPanelSelection(
    selectedNode,
    selectedEdge,
    focusedNode,
    focusedEdge
  );
  const title = currentNode ? getNodeTitle(currentNode) : "No selection";
  const properties = currentNode?.properties ?? {};
  const relationships = useMemo(() => {
    if (!rawGraph || !currentNode) {
      return [];
    }

    return rawGraph.edges
      .filter((edge) => edge.source === currentNode.id || edge.target === currentNode.id)
      .map((edge) => {
        const neighbourId = edge.source === currentNode.id ? edge.target : edge.source;
        const neighbour = rawGraph.nodes.find((node) => node.id === neighbourId);
        return {
          edge,
          neighbourId,
          label: neighbour ? getNodeTitle(neighbour) : neighbourId,
          type: edge.type ?? "Neighbour"
        };
      })
      .slice(0, 40);
  }, [currentNode, rawGraph]);
  const initialValues = useMemo<PropertyEditValues>(
    () => propertyValuesFromProperties(target ?? "node", properties, currentNode?.id ?? ""),
    [properties, currentNode?.id, target]
  );

  if (!target) {
    return null;
  }

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
      const updatedData = propertyValuesToUpdatedData("node", values);

      if (selectedNode) {
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

      setIsDialogOpen(false);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Failed to save properties.");
    } finally {
      setIsSaving(false);
    }
  }

  async function confirmDelete() {
    if (isDeleting || !confirmDeleteOpen) {
      return;
    }

    setIsDeleting(true);
    setErrorMessage(null);

    try {
      if (selectedNode) {
        await deleteEntity(apiBase, selectedNode.id);
        useGraphStore.removeNode(selectedNode.id);
      }

      useGraphStore.resetSelection();
      setConfirmDeleteOpen(false);
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

      <div className="graph-workbench__selection-meta">
        <span>{hasPinnedSelection ? "Selected" : "Focused"}</span>
        <span>Node</span>
        {currentNode?.type ? <span>{currentNode.type}</span> : null}
      </div>

      {renderProperties(properties)}

      {currentNode && relationships.length > 0 ? (
        <section className="graph-workbench__relationships" aria-label="Node relationships">
          <h3>Relationships</h3>
          <div>
            {relationships.map(({ edge, neighbourId, label, type }) => (
              <button
                key={`${edge.id}:${neighbourId}`}
                type="button"
                onClick={() => useGraphStore.selectNode(neighbourId, true)}
              >
                <span>{type}</span>
                <strong>{label}</strong>
              </button>
            ))}
          </div>
        </section>
      ) : null}

      {hasPinnedSelection ? (
        <div className="graph-workbench__panel-actions">
          <button className="graph-workbench__primary-button" type="button" onClick={() => setIsDialogOpen(true)}>
            Edit properties
          </button>
          <button className="graph-workbench__danger-button" type="button" onClick={() => setConfirmDeleteOpen(true)}>
            Delete entity
          </button>
        </div>
      ) : (
        <p className="graph-workbench__muted graph-workbench__hover-hint">Click to pin this item for editing.</p>
      )}

      {errorMessage ? <p className="graph-workbench__dialog-error graph-workbench__panel-error">{errorMessage}</p> : null}

      <PropertyEditDialog
        open={isDialogOpen}
        target="node"
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
        open={confirmDeleteOpen}
        title="Delete entity"
        message={
          selectedNode
            ? `Delete entity ${getNodeTitle(selectedNode)} from the graph? This action cannot be undone.`
            : "Delete this graph item? This action cannot be undone."
        }
        confirmText="Delete entity"
        isConfirming={isDeleting}
        onCancel={() => {
          if (!isDeleting) {
            setConfirmDeleteOpen(false);
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
