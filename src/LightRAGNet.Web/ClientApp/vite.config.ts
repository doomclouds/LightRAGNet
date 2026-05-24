import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../wwwroot",
    emptyOutDir: false,
    manifest: false,
    sourcemap: false,
    rollupOptions: {
      preserveEntrySignatures: "strict",
      input: {
        graphWorkbench: "src/graph-workbench/main.tsx",
        cacheManagement: "src/cache-management/main.tsx"
      },
      output: {
        format: "es",
        entryFileNames: (chunkInfo) => {
          if (chunkInfo.name === "cacheManagement") {
            return "cache-management/assets/cache-management.js";
          }

          return "graph-workbench/assets/graph-workbench.js";
        },
        chunkFileNames: "assets/[name].js",
        assetFileNames: (assetInfo) => {
          const assetNames = [...(assetInfo.names ?? []), ...(assetInfo.originalFileNames ?? [])];

          if (assetNames.some((name) => name === "cacheManagement.css" || name.endsWith("cache-management.css"))) {
            return "cache-management/assets/cache-management.css";
          }

          if (assetNames.some((name) => name === "graphWorkbench.css" || name.endsWith(".css"))) {
            return "graph-workbench/assets/graph-workbench.css";
          }

          return "assets/[name][extname]";
        }
      }
    }
  }
});
