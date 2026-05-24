import { Trash2 } from 'lucide-react';

export function ClearAllDataAction() {
  return (
    <button
      type="button"
      className="lrn-button lrn-button--danger clear-all-data-action"
      disabled
      title="Clear all data is not wired in the React shell yet."
    >
      <Trash2 size={16} aria-hidden="true" />
      <span>Clear All Data</span>
    </button>
  );
}
