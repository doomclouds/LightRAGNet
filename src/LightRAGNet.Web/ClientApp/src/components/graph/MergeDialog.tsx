type MergeDialogProps = {
  open: boolean;
  sourceEntity: string;
  targetEntity: string;
  onCancel: () => void;
  onUseMergedStart: () => void;
  onKeepCurrentStart: () => void;
};

export function MergeDialog({
  open,
  sourceEntity,
  targetEntity,
  onCancel,
  onUseMergedStart,
  onKeepCurrentStart
}: MergeDialogProps) {
  if (!open) {
    return null;
  }

  return (
    <div className="graph-workbench__dialog-backdrop" role="presentation">
      <section className="graph-workbench__dialog" role="dialog" aria-modal="true" aria-label="Entity merged">
        <header>
          <h3>Entity merged</h3>
          <button aria-label="Close" onClick={onCancel} type="button">
            ×
          </button>
        </header>
        <div className="graph-workbench__dialog-body">
          <p>
            {sourceEntity} was merged into {targetEntity}. Refresh the current graph to avoid stale nodes, or continue
            from the merged entity.
          </p>
        </div>
        <footer>
          <button onClick={onKeepCurrentStart} type="button">
            Refresh current graph
          </button>
          <button className="graph-workbench__primary-button" onClick={onUseMergedStart} type="button">
            Use merged entity
          </button>
        </footer>
      </section>
    </div>
  );
}
