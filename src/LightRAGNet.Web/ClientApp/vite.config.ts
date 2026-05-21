import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../wwwroot/graph-workbench",
    emptyOutDir: true,
    manifest: false,
    sourcemap: false,
    rollupOptions: {
      input: {
        graphWorkbench: "src/graph-workbench/main.tsx"
      },
      output: {
        entryFileNames: "assets/graph-workbench.js",
        chunkFileNames: "assets/[name].js",
        assetFileNames: (assetInfo) => {
          if (assetInfo.names?.some((name) => name.endsWith(".css"))) {
            return "assets/graph-workbench.css";
          }

          return "assets/[name][extname]";
        }
      }
    }
  }
});
