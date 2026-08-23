import { useParams } from "react-router";
import { NoSuchProject, useProjectAtHand, useProjects } from "../projects/projects";
import { DeleteProject } from "./DeleteProject";
import { IngestTokens } from "./IngestTokens";
import { ProjectGroup } from "./ProjectGroup";
import { ProjectHost } from "./ProjectHost";
import { ProjectMute } from "./ProjectMute";
import { ProjectName } from "./ProjectName";
import { RetentionWindow } from "./RetentionWindow";
import { SettingsScreen } from "./SettingsScreen";

/**
 * What is changed rarely about one project.
 *
 * It is a screen over acts that already exist, and it is the second of the two
 * surfaces a project has — reached from the shell's project tabs beside the log,
 * not from somewhere a session starts.
 *
 * Three areas, in the order an operator is likely to want them: what the project
 * is — its name, the group it is listed under, the machine it runs on and how
 * long it keeps entries — what it is delivered to on, and its end. **The end is an area of its own
 * rather than the bottom of the first**, because an act that destroys data and
 * cannot be undone should be arrived at rather than scrolled past.
 *
 * The project is read off the list the shell already fetched, so opening these
 * settings asks the installation for nothing until an area that needs something
 * is opened.
 */
export function ProjectSettings() {
  const { id, section } = useParams();
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
    <SettingsScreen
      heading={project.name}
      at={`/project/${project.id}/settings`}
      section={section}
      groups={[
        {
          at: null,
          name: "The project",
          panel: (
            <>
              <ProjectName project={project} onRenamed={reload} />
              <ProjectGroup project={project} onMoved={reload} />
              <ProjectHost project={project} onMoved={reload} />
              <RetentionWindow project={project} onChanged={reload} />
              <ProjectMute project={project} onMuted={reload} />
            </>
          ),
        },
        {
          at: "tokens",
          name: "Ingest tokens",
          panel: <IngestTokens projectId={project.id} onChanged={reload} />,
        },
        {
          at: "delete",
          name: "Delete this project",
          panel: <DeleteProject project={project} onDeleted={reload} />,
        },
      ]}
    />
  );
}
