import { useState } from "react";
import { Link, NavLink, Route, Routes, useLocation } from "react-router";
import { api } from "../api/client";
import { ProjectList } from "../projects/ProjectList";
import { ProjectScreen } from "../projects/ProjectScreen";
import { ProjectSwitcher } from "../projects/ProjectSwitcher";
import { projectIdIn, ProjectsProvider, useProjects } from "../projects/projects";
import { InstallationSettings } from "../settings/InstallationSettings";
import { ProjectSettings } from "../settings/ProjectSettings";
import { browserTimeZone } from "../shared/time";

/**
 * What is around every screen of a signed-in installation.
 *
 * It carries no user furniture — no avatar, no notification bell, nothing that
 * exists to tell people apart — because there is one operator and no user model
 * (`docs/ui.md`). What it does carry is every way this application is navigated,
 * on two levels: the installation across the top, and the project being read
 * below it.
 *
 * The split is what the two levels actually are. The switcher, the zone, the
 * installation's own settings and the sign-out are true wherever the operator
 * is; the log and a project's settings only exist while a project is open, and
 * so does the row that holds them. Before this, a project's settings sat in the
 * status line of the log view and the way back was a sentence — navigation
 * inside the content, which is what left every screen without a place.
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
        <div className="shell-bar">
          <Link to="/" className="wordmark">
            logaffe
          </Link>

          <ProjectSwitcher />

          {/* Every timestamp below is in this zone, absolute and to the
              millisecond, and there is no toggle to another one. */}
          <span className="zone">Times in {browserTimeZone()}</span>

          {/* The installation's own: the sessions, the agent tokens and the
              operator's credentials. It is named in full because a project has
              settings too, and the two are one row apart on this screen. */}
          <nav className="shell-acts" aria-label="Installation">
            <NavLink to="/settings">Installation settings</NavLink>

            <button type="button" className="plain" onClick={() => void signOut()}>
              Sign out
            </button>
          </nav>
        </div>

        <ProjectTabs />
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

      {/* The log view is the full height of what is left below the shell and the
          project list is a column, so the surface is not constrained here — each
          screen says how wide it is and how much of the height it takes. */}
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
          <Route path="/project/:id/settings" element={<ProjectSettings />} />
          <Route path="/settings" element={<InstallationSettings />} />
          <Route path="*" element={<ProjectList />} />
        </Routes>
      </main>
    </ProjectsProvider>
  );
}

/**
 * The two surfaces a project has, shown while one is open and not otherwise.
 *
 * A row that is present but empty on the project list would be a place the eye
 * learns to skip; this way the second level appearing *is* the statement that
 * the operator is inside a project.
 *
 * An address naming a project this installation does not hold — ordinarily one
 * deleted from another browser — gets no tabs, since both of them would lead
 * back into the same dead end the screen is already saying.
 */
function ProjectTabs() {
  const at = projectIdIn(useLocation().pathname);
  const { state } = useProjects();

  if (at === null || state.status !== "held") {
    return null;
  }

  if (!state.projects.some((project) => project.id === at)) {
    return null;
  }

  return (
    <nav className="shell-tabs" aria-label="Project">
      {/* `end`, so that the settings address does not also mark the log. */}
      <NavLink end to={`/project/${at}`}>
        Log
      </NavLink>
      <NavLink to={`/project/${at}/settings`}>Project settings</NavLink>
    </nav>
  );
}
