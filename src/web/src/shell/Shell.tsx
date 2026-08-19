import { useEffect, useState, type ReactNode } from "react";
import { Link, NavLink, Route, Routes, useLocation } from "react-router";
import { api } from "../api/client";
import { ProjectList } from "../projects/ProjectList";
import { ProjectScreen } from "../projects/ProjectScreen";
import { ProjectSwitcher } from "../projects/ProjectSwitcher";
import { GroupsProvider } from "../projects/groups";
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
    <WhatTheInstallationHolds>
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

      <NoSecondFactor />

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
          {/* An area of a settings screen is an address of its own, so that a
              reload comes back to it and the back button walks the ones just
              opened. The screen without a segment is its first area. */}
          <Route path="/project/:id/settings/:section" element={<ProjectSettings />} />
          <Route path="/settings" element={<InstallationSettings />} />
          <Route path="/settings/:section" element={<InstallationSettings />} />
          {/* One area carries an address of its own inside it, because a host
              is a screen rather than a row: what it reported, what it reports
              on, and its end. */}
          <Route path="/settings/hosts/:hostId" element={<InstallationSettings />} />
          <Route path="*" element={<ProjectList />} />
        </Routes>
      </main>
    </WhatTheInstallationHolds>
  );
}

/**
 * An installation running behind a password alone says so, for as long as that
 * is true.
 *
 * The second factor is optional (ADR 0041), and the interface is the only thing
 * that can keep an omission from passing for a setting — so this is **not
 * dismissible**. It is not a warning about something that went wrong; it is the
 * state of the account, and it goes away by enrolling one.
 */
function NoSecondFactor() {
  const [enrolled, setEnrolled] = useState<boolean>();

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const { data } = await api.GET("/second-factor");

        if (current && data !== undefined) {
          setEnrolled(data.isEnrolled);
        }
      } catch {
        // Asked once and never insisted on. A banner that cannot be shown is
        // not a thing to say a second sentence about.
      }
    })();

    return () => {
      current = false;
    };
  }, []);

  if (enrolled !== false) {
    return null;
  }

  return (
    <p className="notice">
      This installation has no second factor. Its password is the only thing between the
      internet and everything it holds.{" "}
      <Link to="/settings/credentials">Enrol one</Link>.
    </p>
  );
}

/**
 * The two answers every screen below is a reading of: the projects, and the
 * headings they are listed under.
 *
 * They are two requests and one moment — the application asks for both once,
 * when a session starts, and never again on a timer. The groups are not folded
 * into the project rows because a group holding no projects is one the operator
 * made and has to be shown all the same (`docs/ui.md`), and nothing the projects
 * say would carry it.
 */
function WhatTheInstallationHolds({ children }: { children: ReactNode }) {
  return (
    <ProjectsProvider>
      <GroupsProvider>{children}</GroupsProvider>
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
