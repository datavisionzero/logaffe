import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router";
import { App } from "./App";
import { ThemeProvider } from "./components/theme-provider";
import "./index.css";

const root = document.getElementById("root");
if (!root) {
  throw new Error("The page is missing its root element.");
}

createRoot(root).render(
  <StrictMode>
    {/* Puts `dark` or `light` on <html> from the operating system's colour
        scheme, which is the only thing that decides it (ADR 0051). */}
    <ThemeProvider>
      {/* The address bar is where a view is kept: a reload comes back to the
          same one and the back button walks what was just narrowed
          (`docs/ui.md`). */}
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </ThemeProvider>
  </StrictMode>,
);
