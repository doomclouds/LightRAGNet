import React from "react";
import { createRoot } from "react-dom/client";
import type { Root } from "react-dom/client";
import { SystemStatusWorkbench } from "./SystemStatusWorkbench";
import "../styles/system-status.css";

const mountedRoots = new Map<string, Root>();

export function mountSystemStatus(elementId: string, apiBase = ""): void {
  const rootElement = document.getElementById(elementId);

  if (!rootElement) {
    return;
  }

  unmountSystemStatus(elementId);

  const root = createRoot(rootElement);
  mountedRoots.set(elementId, root);
  root.render(
    <React.StrictMode>
      <SystemStatusWorkbench apiBase={apiBase} />
    </React.StrictMode>
  );
}

export function unmountSystemStatus(elementId: string): void {
  const root = mountedRoots.get(elementId);

  if (!root) {
    return;
  }

  root.unmount();
  mountedRoots.delete(elementId);
}
