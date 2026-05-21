import React from "react";
import { createRoot } from "react-dom/client";
import { GraphWorkbench } from "./GraphWorkbench";
import "../styles/graph-workbench.css";

const rootElement = document.getElementById("graph-workbench-root");

if (rootElement) {
  createRoot(rootElement).render(
    <React.StrictMode>
      <GraphWorkbench apiBase={rootElement.dataset.apiBase ?? ""} />
    </React.StrictMode>
  );
}
