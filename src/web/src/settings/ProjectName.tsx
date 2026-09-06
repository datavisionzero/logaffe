import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import type { HeldProject } from "../projects/projects";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { Field } from "../components/Field";
import { About, Area, Said } from "./Area";

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
    <Area title="Name">
      <About>
        Unique within this project's group, and the only thing about a project a person
        reads. Renaming moves nothing: entries, tokens and queries are attached to the
        project's identity, so no sender notices and nothing has to be redeployed.
      </About>

      <form onSubmit={rename} className="grid max-w-md gap-3">
        <Field label="Name">
          <Input
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              setRenamed(false);
            }}
            aria-invalid={problem !== undefined || undefined}
          />
        </Field>

        <Said problem={problem} note={renamed ? "Renamed." : undefined} />

        <Button
          type="submit"
          disabled={renaming || name.trim() === project.name}
          className="w-fit"
        >
          Rename the project
        </Button>
      </form>
    </Area>
  );
}
