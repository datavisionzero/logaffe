import { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router";
import { ChevronsUpDownIcon } from "lucide-react";
import { addressOf, carriedToAnotherProject, filtersIn } from "../logs/filters";
import { arrangedByGroup, groupOf, useGroups } from "./groups";
import { projectIdIn, useProjects } from "./projects";

/**
 * Present everywhere, and the thing the shell is built around.
 *
 * Moving from one project to another is the frequent act, and it should never
 * be a trip back to a start page (`docs/ui.md`) — so this sits in the shell
 * rather than on any one screen. It is a menu rather than a bare `<select>`
 * because it does two jobs at once: it says which project is being read, which
 * is the only place on the log view that name appears, and it is the way into
 * every other one and back to the list.
 *
 * **Switching keeps the time range and the level threshold and drops everything
 * else.** Those two are questions about the world — *the last fifteen minutes*,
 * *warnings and worse* — and carrying them over is what makes "the same five
 * minutes in the other service" one click. An instance, a logger name, a trace
 * or a search text belongs to the project it was found in, and carrying it into
 * another one would produce an empty list that looks like an outage.
 *
 * **It names the group beside the project.** A project's name is unique only
 * within its group (`docs/projects.md`), and this is the one place a project is
 * named while the list it stands in is nowhere on the screen — reading `api`
 * alone above a log that could be either of two would be the three-in-the-
 * morning trap moved rather than removed.
 */
export function ProjectSwitcher() {
  const { state } = useProjects();
  const { state: groups } = useGroups();
  const { pathname, search } = useLocation();
  const [open, setOpen] = useState(false);

  const box = useRef<HTMLDivElement | null>(null);
  const button = useRef<HTMLButtonElement | null>(null);

  // Navigating is what the menu is for, so arriving anywhere closes it.
  useEffect(() => setOpen(false), [pathname, search]);

  useEffect(() => {
    if (!open) {
      return;
    }

    function elsewhere(event: MouseEvent) {
      if (box.current !== null && !box.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }

    function escape(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setOpen(false);
        button.current?.focus();
      }
    }

    document.addEventListener("mousedown", elsewhere);
    document.addEventListener("keydown", escape);

    return () => {
      document.removeEventListener("mousedown", elsewhere);
      document.removeEventListener("keydown", escape);
    };
  }, [open]);

  if (state.status !== "held" || state.projects.length === 0) {
    return null;
  }

  const at = projectIdIn(pathname);
  const held = state.projects.find((project) => project.id === at) ?? null;
  const carried = addressOf(carriedToAnotherProject(filtersIn(new URLSearchParams(search))));
  const under = held === null ? null : groupOf(held, groups);

  const { ungrouped, grouped } = arrangedByGroup(
    state.projects,
    groups.status === "held" ? groups.groups : [],
  );

  return (
    <div className="relative" ref={box}>
      <button
        type="button"
        ref={button}
        aria-expanded={open}
        onClick={() => setOpen(!open)}
        className="flex w-full items-center gap-2 rounded-lg border border-transparent px-2 py-1.5 text-left text-sm outline-none hover:bg-sidebar-accent focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-expanded:bg-sidebar-accent"
      >
        {/* Two spans and the space between them, which is what the button is
            called: "Project billing" and not the two run together. The group
            stands in front of the name where there is one, because the name
            alone is unique only inside it. */}
        <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
          Project
        </span>{" "}
        <span className={held === null ? "quiet flex-1 truncate" : "flex-1 truncate font-medium"}>
          {held === null ? "Choose a project" : under === null ? held.name : `${under} / ${held.name}`}
        </span>
        <ChevronsUpDownIcon aria-hidden className="size-3.5 shrink-0 text-muted-foreground" />
      </button>

      {open && (
        <ul className="absolute z-20 mt-1 max-h-96 w-full min-w-56 overflow-auto rounded-lg border bg-popover p-1 text-popover-foreground shadow-md">
          {/* The way back to the list, which is otherwise only the wordmark. */}
          <li className="mb-1 border-b pb-1">
            <Link to="/" className={row}>
              All projects
            </Link>
          </li>

          {/* The same arrangement the list has, for the same reason: the
              projects in no group first, then the groups by name. */}
          {ungrouped.map((project) => (
            <li key={project.id}>
              <Link
                to={`/project/${project.id}${carried}`}
                aria-current={project.id === at ? "true" : undefined}
                className={row}
              >
                {project.name}
              </Link>
            </li>
          ))}

          {grouped
            .filter(({ projects }) => projects.length > 0)
            .map(({ group, projects }) => (
              <li key={group.id}>
                {/* A heading in a menu and not a thing to choose: a group is
                    for finding a project, never for opening. */}
                <p className="px-2 pt-2 pb-1 text-xs font-medium tracking-wide text-muted-foreground uppercase">
                  {group.name}
                </p>
                <ul>
                  {projects.map((project) => (
                    <li key={project.id}>
                      <Link
                        to={`/project/${project.id}${carried}`}
                        aria-current={project.id === at ? "true" : undefined}
                        className={row}
                      >
                        {project.name}
                      </Link>
                    </li>
                  ))}
                </ul>
              </li>
            ))}
        </ul>
      )}
    </div>
  );
}

/**
 * One line of the menu. It stays a link rather than becoming a menu item: these
 * are addresses, and an operator who opens one in a second tab is doing
 * something the product should not have to be asked about.
 */
const row =
  "block truncate rounded-md px-2 py-1.5 text-sm outline-none hover:bg-accent focus-visible:bg-accent aria-[current=true]:bg-accent aria-[current=true]:font-medium";
