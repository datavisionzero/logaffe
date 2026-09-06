import { NavLink, Link, useLocation } from "react-router";
import { ScrollTextIcon, ServerIcon, SettingsIcon } from "lucide-react";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarSeparator,
  useSidebar,
} from "../components/ui/sidebar";
import { ProjectSwitcher } from "../projects/ProjectSwitcher";
import { projectIdIn, useProjects } from "../projects/projects";

/**
 * Every way this application is navigated, on the two levels it has.
 *
 * The two-level header this replaced put the installation across the top and
 * the open project in a row beneath it, and the split was the point of it:
 * before that, a project's settings sat in the status line of the log view and
 * the way back was a sentence — navigation inside the content, which is what
 * left every screen without a place. The split survives the move. The open
 * project is at the top, with the switcher and its two destinations; the
 * installation is at the bottom, behind a separator. Nothing goes back into the
 * content.
 *
 * That this is a column at all reverses what `docs/ui.md` used to say, and
 * [ADR 0051](../../../../docs/adr/0051-the-web-interface-is-tailwind-base-ui-and-planaffes-own-tokens.md)
 * argues it rather than editing the sentence away: logaffe and planaffe are
 * meant to be operated alike and not merely to look alike. The width the log is
 * read in is bought back by the collapse control in the header, whose state is
 * deliberately not remembered.
 *
 * It carries no avatar, no notification bell and nothing that exists to tell
 * people apart, because there is one operator and no user model (`docs/ui.md`).
 */
export function AppSidebar() {
  const { setOpenMobile } = useSidebar();
  const { pathname } = useLocation();
  const { state } = useProjects();

  const at = projectIdIn(pathname);
  const open =
    at !== null && state.status === "held" && state.projects.some((project) => project.id === at)
      ? at
      : null;

  return (
    <Sidebar collapsible="offcanvas">
      <SidebarHeader className="gap-3 px-3 pt-3">
        {/* The way back to the list, and the only place the product names
            itself. */}
        <Link
          to="/"
          onClick={() => setOpenMobile(false)}
          className="flex items-center gap-2 rounded-md px-1 text-sm font-semibold outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          <span aria-hidden className="size-4.5 rounded-sm bg-brand" />
          logaffe
        </Link>

        {/* Navigation, and labelled as such: it is the way into every other
            project and back to the list, and it belongs to the frame rather
            than to any screen. */}
        <nav aria-label="Projects">
          <ProjectSwitcher />
        </nav>
      </SidebarHeader>

      <SidebarContent>
        {/* A group that is present but empty while no project is open would be
            a place the eye learns to skip; this way the second level appearing
            *is* the statement that the operator is inside a project. An address
            naming a project this installation does not hold — ordinarily one
            deleted from another browser — gets none of it either, since both
            links would lead back into the dead end the screen already says. */}
        {open !== null && (
          <SidebarGroup>
            <SidebarGroupLabel>Project</SidebarGroupLabel>
            <SidebarGroupContent>
              <nav aria-label="Project">
                <SidebarMenu>
                  <SidebarMenuItem>
                    <SidebarMenuButton
                      isActive={pathname === `/project/${open}`}
                      render={
                        // `end`, so that the settings address does not also
                        // mark the log.
                        <NavLink end to={`/project/${open}`} onClick={() => setOpenMobile(false)} />
                      }
                    >
                      <ScrollTextIcon />
                      <span>Log</span>
                    </SidebarMenuButton>
                  </SidebarMenuItem>

                  <SidebarMenuItem>
                    <SidebarMenuButton
                      isActive={pathname.startsWith(`/project/${open}/settings`)}
                      render={
                        <NavLink
                          to={`/project/${open}/settings`}
                          onClick={() => setOpenMobile(false)}
                        />
                      }
                    >
                      <SettingsIcon />
                      <span>Project settings</span>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                </SidebarMenu>
              </nav>
            </SidebarGroupContent>
          </SidebarGroup>
        )}
      </SidebarContent>

      <SidebarFooter className="px-2 pb-3">
        <SidebarSeparator className="mx-1" />
        {/* The installation's own: the sessions, the agent tokens, the hosts
            and the operator's credentials. It is named in full because a
            project has settings too, and the two are one list apart. */}
        <nav aria-label="Installation">
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton
                isActive={pathname.startsWith("/settings")}
                render={<NavLink to="/settings" onClick={() => setOpenMobile(false)} />}
              >
                <ServerIcon />
                <span>Installation settings</span>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </nav>
      </SidebarFooter>
    </Sidebar>
  );
}
