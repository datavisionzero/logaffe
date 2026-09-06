import { useEffect, useState, type ReactNode } from "react";
import { Link, Route, Routes } from "react-router";
import { api } from "../api/client";
import { Button } from "../components/ui/button";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "../components/ui/sidebar";
import { ProjectList } from "../projects/ProjectList";
import { ProjectScreen } from "../projects/ProjectScreen";
import { GroupsProvider } from "../projects/groups";
import { ProjectsProvider } from "../projects/projects";
import { InstallationSettings } from "../settings/InstallationSettings";
import { ProjectSettings } from "../settings/ProjectSettings";
import { browserTimeZone } from "../shared/time";
import { AccountMenu } from "./AccountMenu";
import { AppSidebar } from "./AppSidebar";

/**
 * What is around every screen of a signed-in installation.
 *
 * Navigation is a column on the left and an account menu at the top right —
 * planaffe's model, taken deliberately, because the two products are meant to
 * be operated alike and not merely to look alike
 * ([ADR 0051](../../../../docs/adr/0051-the-web-interface-is-tailwind-base-ui-and-planaffes-own-tokens.md)).
 * What the column holds and why it is split the way it is belongs to
 * `AppSidebar`; what is here is the frame around it.
 *
 * The frame is what scrolls least: the sidebar and this header stay where they
 * are and the surface below is what moves, so navigation is never something to
 * scroll back up to. The surface says nothing about its own width — each screen
 * decides how wide it is and how much of the height it takes, which is what
 * lets the log view be the full height of what is left.
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
      <SidebarProvider className="min-h-0 flex-1">
        <AppSidebar />

        <SidebarInset className="m-0 min-w-0 max-w-none overflow-hidden p-0">
          <header className="flex h-12 shrink-0 items-center gap-2 border-b px-3">
            {/* The handle the log view takes its width back with. It is not a
                setting and nothing remembers it: every load starts expanded,
                which is what keeps `docs/ui.md`'s rule against layout settings
                true and every screenshot comparable to every other. */}
            <SidebarTrigger />

            <div className="flex-1" />

            {/* Every timestamp below is in this zone, absolute and to the
                millisecond, and there is no toggle to another one. Too long a
                sentence for a narrow window, where the account menu says it
                instead. */}
            <span className="hidden text-xs text-muted-foreground sm:inline">
              Times in {browserTimeZone()}
            </span>

            <AccountMenu onSignOut={() => void signOut()} />
          </header>

          <NoSecondFactor />

          {/* A set of backup codes that quietly runs out ends at Host Recovery,
              so the product says how many remain whenever one is spent. */}
          {remaining !== null && (
            <Notice>
              A backup code was spent signing in.{" "}
              {remaining === 0
                ? "None are left — issue a fresh set."
                : `${remaining} ${remaining === 1 ? "code is" : "codes are"} left.`}{" "}
              <Button variant="link" size="xs" onClick={() => setRemaining(null)}>
                Dismiss
              </Button>
            </Notice>
          )}

          <div className="flex min-h-0 flex-1 flex-col overflow-auto [scrollbar-gutter:stable]">
            {/* The SPA's addresses are singular where the contract's are
                plural, and that is load-bearing rather than a matter of taste:
                the server falls back to `index.html` only for what no endpoint
                matched, and `/projects/{id}` is an endpoint — so a reload of a
                plural address would answer JSON instead of this application.
                Every screen below therefore names a space no route of
                `docs/api/openapi.json` occupies. */}
            <Routes>
              <Route path="/" element={<ProjectList />} />
              <Route path="/project/:id" element={<ProjectScreen />} />
              <Route path="/project/:id/settings" element={<ProjectSettings />} />
              {/* An area of a settings screen is an address of its own, so that
                  a reload comes back to it and the back button walks the ones
                  just opened. The screen without a segment is its first area. */}
              <Route path="/project/:id/settings/:section" element={<ProjectSettings />} />
              <Route path="/settings" element={<InstallationSettings />} />
              <Route path="/settings/:section" element={<InstallationSettings />} />
              {/* One area carries an address of its own inside it, because a
                  host is a screen rather than a row: what it reported, what it
                  reports on, and its end. */}
              <Route path="/settings/hosts/:hostId" element={<InstallationSettings />} />
              <Route path="*" element={<ProjectList />} />
            </Routes>
          </div>
        </SidebarInset>
      </SidebarProvider>
    </WhatTheInstallationHolds>
  );
}

/** A sentence the frame says above the surface, and never inside it. */
function Notice({ children }: { children: ReactNode }) {
  return (
    <p className="shrink-0 border-b border-brand/30 bg-brand-soft px-3 py-2 text-sm text-foreground">
      {children}
    </p>
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
    <Notice>
      This installation has no second factor. Its password is the only thing between the
      internet and everything it holds.{" "}
      <Link to="/settings/credentials" className="font-medium underline underline-offset-4">
        Enrol one
      </Link>
      .
    </Notice>
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
