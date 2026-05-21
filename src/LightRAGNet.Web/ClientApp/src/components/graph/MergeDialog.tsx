type MergeDialogProps = {
  open: boolean;
  onCancel: () => void;
};

export function MergeDialog({ open, onCancel }: MergeDialogProps) {
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
          <p>Merge workflow is reserved for the next task.</p>
        </div>
        <footer>
          <button onClick={onCancel} type="button">
            Close
          </button>
        </footer>
      </section>
    </div>
  );
}
