import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { crx } from "@crxjs/vite-plugin";
import manifest from "./manifest.json" assert { type: "json" };

// React 16 needs the classic JSX runtime (no automatic runtime).
export default defineConfig({
  plugins: [
    react({ jsxRuntime: "classic" }),
    crx({ manifest })
  ],
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: true
  },
  server: {
    port: 5173,
    strictPort: true,
    cors: {
      origin: [/^chrome-extension:\/\//]
    },
    hmr: { port: 5173 }
  }
});
