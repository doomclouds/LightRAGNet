import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Banner } from '@/shared/components/Banner';
import { ConfirmDialog } from '@/shared/components/ConfirmDialog';
import { DiagnosticTable } from '@/shared/components/DiagnosticTable';
import { Field } from '@/shared/components/Field';
import { SegmentedControl } from '@/shared/components/SegmentedControl';

describe('design system primitives', () => {
  it('renders semantic banners with tone classes and readable content', () => {
    render(
      <Banner tone="danger" title="Unable to load cache overview">
        Check the server connection and try again.
      </Banner>
    );

    const banner = screen.getByRole('alert');

    expect(banner).toHaveClass('lrn-banner', 'lrn-banner--danger');
    expect(screen.getByText('Unable to load cache overview')).toBeInTheDocument();
    expect(screen.getByText('Check the server connection and try again.')).toBeInTheDocument();
  });

  it('allows banners to override alert semantics with note role', () => {
    render(<Banner tone="warning" role="note">The queue is paused.</Banner>);

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveClass('lrn-banner', 'lrn-banner--warning');
  });

  it('renders segmented controls with stable pressed state and change callback', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(
      <SegmentedControl
        ariaLabel="Time window"
        value="24h"
        options={[
          { value: '24h', label: '24h' },
          { value: '7d', label: '7d' }
        ]}
        onChange={onChange}
      />
    );

    expect(screen.getByRole('group', { name: 'Time window' })).toHaveClass('lrn-segmented-control');
    expect(screen.getByRole('button', { name: '24h' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: '7d' })).toHaveAttribute('aria-pressed', 'false');

    await user.click(screen.getByRole('button', { name: '7d' }));

    expect(onChange).toHaveBeenCalledWith('7d');
  });

  it('links fields to label, hint, and error text without relying on placeholders', () => {
    render(
      <Field label="Workspace" hint="Use _ for the default workspace" error="Workspace is required">
        <input value="" onChange={() => undefined} />
      </Field>
    );

    const input = screen.getByLabelText('Workspace');

    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('Use _ for the default workspace Workspace is required');
    expect(screen.getByText('Workspace is required')).toHaveClass('lrn-field__error');
  });

  it('preserves existing field descriptions and generates distinct control ids', () => {
    render(
      <>
        <span id="existing-description">Existing description</span>
        <Field label="Workspace" hint="Hint text" error="Error text">
          <input aria-describedby="existing-description" />
        </Field>
        <Field label="Namespace">
          <input />
        </Field>
      </>
    );

    const workspace = screen.getByLabelText('Workspace');
    const namespace = screen.getByLabelText('Namespace');

    expect(workspace).toHaveAccessibleDescription('Existing description Hint text Error text');
    expect(workspace).toHaveAttribute('aria-describedby', expect.stringContaining('existing-description'));
    expect(workspace.id).not.toBe(namespace.id);
  });

  it('supports field render props with generated control props', () => {
    render(
      <Field label="Workspace" hint="Use _ for default">
        {(controlProps) => <input {...controlProps} data-control-id={controlProps.id} />}
      </Field>
    );

    const input = screen.getByLabelText('Workspace');

    expect(input).toHaveAttribute('data-control-id', input.id);
    expect(input).toHaveAccessibleDescription('Use _ for default');
  });

  it('renders diagnostic rows with wrapped values and optional monospace values', () => {
    render(
      <DiagnosticTable
        rows={[
          { label: 'Provider', value: 'DeepSeek' },
          { label: 'Cache key', value: 'query:workspace:long-value-that-must-wrap', monospace: true }
        ]}
      />
    );

    expect(screen.getByRole('table')).toHaveClass('lrn-diagnostic-table');
    expect(screen.getByRole('row', { name: /Provider DeepSeek/ })).toBeInTheDocument();
    expect(screen.getByText('query:workspace:long-value-that-must-wrap')).toHaveClass(
      'lrn-diagnostic-table__value--mono'
    );
  });

  it('renders confirm dialogs with escape cancel, pending state, and danger action', async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    const onConfirm = vi.fn();

    render(
      <ConfirmDialog
        open
        title="Clear cache entries?"
        tone="danger"
        confirmLabel="Clear"
        cancelLabel="Cancel"
        pending={false}
        onCancel={onCancel}
        onConfirm={onConfirm}
      >
        This action cannot be undone.
      </ConfirmDialog>
    );

    expect(screen.getByRole('dialog', { name: 'Clear cache entries?' })).toHaveClass('lrn-modal');
    expect(screen.getByText('This action cannot be undone.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Clear' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);

    await user.keyboard('{Escape}');
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('uses unique dialog title and description ids for concurrent confirm dialogs', () => {
    render(
      <>
        <ConfirmDialog
          open
          title="Clear cache entries?"
          confirmLabel="Clear"
          cancelLabel="Cancel"
          onCancel={() => undefined}
          onConfirm={() => undefined}
        >
          Cache entries will be removed.
        </ConfirmDialog>
        <ConfirmDialog
          open
          title="Delete document?"
          confirmLabel="Delete"
          cancelLabel="Cancel"
          onCancel={() => undefined}
          onConfirm={() => undefined}
        >
          The document will be removed.
        </ConfirmDialog>
      </>
    );

    const dialogs = screen.getAllByRole('dialog');
    const titleIds = dialogs.map((dialog) => dialog.getAttribute('aria-labelledby'));
    const descriptionIds = dialogs.map((dialog) => dialog.getAttribute('aria-describedby'));

    expect(new Set(titleIds).size).toBe(2);
    expect(new Set(descriptionIds).size).toBe(2);
    titleIds.forEach((id) => expect(document.getElementById(id ?? '')).toBeInTheDocument());
    descriptionIds.forEach((id) => expect(document.getElementById(id ?? '')).toBeInTheDocument());
  });

  it('moves focus into confirm dialogs, traps tabbing, and restores focus on close', async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    const onConfirm = vi.fn();
    const launcher = document.createElement('button');
    launcher.textContent = 'Open dialog';
    document.body.append(launcher);
    launcher.focus();

    const { rerender } = render(
      <ConfirmDialog
        open
        title="Clear cache entries?"
        confirmLabel="Clear"
        cancelLabel="Cancel"
        onCancel={onCancel}
        onConfirm={onConfirm}
      >
        This action cannot be undone.
      </ConfirmDialog>
    );

    const dialog = screen.getByRole('dialog', { name: 'Clear cache entries?' });
    const cancel = screen.getByRole('button', { name: 'Cancel' });
    const confirm = screen.getByRole('button', { name: 'Clear' });

    expect(cancel).toHaveFocus();

    await user.tab({ shift: true });
    expect(confirm).toHaveFocus();

    await user.tab();
    expect(cancel).toHaveFocus();

    rerender(
      <ConfirmDialog
        open={false}
        title="Clear cache entries?"
        confirmLabel="Clear"
        cancelLabel="Cancel"
        onCancel={onCancel}
        onConfirm={onConfirm}
      >
        This action cannot be undone.
      </ConfirmDialog>
    );

    expect(launcher).toHaveFocus();
    expect(dialog).not.toBeInTheDocument();
    launcher.remove();
  });

  it('keeps pending confirm dialogs open and disables both actions', () => {
    const onCancel = vi.fn();

    render(
      <ConfirmDialog
        open
        title="Delete document?"
        tone="danger"
        confirmLabel="Delete"
        cancelLabel="Cancel"
        pending
        onCancel={onCancel}
        onConfirm={() => undefined}
      >
        The document will be removed from the list.
      </ConfirmDialog>
    );

    const dialog = screen.getByRole('dialog', { name: 'Delete document?' });

    expect(dialog).toHaveFocus();
    expect(dialog).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
  });

  it('does not cancel pending confirm dialogs with Escape', async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();

    render(
      <ConfirmDialog
        open
        title="Delete document?"
        confirmLabel="Delete"
        cancelLabel="Cancel"
        pending
        onCancel={onCancel}
        onConfirm={() => undefined}
      >
        The document will be removed from the list.
      </ConfirmDialog>
    );

    await user.keyboard('{Escape}');

    expect(onCancel).not.toHaveBeenCalled();
  });
});
