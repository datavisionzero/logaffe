import { useState } from "react";
import { Link } from "react-router";
import { api } from "../api/client";
import { useGroups } from "../projects/groups";
import type { HeldProject } from "../projects/projects";

/** The value the select carries for a project in no group. */
const none = "";

/**
 * Which group a project is listed under.
 *
 * **It is chosen here and made elsewhere.** A project's settings say where the
 * project sits; a screen about one project is the wrong place to bring into
 * existence a thing that outlives it, so making, renaming and removing a group
 * is in the installation's settings (`docs/ui.md`).
 *
 * Moving a project moves nothing else. Entries, tokens and queries are attached
 * to its identity, so no sender notices and nothing has to be redeployed — the
 * heading it appears under is the whole of what changes.
 */
export function ProjectGroup({
  project,
  onMoved,
}: {
  project: HeldProject;
  onMoved: () => void;
}) {
  const { state } = useGroups();
  const [problem, setProblem] = useState<string>();
  const [moved, setMoved] = useState(false);
  const [moving, setMoving] = useState(false);

  async function move(groupId: string) {
    setProblem(undefined);
    setMoved(false);
    setMoving(true);

    try {
      const { response } = await api.PUT("/projects/{id}/group", {
        params: { path: { id: project.id } },
        body: { groupId: groupId === none ? null : groupId },
      });

      if (response.status === 204) {
        setMoved(true);
        onMoved();
        return;
      }

      // A name is unique within its group, so a group already holding a project
      // by this one's name refuses the move rather than resolving it — renaming
      // a project nobody asked to rename is not this screen's to do.
      if (response.status === 409) {
        setProblem(
          "That group already holds a project by this name. Rename one of the two first.",
        );
        return;
      }

      setProblem(
        response.status === 404
          ? "This project or that group is gone. It may have been changed from another browser."
          : "This installation refused the move.",
      );
    } catch {
      setProblem("This installation did not answer.");
    } finally {
      setMoving(false);
    }
  }

  return (
    <section>
      <h2>Group</h2>
      <p>
        The heading this project is listed under, which is there so that a list of twenty
        projects can be read. Moving it changes nothing else: deliveries carry on
        untouched, nothing has to be redeployed, and a group is never what a search runs
        inside.
      </p>

      {state.status === "unreachable" && (
        <p className="refusal">This installation did not answer.</p>
      )}

      {state.status === "held" && state.groups.length === 0 ? (
        <p className="quiet">
          This installation holds no groups.{" "}
          <Link to="/settings/groups">Make one in the installation's settings</Link>, and
          this project can be put into it here.
        </p>
      ) : (
        <label>
          Group
          <select
            value={project.groupId ?? none}
            disabled={moving || state.status !== "held"}
            onChange={(e) => void move(e.target.value)}
            aria-invalid={problem !== undefined || undefined}
          >
            <option value={none}>No group</option>
            {state.status === "held" &&
              [...state.groups]
                .sort((one, other) => one.name.localeCompare(other.name))
                .map((group) => (
                  <option key={group.id} value={group.id}>
                    {group.name}
                  </option>
                ))}
          </select>
        </label>
      )}

      {problem !== undefined && <p className="refusal">{problem}</p>}
      {moved && <p className="quiet">Moved.</p>}
    </section>
  );
}
