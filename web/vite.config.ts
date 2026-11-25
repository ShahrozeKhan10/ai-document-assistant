import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In development, Vite forwards /api calls to the ASP.NET Core API,
// so the browser sees one origin and no CORS setup is needed.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    open: true,
    proxy: {
      "/api": "http://localhost:5080",
    },
  },
});
