import { Link, useParams } from "react-router";
import { LogView } from "../logs/LogView";
import { useProjects } from "./projects";

/**
 * One project, which is where nearly all the time is spent.
 *
 * What this screen is is the log view; what it holds here is the project it is
 * for, read off the list the shell already fetched rather than asked for again.
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
      <section className="narrow">
        <h1>No such project</h1>
        <p>
          This installation holds no project by that identity. It may have been deleted
          from another browser.
        </p>
        <Link to="/">Back to the projects</Link>
      </section>
    );
  }

  // Keyed by the project, so that switching to another one starts the view
  // rather than carrying the selection, the page and the tail's position of the
  // one that was open into it.
  return <LogView key={project.id} project={project} />;
}
