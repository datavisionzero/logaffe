import { useState } from "react";
import { Link, Route, Routes } from "react-router";
import { api } from "../api/client";
import { ProjectList } from "../projects/ProjectList";
import { ProjectScreen } from "../projects/ProjectScreen";
import { ProjectSwitcher } from "../projects/ProjectSwitcher";
import { ProjectsProvider } from "../projects/projects";
import { browserTimeZone } from "../shared/time";

/**
 * What is around every screen of a signed-in installation.
 *
 * It carries no user furniture — no avatar, no notification bell, nothing that
 * exists to tell people apart — because there is one operator and no user model
 * (`docs/ui.md`). What it does carry is the project switcher, which is present
 * everywhere, and the zone every timestamp on the screen is in, stated once so
 * that no screen is ambiguous about it.
 */
export function Shell({
  backupCodesRemaining,
  onSignedOut,
}: {
  backupCodesRemaining: number | null;
  onSignedOut: () => void;
}) {
  const [remaining, setRemaining] = useState(backupCodesRemaining);

  async function signOut() {
    try {
      await api.POST("/sign-out");
    } finally {
      onSignedOut();
    }
  }

  return (
    <ProjectsProvider>
      <header className="shell">
        <Link to="/" className="wordmark">
          logaffe
        </Link>

        <ProjectSwitcher />

        {/* Every timestamp below is in this zone, absolute and to the
            millisecond, and there is no toggle to another one. */}
        <span className="zone">Times in {browserTimeZone()}</span>

        <button type="button" className="plain" onClick={() => void signOut()}>
          Sign out
        </button>
      </header>

      {/* A set of backup codes that quietly runs out ends at Host Recovery, so
          the product says how many remain whenever one is spent. */}
      {remaining !== null && (
        <p className="notice">
          A backup code was spent signing in.{" "}
          {remaining === 0
            ? "None are left — issue a fresh set."
            : `${remaining} ${remaining === 1 ? "code is" : "codes are"} left.`}{" "}
          <button type="button" className="plain" onClick={() => setRemaining(null)}>
            Dismiss
          </button>
        </p>
      )}

      {/* The log view is the full width of the window and the project list is
          a column, so the surface is not constrained here — each screen says
          how wide it is. */}
      <main className="surface">
        {/* The SPA's addresses are singular where the contract's are plural,
            and that is load-bearing rather than a matter of taste: the server
            falls back to `index.html` only for what no endpoint matched, and
            `/projects/{id}` is an endpoint — so a reload of a plural address
            would answer JSON instead of this application. Every screen below
            therefore names a space no route of `docs/api/openapi.json` occupies. */}
        <Routes>
          <Route path="/" element={<ProjectList />} />
          <Route path="/project/:id" element={<ProjectScreen />} />
          <Route path="*" element={<ProjectList />} />
        </Routes>
      </main>
    </ProjectsProvider>
  );
}
