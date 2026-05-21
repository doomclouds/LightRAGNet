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
      <section className="graph-workbench__dialog" role="dialog" aria-modal="true" aria-label="Merge entities">
        <header>
          <h3>Merge Entities</h3>
          <button aria-label="Close" onClick={onCancel} type="button">
            ×
          </button>
        </header>
        <div className="graph-workbench__dialog-body">
          <p>
            {sourceEntity} will be merged into {targetEntity}. Choose which entity should provide the starting
            properties for the merge.
          </p>
        </div>
        <footer>
          <button onClick={onCancel} type="button">
            Cancel
          </button>
          <button onClick={onKeepCurrentStart} type="button">
            Keep current start
          </button>
          <button className="graph-workbench__primary-button" onClick={onUseMergedStart} type="button">
            Use merged start
          </button>
        </footer>
      </section>
    </div>
  );
}
