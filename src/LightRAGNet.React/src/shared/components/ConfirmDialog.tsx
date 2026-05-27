import { useEffect, type ReactNode } from 'react';
import { Button } from './Button';

type ConfirmDialogTone = 'neutral' | 'danger';

type ConfirmDialogProps = {
  open: boolean;
  title: string;
  children: ReactNode;
  confirmLabel: string;
  cancelLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
  pending?: boolean;
  tone?: ConfirmDialogTone;
};

export function ConfirmDialog({
  open,
  title,
  children,
  confirmLabel,
  cancelLabel,
  onConfirm,
  onCancel,
  pending = false,
  tone = 'neutral'
}: ConfirmDialogProps) {
  useEffect(() => {
    if (!open || pending) {
      return;
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        onCancel();
      }
    }

    document.addEventListener('keydown', handleKeyDown);

    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onCancel, open, pending]);

  if (!open) {
    return null;
  }

  return (
    <div className="lrn-confirm-dialog" role="presentation">
      <div className="lrn-scrim lrn-confirm-dialog__scrim" onClick={pending ? undefined : onCancel} />
      <section
        className="lrn-modal lrn-confirm-dialog__surface"
        role="dialog"
        aria-modal="true"
        aria-labelledby="lrn-confirm-dialog-title"
      >
        <header className="lrn-confirm-dialog__header">
          <h2 id="lrn-confirm-dialog-title">{title}</h2>
        </header>
        <div className="lrn-confirm-dialog__body">{children}</div>
        <footer className="lrn-confirm-dialog__footer">
          <Button disabled={pending} onClick={onCancel}>
            {cancelLabel}
          </Button>
          <Button tone={tone === 'danger' ? 'danger' : 'primary'} disabled={pending} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </footer>
      </section>
    </div>
  );
}
