import React from "react";
import { createRoot } from "react-dom/client";
import type { Root } from "react-dom/client";

import { CacheManagementWorkbench } from "./CacheManagementWorkbench";
// Keep the query so Vite inlines shared theme CSS into this Razor fixed CSS asset.
import "../styles/theme.css?cache-management";
import "../styles/cache-management.css";

const mountedRoots = new Map<string, Root>();

export function mountCacheManagement(elementId: string, apiBase = ""): void {
  const rootElement = document.getElementById(elementId);

  if (!rootElement) {
    return;
  }

  unmountCacheManagement(elementId);

  const root = createRoot(rootElement);
  mountedRoots.set(elementId, root);
  root.render(
    <React.StrictMode>
      <CacheManagementWorkbench apiBase={apiBase} />
    </React.StrictMode>
  );
}

export function unmountCacheManagement(elementId: string): void {
  const root = mountedRoots.get(elementId);

  if (!root) {
    return;
  }

  root.unmount();
  mountedRoots.delete(elementId);
}
