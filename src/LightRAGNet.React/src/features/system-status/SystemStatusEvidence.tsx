type SystemStatusEvidenceProps = {
  evidence: Record<string, unknown>;
};

function renderEvidenceValue(value: unknown): string {
  if (value === null || typeof value !== "object") {
    return String(value);
  }

  return JSON.stringify(value);
}

export function SystemStatusEvidence({ evidence }: SystemStatusEvidenceProps) {
  const entries = Object.entries(evidence);

  if (entries.length === 0) {
    return null;
  }

  return (
    <details className="system-status__evidence">
      <summary>Evidence</summary>
      <table>
        <tbody>
          {entries.map(([key, value]) => (
            <tr key={key}>
              <th scope="row">{key}</th>
              <td>{renderEvidenceValue(value)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </details>
  );
}
