import { existsSync } from "node:fs";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const input: Record<string, string> = {
  graphWorkbench: "src/graph-workbench/main.tsx"
};

if (existsSync("src/system-status/main.tsx")) {
  input.systemStatus = "src/system-status/main.tsx";
}

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../wwwroot",
    emptyOutDir: false,
    manifest: false,
    sourcemap: false,
    rollupOptions: {
      preserveEntrySignatures: "strict",
      input,
      output: {
        format: "es",
        entryFileNames: (chunkInfo) => {
          if (chunkInfo.name === "systemStatus") {
            return "system-status/assets/system-status.js";
          }

          return "graph-workbench/assets/graph-workbench.js";
        },
        chunkFileNames: "assets/[name].js",
        assetFileNames: (assetInfo) => {
          const assetNames = assetInfo.names ?? [];
          const normalizedAssetNames = assetNames.map((name) => name.toLowerCase());

          if (assetNames.some((name) => name.endsWith(".css"))) {
            if (normalizedAssetNames.some((name) => name.includes("system-status") || name.includes("systemstatus"))) {
              return "system-status/assets/system-status.css";
            }

            return "graph-workbench/assets/graph-workbench.css";
          }

          return "assets/[name][extname]";
        }
      }
    }
  }
});
