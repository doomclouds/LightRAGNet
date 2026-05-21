type GraphWorkbenchProps = {
  apiBase: string;
};

const queueItems = ["Load graph", "Inspect node", "Edit relationships"];

export function GraphWorkbench({ apiBase }: GraphWorkbenchProps) {
  return (
    <main className="graph-workbench" data-api-base={apiBase}>
      <aside className="graph-workbench__sidebar" aria-label="Graph workbench navigation">
        <div>
          <p className="graph-workbench__eyebrow">Knowledge Graph</p>
          <h1>Workbench</h1>
        </div>
        <nav className="graph-workbench__nav" aria-label="Workbench steps">
          {queueItems.map((item, index) => (
            <button
              className={index === 0 ? "graph-workbench__nav-item is-active" : "graph-workbench__nav-item"}
              key={item}
              type="button"
            >
              <span>{index + 1}</span>
              {item}
            </button>
          ))}
        </nav>
      </aside>

      <section className="graph-workbench__canvas" aria-label="Graph canvas placeholder">
        <div className="graph-workbench__toolbar">
          <button type="button">Refresh</button>
          <button type="button">Layout</button>
          <button type="button">Filter</button>
        </div>
        <div className="graph-workbench__stage">
          <div className="graph-workbench__node graph-workbench__node--primary">Entity</div>
          <div className="graph-workbench__edge" />
          <div className="graph-workbench__node graph-workbench__node--secondary">Relation</div>
        </div>
      </section>

      <aside className="graph-workbench__details" aria-label="Selection details">
        <p className="graph-workbench__eyebrow">Selection</p>
        <h2>No node selected</h2>
        <p>Task 7 will connect the live graph API and selection state here.</p>
        <dl>
          <div>
            <dt>Status</dt>
            <dd>Shell ready</dd>
          </div>
          <div>
            <dt>API base</dt>
            <dd>{apiBase || "current origin"}</dd>
          </div>
        </dl>
      </aside>
    </main>
  );
}
