import { useCallback, useEffect, useState } from "react";
import { api } from "../api/client";
import { Button } from "../components/ui/button";
import { Said } from "./Area";

interface Assignable {
  id: string;
  name: string;
  reached: boolean;
}

/**
 * Which projects one person reaches (ADR 0055).
 *
 * **It is the one screen that changes what somebody else can see.** Everything
 * else in this product narrows to the person reading it; this is the exception,
 * and it is why the administrator role exists.
 *
 * **The list of projects here is every project on the installation**, including
 * the ones the administrator doing this cannot open. Handing out access is
 * exactly the case where somebody works on a project they do not read, and
 * requiring them to assign it to themselves first would make the role
 * self-granting by the back door. What they see of such a project is its name.
 *
 * **Taking one away takes effect on that person's next request.** Nothing caches
 * a reach in front of the store.
 */
export function ProjectAccess({
  userId,
  name,
  onDone,
}: {
  userId: string;
  name: string;
  onDone: () => void;
}) {
  const [projects, setProjects] = useState<Assignable[]>();
  const [refusal, setRefusal] = useState<string>();
  const [busy, setBusy] = useState(false);

  const read = useCallback(async () => {
    try {
      const [all, mine] = await Promise.all([api.GET("/projects"), api.GET("/users")]);

      if (all.data === undefined || mine.data === undefined) {
        setRefusal("This installation did not answer.");
        return;
      }

      // Read one project at a time rather than in one call, because the
      // assignments are the project's own list: there is no endpoint that
      // answers "everything this person reaches" by name, and inventing one
      // would be a second read path for a fact this already carries.
      const reached = new Set<string>();

      await Promise.all(
        all.data.map(async (project) => {
          const { data } = await api.GET("/projects/{id}/access", {
            params: { path: { id: project.id } },
          });

          if (data?.some((assignment) => assignment.userId === userId) === true) {
            reached.add(project.id);
          }
        }),
      );

      setProjects(
        all.data.map((project) => ({
          id: project.id,
          name: project.name,
          reached: reached.has(project.id),
        })),
      );
    } catch {
      setRefusal("This installation did not answer.");
    }
  }, [userId]);

  useEffect(() => void read(), [read]);

  async function change(project: Assignable) {
    setRefusal(undefined);
    setBusy(true);

    try {
      const { response } = project.reached
        ? await api.DELETE("/projects/{id}/access/{userId}", {
            params: { path: { id: project.id, userId } },
          })
        : await api.PUT("/projects/{id}/access/{userId}", {
            params: { path: { id: project.id, userId } },
          });

      if (response.status === 204) {
        await read();
        return;
      }

      setRefusal("This installation refused that.");
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="grid gap-3">
      <p>
        What <strong>{name}</strong> reaches. A project nobody assigned them does not exist
        for them: it is absent from their list rather than refused.
      </p>

      {projects !== undefined && projects.length === 0 && (
        <p className="quiet">This installation holds no projects yet.</p>
      )}

      <div className="flex flex-wrap gap-2">
        {projects?.map((project) => (
          <Button
            key={project.id}
            type="button"
            size="sm"
            variant={project.reached ? "default" : "outline"}
            disabled={busy}
            onClick={() => void change(project)}
          >
            {project.name}
          </Button>
        ))}
      </div>

      <Said problem={refusal} />

      <Button type="button" variant="link" size="sm" className="w-fit px-0" onClick={onDone}>
        Done
      </Button>
    </div>
  );
}
