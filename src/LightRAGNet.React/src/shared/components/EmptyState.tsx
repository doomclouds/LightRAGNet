type EmptyStateProps = {
  title: string;
  description: string;
};

export function EmptyState({ title, description }: EmptyStateProps) {
  return (
    <div className="lrn-empty-state">
      <strong>{title}</strong>
      <p>{description}</p>
    </div>
  );
}
