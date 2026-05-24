type ConfirmDialogProps = {
  open: boolean;
  title: string;
  message: string;
  confirmText: string;
  isConfirming?: boolean;
  onCancel: () => void;
  onConfirm: () => void;
};

export function ConfirmDialog({
  open,
  title,
  message,
  confirmText,
  isConfirming = false,
  onCancel,
  onConfirm
}: ConfirmDialogProps) {
  if (!open) {
    return null;
  }

  return (
    <div className="graph-workbench__dialog-backdrop" role="presentation">
      <section className="graph-workbench__dialog graph-workbench__confirm-dialog" role="dialog" aria-modal="true" aria-label={title}>
        <header>
          <h3>{title}</h3>
          <button aria-label="Close" disabled={isConfirming} onClick={onCancel} type="button">
            ×
          </button>
        </header>
        <div className="graph-workbench__dialog-body">
          <p>{message}</p>
        </div>
        <footer>
          <button disabled={isConfirming} onClick={onCancel} type="button">
            Cancel
          </button>
          <button className="graph-workbench__danger-button" disabled={isConfirming} onClick={onConfirm} type="button">
            {isConfirming ? "Deleting" : confirmText}
          </button>
        </footer>
      </section>
    </div>
  );
}
