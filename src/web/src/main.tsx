import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router";
import { App } from "./App";
import "./index.css";

const root = document.getElementById("root");
if (!root) {
  throw new Error("The page is missing its root element.");
}

createRoot(root).render(
  <StrictMode>
    {/* The address bar is where a view is kept: a reload comes back to the same
        one and the back button walks what was just narrowed (`docs/ui.md`). */}
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
);
