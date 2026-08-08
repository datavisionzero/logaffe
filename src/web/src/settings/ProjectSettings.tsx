import { Link, useParams } from "react-router";
import { NoSuchProject, useProjectAtHand, useProjects } from "../projects/projects";
import { DeleteProject } from "./DeleteProject";
import { IngestTokens } from "./IngestTokens";
import { ProjectName } from "./ProjectName";
import { RetentionWindow } from "./RetentionWindow";

/**
 * What is changed rarely about one project.
 *
 * It is a screen over acts that already exist, and it is deliberately reachable
 * from the log view rather than being somewhere a session starts: the name, the
 * window entries leave by, the tokens they arrive on, and the end of the project
 * — in the order an operator is likely to want them, with the irreversible one
 * last.
 *
 * The project is read off the list the shell already fetched, so opening these
 * settings asks the installation for the tokens and nothing else.
 */
export function ProjectSettings() {
  const { id } = useParams();
  const at = useProjectAtHand(id);
  const { reload } = useProjects();

  if (at.at === "asking") {
    return null;
  }

  if (at.at === "unknown") {
    return <NoSuchProject />;
  }

  const project = at.project;

  return (
    <section className="narrow settings">
      <h1>{project.name}</h1>
      <p>
        <Link to={`/project/${project.id}`}>Back to the log</Link>
      </p>

      <ProjectName project={project} onRenamed={reload} />
      <RetentionWindow project={project} onChanged={reload} />
      <IngestTokens projectId={project.id} onChanged={reload} />
      <DeleteProject project={project} onDeleted={reload} />
    </section>
  );
}
