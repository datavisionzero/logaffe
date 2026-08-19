// From vitest rather than vite, so that the test section below is typed too.
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],

  // A local `npm run build` lands where the server serves static files from, so
  // that one `dotnet run` gives the whole product. The image build does the same
  // thing across two stages.
  build: {
    outDir: "../Logaffe.Api/wwwroot",
    emptyOutDir: true,
  },

  // In development the two toolchains run side by side and nothing joins them:
  // Vite serves the SPA and forwards what belongs to the backend. Every route
  // of the contract is listed, because the session cookie is `SameSite=Strict`
  // and only reaches the installation when the browser thinks it is talking to
  // one origin — which is what this forwarding is for.
  server: {
    port: 5173,
    proxy: Object.fromEntries(
      [
        "/health",
        "/openapi",
        "/ingest",
        "/ingest-tokens",
        "/claim",
        "/sign-in",
        "/sign-out",
        "/sessions",
        "/password",
        "/backup-codes",
        "/second-factor",
        "/projects",
        "/agent-tokens",
        "/hosts",
        "/host-tokens",
        "/samples",
      ].map((route) => [route, "http://localhost:5142"]),
    ),
  },

  test: {
    environment: "jsdom",
    setupFiles: ["./src/shared/setupTests.ts"],
  },
});
