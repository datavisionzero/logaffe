import { useParams } from "react-router";
import { LogView } from "../logs/LogView";
import { NoSuchProject, useProjectAtHand } from "./projects";

/**
 * One project, which is where nearly all the time is spent.
 *
 * What this screen is is the log view; what it holds here is the project it is
 * for, read off the list the shell already fetched rather than asked for again.
 */
export function ProjectScreen() {
  const { id } = useParams();
  const at = useProjectAtHand(id);

  if (at.at === "asking") {
    return null;
  }

  if (at.at === "unknown") {
    return <NoSuchProject />;
  }

  // Keyed by the project, so that switching to another one starts the view
  // rather than carrying the selection, the page and the tail's position of the
  // one that was open into it.
  return <LogView key={at.project.id} project={at.project} />;
}
