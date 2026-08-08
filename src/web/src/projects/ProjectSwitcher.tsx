import { useNavigate, useParams, useSearchParams } from "react-router";
import { addressOf, carriedToAnotherProject, filtersIn } from "../logs/filters";
import { useProjects } from "./projects";

/**
 * Present everywhere.
 *
 * Moving from one project to another is the frequent act, and it should never
 * be a trip back to a start page (`docs/ui.md`) — so this sits in the shell
 * rather than on any one screen.
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
  const { id } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();

  if (state.status !== "held" || state.projects.length === 0) {
    return null;
  }

  const carried = addressOf(carriedToAnotherProject(filtersIn(params)));

  return (
    <label className="switcher">
      <span className="visually-hidden">Project</span>
      <select
        value={id ?? ""}
        onChange={(event) => void navigate(`/project/${event.target.value}${carried}`)}
      >
        <option value="" disabled>
          Choose a project
        </option>
        {state.projects.map((project) => (
          <option key={project.id} value={project.id}>
            {project.name}
          </option>
        ))}
      </select>
    </label>
  );
}
