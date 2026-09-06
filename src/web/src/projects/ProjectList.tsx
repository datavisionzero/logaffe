import { useState } from "react";
import { Link } from "react-router";
import { Button } from "../components/ui/button";
import { Page, PageTitle } from "../components/Page";
import { formatTimestamp } from "../shared/time";
import { CreateProject } from "./CreateProject";
import { arrangedByGroup, useGroups } from "./groups";
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
 *
 * **It is grouped once there are groups.** Projects in no group come first and
 * with no heading over them, so an installation that uses none reads exactly as
 * it did before there were any; the groups follow in the order of their names,
 * and there is nothing to drag.
 */
export function ProjectList() {
  const { state, reload } = useProjects();
  const { state: groups } = useGroups();
  const [creating, setCreating] = useState(false);

  if (state.status === "asking") {
    return (
      <Page>
        <p className="quiet">Asking the installation what it holds…</p>
      </Page>
    );
  }

  if (state.status === "unreachable") {
    return (
      <Page>
        <p className="refusal">This installation did not answer.</p>
      </Page>
    );
  }

  if (state.projects.length === 0) {
    return (
      <Page className="grid gap-4">
        <PageTitle>No projects yet</PageTitle>
        <p className="max-w-prose">
          A project is the unit of separation: every log entry belongs to exactly one, and
          nothing can be delivered until there is one to deliver to. Create the first.
        </p>
        <CreateProject onCreated={reload} />
      </Page>
    );
  }

  const { ungrouped, grouped } = arrangedByGroup(
    state.projects,
    groups.status === "held" ? groups.groups : [],
  );

  return (
    <Page className="grid gap-6">
      <PageTitle>Projects</PageTitle>

      {ungrouped.length > 0 && <Projects projects={ungrouped} />}

      {grouped.map(({ group, projects }) => (
        <section key={group.id} className="grid gap-2">
          <h2 className="text-sm font-semibold text-muted-foreground">{group.name}</h2>

          {/* A group with nothing in it says so rather than being left out: it
              is something the operator made and not a side effect of what the
              projects say, and a list that omitted it would answer *where did
              the group I just created go* (ADR 0039). */}
          {projects.length === 0 ? (
            <p className="quiet">No projects are in this group.</p>
          ) : (
            <Projects projects={projects} />
          )}
        </section>
      ))}

      {creating ? (
        <section className="grid gap-3 rounded-lg border bg-card p-4">
          <h2 className="text-base font-semibold">A new project</h2>
          <CreateProject
            onCreated={() => {
              setCreating(false);
              reload();
            }}
          />
        </section>
      ) : (
        <Button variant="outline" className="w-fit" onClick={() => setCreating(true)}>
          Create a project
        </Button>
      )}
    </Page>
  );
}

/**
 * The rows themselves, which are the same wherever they stand: the headings
 * above them say where they are, and nothing on a row repeats it.
 */
function Projects({ projects }: { projects: HeldProject[] }) {
  return (
    <div className="overflow-x-auto rounded-lg border bg-card">
      <table className="w-full border-collapse text-left text-sm">
        <thead>
          <tr className="border-b">
            <Th>Project</Th>
            <Th>Last entry received</Th>
            <Th>Ingest tokens</Th>
            <Th>Kept for</Th>
          </tr>
        </thead>
        <tbody>
          {projects.map((project) => (
            <ProjectRow key={project.id} project={project} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Th({ children }: { children: string }) {
  return (
    <th
      scope="col"
      className="px-3 py-2 text-xs font-medium tracking-wide text-muted-foreground uppercase"
    >
      {children}
    </th>
  );
}

function ProjectRow({ project }: { project: HeldProject }) {
  return (
    <tr className="border-b last:border-b-0 hover:bg-muted/50">
      <th scope="row" className="px-3 py-2 font-medium">
        <Link
          to={`/project/${project.id}`}
          className="rounded-sm text-brand underline-offset-4 outline-none hover:underline focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          {project.name}
        </Link>
      </th>

      <td className="px-3 py-2">
        {/* The receipt clock and not the event clock: an entry that arrives
            carrying yesterday's timestamp arrived today, and the question this
            column answers is whether the application is still delivering. */}
        {project.lastReceivedAt === null ? (
          <span className="quiet">Nothing has ever arrived</span>
        ) : (
          <time dateTime={project.lastReceivedAt.toISOString()} className="font-mono text-xs">
            {formatTimestamp(project.lastReceivedAt)}
          </time>
        )}
      </td>

      <td className="px-3 py-2">
        <IngestTokens held={project.ingestTokens} />
      </td>

      <td className="px-3 py-2 whitespace-nowrap">{project.retentionDays} days</td>
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
