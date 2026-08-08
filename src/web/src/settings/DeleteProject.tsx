import { useState } from "react";
import { useNavigate } from "react-router";
import { api } from "../api/client";
import type { HeldProject } from "../projects/projects";

/**
 * Ending a project, which is immediate and irreversible.
 *
 * **The guard is this screen's and the server sees nothing of it.** The route
 * takes an identity and no typed name: repeating the name back would protect
 * nobody who issued the request deliberately, and it would make one route answer
 * to a rule none of the others do (`docs/projects.md`). What the typing is for
 * is the operator with two tabs open, and that is a thing an interface can do
 * something about and an endpoint cannot.
 *
 * The project, its tokens and its visibility go at once; the entries follow in
 * the background
 * ([ADR 0019](docs/adr/0019-a-project-is-deleted-at-once-and-its-entries-follow.md)).
 */
export function DeleteProject({
  project,
  onDeleted,
}: {
  project: HeldProject;
  onDeleted: () => void;
}) {
  const navigate = useNavigate();
  const [typed, setTyped] = useState("");
  const [refusal, setRefusal] = useState<string>();
  const [deleting, setDeleting] = useState(false);

  async function remove() {
    setRefusal(undefined);
    setDeleting(true);

    try {
      const { response } = await api.DELETE("/projects/{id}", {
        params: { path: { id: project.id } },
      });

      // Already gone is another browser or a second click, and it is the end
      // this act was asking for either way.
      if (response.status === 204 || response.status === 404) {
        onDeleted();
        void navigate("/");
        return;
      }

      setRefusal("This installation refused the deletion.");
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setDeleting(false);
    }
  }

  return (
    <section className="grave">
      <h2>Delete this project</h2>
      <p>
        Immediate and irreversible. The project, its tokens and everything that reads it
        go at once, and its entries are removed afterwards in the background. There is no
        undelete and no archive.
      </p>
      <p>
        Anything still holding an ingest token gets <code>401</code> from its next
        delivery and carries on writing its own local file, exactly as it would through a
        botched rotation.
      </p>

      <label>
        Type <b>{project.name}</b> to confirm
        <input value={typed} onChange={(e) => setTyped(e.target.value)} />
      </label>

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      <button
        type="button"
        className="grave"
        disabled={deleting || typed.trim() !== project.name}
        onClick={() => void remove()}
      >
        Delete {project.name}
      </button>
    </section>
  );
}
