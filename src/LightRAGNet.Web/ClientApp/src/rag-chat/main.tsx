import React from "react";
import { createRoot } from "react-dom/client";
import type { Root } from "react-dom/client";

import { RagChatWorkbench } from "./RagChatWorkbench";
// Keep the query so Vite inlines shared theme CSS into this Razor fixed CSS asset.
import "../styles/theme.css?rag-chat";
import "../styles/rag-chat.css";

const mountedRoots = new Map<string, Root>();

export function mountRagChat(rootElementId: string, apiBase = ""): void {
  const rootElement = document.getElementById(rootElementId);

  if (!rootElement) {
    return;
  }

  unmountRagChat(rootElementId);

  const root = createRoot(rootElement);
  mountedRoots.set(rootElementId, root);
  root.render(
    <React.StrictMode>
      <RagChatWorkbench apiBase={apiBase} />
    </React.StrictMode>
  );
}

export function unmountRagChat(rootElementId: string): void {
  const root = mountedRoots.get(rootElementId);

  if (!root) {
    return;
  }

  root.unmount();
  mountedRoots.delete(rootElementId);
}
