import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import type { HeldProject } from "../projects/projects";

/**
 * Renaming a project, which moves nothing.
 *
 * Entries, tokens and queries are attached to the identity rather than to the
 * name (`docs/projects.md`), so no sender notices and nothing is redeployed —
 * which is worth saying on the screen, because a name that looks like it is in
 * a token is a name nobody dares change.
 */
export function ProjectName({
  project,
  onRenamed,
}: {
  project: HeldProject;
  onRenamed: () => void;
}) {
  const [name, setName] = useState(project.name);
  const [problem, setProblem] = useState<string>();
  const [renamed, setRenamed] = useState(false);
  const [renaming, setRenaming] = useState(false);

  async function rename(event: FormEvent) {
    event.preventDefault();
    setProblem(undefined);
    setRenamed(false);
    setRenaming(true);

    try {
      const { response, error } = await api.PATCH("/projects/{id}", {
        params: { path: { id: project.id } },
        body: { name },
      });

      if (response.status === 204) {
        setRenamed(true);
        onRenamed();
        return;
      }

      if (response.status === 409) {
        setProblem("This project's group already holds a project by that name.");
        return;
      }

      if (response.status === 404) {
        setProblem("This project is gone. It may have been deleted from another browser.");
        return;
      }

      setProblem(
        response.status === 400
          ? problemWith(error, "name")
          : "This installation refused the rename.",
      );
    } catch {
      setProblem("This installation did not answer.");
    } finally {
      setRenaming(false);
    }
  }

  return (
    <section>
      <h2>Name</h2>
      <p>
        Unique within this project's group, and the only thing about a project a person
        reads. Renaming moves nothing: entries, tokens and queries are attached to the
        project's identity, so no sender notices and nothing has to be redeployed.
      </p>

      <form onSubmit={rename}>
        <label>
          Name
          <input
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              setRenamed(false);
            }}
            aria-invalid={problem !== undefined || undefined}
          />
        </label>
        {problem !== undefined && <p className="refusal">{problem}</p>}
        {renamed && <p className="quiet">Renamed.</p>}

        <button type="submit" disabled={renaming || name.trim() === project.name}>
          Rename the project
        </button>
      </form>
    </section>
  );
}
