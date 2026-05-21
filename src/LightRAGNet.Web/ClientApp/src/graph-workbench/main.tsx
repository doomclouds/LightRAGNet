import React from "react";
import { createRoot } from "react-dom/client";
import type { Root } from "react-dom/client";
import { GraphWorkbench } from "./GraphWorkbench";
import "../styles/graph-workbench.css";

const mountedRoots = new Map<string, Root>();

export function mountGraphWorkbench(elementId: string, apiBase = ""): void {
  const rootElement = document.getElementById(elementId);

  if (!rootElement) {
    return;
  }

  unmountGraphWorkbench(elementId);

  const root = createRoot(rootElement);
  mountedRoots.set(elementId, root);
  root.render(
    <React.StrictMode>
      <GraphWorkbench apiBase={apiBase} />
    </React.StrictMode>
  );
}

export function unmountGraphWorkbench(elementId: string): void {
  const root = mountedRoots.get(elementId);

  if (!root) {
    return;
  }

  root.unmount();
  mountedRoots.delete(elementId);
}
