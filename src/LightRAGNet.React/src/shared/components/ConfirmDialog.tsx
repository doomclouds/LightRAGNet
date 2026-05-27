import { useEffect, useId, useRef, type ReactNode } from 'react';
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
  const generatedId = useId();
  const titleId = `${generatedId}-title`;
  const descriptionId = `${generatedId}-description`;
  const dialogRef = useRef<HTMLElement>(null);
  const previousFocusRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    previousFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const cancelButton = dialogRef.current?.querySelector<HTMLButtonElement>('[data-lrn-cancel-action="true"]');
    const focusTarget = !pending && cancelButton && !cancelButton.disabled ? cancelButton : dialogRef.current;

    focusTarget?.focus();

    return () => {
      const previousFocus = previousFocusRef.current;

      if (previousFocus && document.contains(previousFocus)) {
        previousFocus.focus();
      }

      previousFocusRef.current = null;
    };
  }, [open]);

  useEffect(() => {
    if (!open) {
      return;
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        if (!pending) {
          onCancel();
        }

        return;
      }

      if (event.key === 'Tab') {
        trapFocus(event, dialogRef.current);
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
        ref={dialogRef}
        className="lrn-modal lrn-confirm-dialog__surface"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        aria-busy={pending ? true : undefined}
        tabIndex={-1}
      >
        <header className="lrn-confirm-dialog__header">
          <h2 id={titleId}>{title}</h2>
        </header>
        <div className="lrn-confirm-dialog__body" id={descriptionId}>{children}</div>
        <footer className="lrn-confirm-dialog__footer">
          <Button data-lrn-cancel-action="true" disabled={pending} onClick={onCancel}>
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

function trapFocus(event: KeyboardEvent, dialog: HTMLElement | null) {
  if (!dialog) {
    return;
  }

  const focusableElements = getFocusableElements(dialog);

  if (focusableElements.length === 0) {
    event.preventDefault();
    dialog.focus();
    return;
  }

  const firstElement = focusableElements[0];
  const lastElement = focusableElements[focusableElements.length - 1];
  const activeElement = document.activeElement;

  if (!dialog.contains(activeElement)) {
    event.preventDefault();
    firstElement.focus();
    return;
  }

  if (event.shiftKey && activeElement === firstElement) {
    event.preventDefault();
    lastElement.focus();
    return;
  }

  if (!event.shiftKey && activeElement === lastElement) {
    event.preventDefault();
    firstElement.focus();
  }
}

function getFocusableElements(dialog: HTMLElement): HTMLElement[] {
  const focusableSelector = [
    'a[href]',
    'button:not([disabled])',
    'input:not([disabled])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])'
  ].join(',');

  return Array.from(dialog.querySelectorAll<HTMLElement>(focusableSelector));
}
