import { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router";
import { addressOf, carriedToAnotherProject, filtersIn } from "../logs/filters";
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
 */
export function ProjectSwitcher() {
  const { state } = useProjects();
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

  return (
    <div className="switcher" ref={box}>
      <button
        type="button"
        ref={button}
        className="switcher-button"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
      >
        {/* Two spans and the space between them, which is what the button is
            called: "Project billing" and not the two run together. */}
        <span className="switcher-label">Project</span>{" "}
        <span className={held === null ? "switcher-name quiet" : "switcher-name"}>
          {held === null ? "Choose a project" : held.name}
        </span>
        <span aria-hidden="true">▾</span>
      </button>

      {open && (
        <ul className="switcher-menu">
          {/* The way back to the list, which is otherwise only the wordmark. */}
          <li className="switcher-all">
            <Link to="/">All projects</Link>
          </li>

          {state.projects.map((project) => (
            <li key={project.id}>
              <Link
                to={`/project/${project.id}${carried}`}
                aria-current={project.id === at ? "true" : undefined}
              >
                {project.name}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
