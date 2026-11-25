import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// In development the browser only talks to Vite, which forwards /api to ASP.NET Core.
// That keeps one origin in dev, the same as production, where ASP.NET Core serves the built files.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      "/api": { target: "https://localhost:7149", changeOrigin: true, secure: false },
    },
  },
  build: {
    outDir: "../src/ApiArchitect.Api/wwwroot",
    emptyOutDir: true,
  },
});
