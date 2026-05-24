import { useEffect, useState } from "react";

import type { GraphNodeProperties, JsonValue } from "@/types/graph";

export type PropertyEditTarget = "node" | "edge";

export type PropertyEditValues = {
  entity_id?: string;
  entity_type?: string;
  description?: string;
  keywords?: string;
  weight?: number;
};

type PropertyEditDialogProps = {
  open: boolean;
  target: PropertyEditTarget;
  initialValues: PropertyEditValues;
  errorMessage?: string | null;
  isSaving: boolean;
  onCancel: () => void;
  onSave: (values: PropertyEditValues) => void;
};

function toText(value: unknown): string {
  return typeof value === "string" ? value : "";
}

function toNumber(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) ? value : 1;
}

export function propertyValuesFromProperties(
  target: PropertyEditTarget,
  properties: GraphNodeProperties,
  fallbackName = ""
): PropertyEditValues {
  if (target === "node") {
    return {
      entity_id: toText(properties.entity_id) || toText(properties.entity_name) || fallbackName,
      entity_type: toText(properties.entity_type),
      description: toText(properties.description)
    };
  }

  return {
    description: toText(properties.description),
    keywords: toText(properties.keywords),
    weight: toNumber(properties.weight)
  };
}

export function propertyValuesToUpdatedData(target: PropertyEditTarget, values: PropertyEditValues): GraphNodeProperties {
  if (target === "node") {
    return {
      entity_name: values.entity_id?.trim() ?? "",
      entity_type: values.entity_type?.trim() ?? "",
      description: values.description?.trim() ?? ""
    };
  }

  const weight = Number(values.weight);
  return {
    description: values.description?.trim() ?? "",
    keywords: values.keywords?.trim() ?? "",
    weight: Number.isFinite(weight) ? weight : 1
  };
}

function getInputValue(value: JsonValue | undefined): string {
  return typeof value === "string" || typeof value === "number" ? String(value) : "";
}

export function PropertyEditDialog({
  open,
  target,
  initialValues,
  errorMessage,
  isSaving,
  onCancel,
  onSave
}: PropertyEditDialogProps) {
  const [values, setValues] = useState<PropertyEditValues>(initialValues);

  useEffect(() => {
    if (open) {
      setValues(initialValues);
    }
  }, [initialValues, open]);

  if (!open) {
    return null;
  }

  return (
    <div className="graph-workbench__dialog-backdrop" role="presentation">
      <form
        className="graph-workbench__dialog"
        role="dialog"
        aria-modal="true"
        aria-label={target === "node" ? "Edit node properties" : "Edit edge properties"}
        onSubmit={(event) => {
          event.preventDefault();
          onSave(values);
        }}
      >
        <header>
          <h3>{target === "node" ? "Edit Node" : "Edit Edge"}</h3>
          <button aria-label="Close" disabled={isSaving} onClick={onCancel} type="button">
            ×
          </button>
        </header>

        <div className="graph-workbench__dialog-body">
          {target === "node" ? (
            <>
              <label className="graph-workbench__field">
                <span>Entity ID</span>
                <input
                  required
                  value={values.entity_id ?? ""}
                  onChange={(event) => setValues({ ...values, entity_id: event.currentTarget.value })}
                />
              </label>
              <label className="graph-workbench__field">
                <span>Entity type</span>
                <input
                  value={values.entity_type ?? ""}
                  onChange={(event) => setValues({ ...values, entity_type: event.currentTarget.value })}
                />
              </label>
              <label className="graph-workbench__field">
                <span>Description</span>
                <textarea
                  value={values.description ?? ""}
                  onChange={(event) => setValues({ ...values, description: event.currentTarget.value })}
                />
              </label>
            </>
          ) : (
            <>
              <label className="graph-workbench__field">
                <span>Description</span>
                <textarea
                  value={values.description ?? ""}
                  onChange={(event) => setValues({ ...values, description: event.currentTarget.value })}
                />
              </label>
              <label className="graph-workbench__field">
                <span>Keywords</span>
                <input
                  value={values.keywords ?? ""}
                  onChange={(event) => setValues({ ...values, keywords: event.currentTarget.value })}
                />
              </label>
              <label className="graph-workbench__field">
                <span>Weight</span>
                <input
                  min="0"
                  step="0.1"
                  type="number"
                  value={getInputValue(values.weight)}
                  onChange={(event) =>
                    setValues({
                      ...values,
                      weight: Number.isFinite(event.currentTarget.valueAsNumber)
                        ? event.currentTarget.valueAsNumber
                        : 1
                    })
                  }
                />
              </label>
            </>
          )}

          {errorMessage ? <p className="graph-workbench__dialog-error">{errorMessage}</p> : null}
        </div>

        <footer>
          <button disabled={isSaving} onClick={onCancel} type="button">
            Cancel
          </button>
          <button className="graph-workbench__primary-button" disabled={isSaving} type="submit">
            {isSaving ? "Saving" : "Save"}
          </button>
        </footer>
      </form>
    </div>
  );
}
