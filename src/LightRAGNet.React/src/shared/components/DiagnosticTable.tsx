import type { ReactNode } from 'react';

export type DiagnosticTableRow = {
  label: ReactNode;
  value: ReactNode;
  monospace?: boolean;
};

type DiagnosticTableProps = {
  rows: DiagnosticTableRow[];
  caption?: string;
  className?: string;
};

export function DiagnosticTable({ rows, caption, className }: DiagnosticTableProps) {
  return (
    <table className={['lrn-diagnostic-table', className].filter(Boolean).join(' ')}>
      {caption ? <caption>{caption}</caption> : null}
      <tbody>
        {rows.map((row, index) => (
          <tr key={index}>
            <th scope="row">{row.label}</th>
            <td>
              <span className={row.monospace ? 'lrn-diagnostic-table__value--mono' : undefined}>{row.value}</span>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
