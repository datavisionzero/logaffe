import { useState } from "react";
import { Link } from "react-router";
import { formatTimestamp } from "../shared/time";
import { CreateProject } from "./CreateProject";
import { useProjects, type HeldProject } from "./projects";

/**
 * Where a session starts.
 *
 * It is a list of projects and deliberately not a dashboard: no counts, no
 * numbers nobody asked for, and nothing run over the largest table in the
 * database on every sign-in. Each row carries the one fact an operator wants at
 * a glance — when that project last received an entry — and how many ingest
 * tokens it holds, so that a project whose door is closed is visible without
 * opening each one in turn.
 */
export function ProjectList() {
  const { state, reload } = useProjects();
  const [creating, setCreating] = useState(false);

  if (state.status === "asking") {
    return <p className="narrow quiet">Asking the installation what it holds…</p>;
  }

  if (state.status === "unreachable") {
    return <p className="narrow refusal">This installation did not answer.</p>;
  }

  if (state.projects.length === 0) {
    return (
      <section className="narrow">
        <h1>No projects yet</h1>
        <p>
          A project is the unit of separation: every log entry belongs to exactly one, and
          nothing can be delivered until there is one to deliver to. Create the first.
        </p>
        <CreateProject onCreated={reload} />
      </section>
    );
  }

  return (
    <section className="narrow">
      <h1>Projects</h1>

      <table className="projects">
        <thead>
          <tr>
            <th scope="col">Project</th>
            <th scope="col">Last entry received</th>
            <th scope="col">Ingest tokens</th>
            <th scope="col">Kept for</th>
          </tr>
        </thead>
        <tbody>
          {state.projects.map((project) => (
            <ProjectRow key={project.id} project={project} />
          ))}
        </tbody>
      </table>

      {creating ? (
        <section>
          <h2>A new project</h2>
          <CreateProject
            onCreated={() => {
              setCreating(false);
              reload();
            }}
          />
        </section>
      ) : (
        <button type="button" onClick={() => setCreating(true)}>
          Create a project
        </button>
      )}
    </section>
  );
}

function ProjectRow({ project }: { project: HeldProject }) {
  return (
    <tr>
      <th scope="row">
        <Link to={`/project/${project.id}`}>{project.name}</Link>
      </th>

      <td>
        {/* The receipt clock and not the event clock: an entry that arrives
            carrying yesterday's timestamp arrived today, and the question this
            column answers is whether the application is still delivering. */}
        {project.lastReceivedAt === null ? (
          <span className="quiet">Nothing has ever arrived</span>
        ) : (
          <time dateTime={project.lastReceivedAt.toISOString()}>
            {formatTimestamp(project.lastReceivedAt)}
          </time>
        )}
      </td>

      <td>
        <IngestTokens held={project.ingestTokens} />
      </td>

      <td>{project.retentionDays} days</td>
    </tr>
  );
}

/**
 * One ordinarily, two while the project is being rotated, and none for a
 * project whose door is closed — which is the case this column is on the list
 * for, and the one it says in words rather than as a zero.
 */
function IngestTokens({ held }: { held: number }) {
  if (held === 0) {
    return <span className="closed">None — nothing can deliver here</span>;
  }

  return <span>{held === 1 ? "1" : `${held} (rotating)`}</span>;
}
