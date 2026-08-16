import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router";
import { api, problemWith } from "../api/client";
import { useGroups } from "./groups";
import { RETENTION_MAXIMUM, RETENTION_MINIMUM, RETENTION_OFFERED } from "./retention";

/** The value the select carries for a project in no group. */
const none = "";

/**
 * Creating a project, which is the only way one comes about.
 *
 * A name, a retention window and the group to list it under (`docs/projects.md`):
 * the ingest token is issued separately, and nothing else about a project is
 * decided here.
 *
 * **The group is chosen here rather than only afterwards.** Creating a project
 * and putting it where it belongs is one errand, and sending the operator into
 * the new project's settings to finish it is a second trip for something they
 * already knew. It offers no group by default, which is what most projects are
 * and the only thing an installation holding no groups could mean.
 */
export function CreateProject({ onCreated }: { onCreated: () => void }) {
  const navigate = useNavigate();
  const { state: groups } = useGroups();
  const [name, setName] = useState("");
  const [groupId, setGroupId] = useState(none);
  const [retentionDays, setRetentionDays] = useState(String(RETENTION_OFFERED));
  const [refusal, setRefusal] = useState<string>();
  const [problems, setProblems] = useState<{ name?: string; retentionDays?: string }>({});
  const [creating, setCreating] = useState(false);

  async function create(event: FormEvent) {
    event.preventDefault();
    setRefusal(undefined);
    setProblems({});
    setCreating(true);

    try {
      const { data, response, error } = await api.POST("/projects", {
        body: {
          name,
          retentionDays: Number(retentionDays),
          groupId: groupId === none ? null : groupId,
        },
      });

      if (data !== undefined) {
        // Straight into the project that was just made, because creating one is
        // something an operator does on the way to using it.
        onCreated();
        void navigate(`/project/${data.id}`);
        return;
      }

      if (response.status === 409) {
        // Two projects called `api` is a trap for the operator reaching for one
        // of them at three in the morning. The name has to be free where the
        // project will be listed, which is inside the group it is being given
        // or among the projects in no group.
        setProblems({
          name:
            groupId === none
              ? "This installation already holds a project by that name, in no group."
              : "That group already holds a project by that name.",
        });
        return;
      }

      if (response.status === 404) {
        // The group was removed from another browser while this form was open.
        setRefusal("That group is gone. It may have been removed from another browser.");
        setGroupId(none);
        return;
      }

      if (response.status === 400) {
        setProblems({
          name: problemWith(error, "name"),
          retentionDays: problemWith(error, "retentionDays"),
        });
        return;
      }

      setRefusal("This installation refused to create the project.");
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setCreating(false);
    }
  }

  return (
    <form onSubmit={create}>
      <label>
        Name
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          aria-invalid={problems.name !== undefined || undefined}
        />
      </label>
      {problems.name !== undefined && <p className="refusal">{problems.name}</p>}

      <label>
        Kept for
        <input
          type="number"
          min={RETENTION_MINIMUM}
          max={RETENTION_MAXIMUM}
          value={retentionDays}
          onChange={(e) => setRetentionDays(e.target.value)}
          aria-invalid={problems.retentionDays !== undefined || undefined}
        />
        days
      </label>
      {problems.retentionDays !== undefined && <p className="refusal">{problems.retentionDays}</p>}

      {/* Absent while the installation holds none: a select whose only option
          is "no group" asks the operator to decide something that has one
          possible answer. */}
      {groups.status === "held" && groups.groups.length > 0 && (
        <label>
          Group
          <select value={groupId} onChange={(e) => setGroupId(e.target.value)}>
            <option value={none}>No group</option>
            {[...groups.groups]
              .sort((one, other) => one.name.localeCompare(other.name))
              .map((group) => (
                <option key={group.id} value={group.id}>
                  {group.name}
                </option>
              ))}
          </select>
        </label>
      )}

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      <button type="submit" disabled={creating}>
        Create the project
      </button>
    </form>
  );
}
