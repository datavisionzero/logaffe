import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router";
import { api, problemWith } from "../api/client";
import { RETENTION_MAXIMUM, RETENTION_MINIMUM, RETENTION_OFFERED } from "./retention";

/**
 * Creating a project, which is the only way one comes about.
 *
 * A name and a retention window is the whole of it (`docs/projects.md`): the
 * ingest token is issued separately, and nothing else about a project is
 * decided here.
 */
export function CreateProject({ onCreated }: { onCreated: () => void }) {
  const navigate = useNavigate();
  const [name, setName] = useState("");
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
        body: { name, retentionDays: Number(retentionDays) },
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
        // of them at three in the morning.
        // A project is created in no group and put into one afterwards, so the
        // name it has to be free of is the one the ungrouped projects hold.
        setProblems({
          name: "This installation already holds a project by that name, in no group.",
        });
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

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      <button type="submit" disabled={creating}>
        Create the project
      </button>
    </form>
  );
}
