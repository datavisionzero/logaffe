import { useNavigate, useParams } from "react-router";
import { useProjects } from "./projects";

/**
 * Present everywhere.
 *
 * Moving from one project to another is the frequent act, and it should never
 * be a trip back to a start page (`docs/ui.md`) — so this sits in the shell
 * rather than on any one screen.
 */
export function ProjectSwitcher() {
  const { state } = useProjects();
  const { id } = useParams();
  const navigate = useNavigate();

  if (state.status !== "held" || state.projects.length === 0) {
    return null;
  }

  return (
    <label className="switcher">
      <span className="visually-hidden">Project</span>
      <select
        value={id ?? ""}
        onChange={(event) => void navigate(`/project/${event.target.value}`)}
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
