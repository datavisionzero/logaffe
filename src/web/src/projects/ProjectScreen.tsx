import { Link, useParams } from "react-router";
import { useProjects } from "./projects";

/**
 * One project, which is where nearly all the time is spent.
 *
 * The log view itself — the filters, the entries they leave, the detail of one
 * of them, and the live tail — is the next slice of `docs/ui.md` and is not
 * built yet. What stands here is the project this screen is for, so that the
 * switcher and the list both reach something they name.
 */
export function ProjectScreen() {
  const { id } = useParams();
  const { state } = useProjects();

  if (state.status !== "held") {
    return null;
  }

  const project = state.projects.find((held) => held.id === id);

  if (project === undefined) {
    return (
      <section>
        <h1>No such project</h1>
        <p>
          This installation holds no project by that identity. It may have been deleted
          from another browser.
        </p>
        <Link to="/">Back to the projects</Link>
      </section>
    );
  }

  return (
    <section>
      <h1>{project.name}</h1>
      <p className="quiet">
        The log view is not built yet — see <code>docs/ui.md</code> for what belongs here.
      </p>
    </section>
  );
}
