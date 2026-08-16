import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import { projectsIn, useGroups, type HeldGroup } from "../projects/groups";
import { useProjects } from "../projects/projects";

/**
 * The headings the projects are listed under.
 *
 * **They live here rather than inside a project** for the same reason the agent
 * tokens do: a group is a fact about the installation's projects taken together,
 * and no single project's screen can hold one. A project's own settings say
 * which group it is in, which is all a project knows about the matter.
 *
 * A group is a name and nothing else — no retention its projects inherit, no
 * token, no colour, and nothing to query (ADR 0039). What it is *for* is finding
 * a project on a list of twenty, and that is the whole of it.
 */
export function Groups() {
  const { state, reload } = useGroups();
  const { state: projects, reload: reloadProjects } = useProjects();
  const [renaming, setRenaming] = useState<string>();
  const [removing, setRemoving] = useState<string>();
  const [name, setName] = useState("");
  const [problem, setProblem] = useState<string>();
  const [refusal, setRefusal] = useState<string>();
  const [busy, setBusy] = useState(false);

  /**
   * Every act here ends by reading the list back, and a refusal ends instead.
   * `false` is a refusal already placed where it belongs — the field it is
   * about — and reading the list back after one would be asking the installation
   * for something nobody asked for.
   */
  async function act(perform: () => Promise<string | undefined | false>) {
    setBusy(true);
    setRefusal(undefined);
    setProblem(undefined);

    try {
      const refused = await perform();

      if (refused === undefined) {
        reload();

        // The projects carry the identity of their group, so a removal changes
        // where they are listed. Nothing else here touches them.
        reloadProjects();
      } else if (refused !== false) {
        setRefusal(refused);
      }
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  async function create(event: FormEvent) {
    event.preventDefault();

    await act(async () => {
      const { data, error, response } = await api.POST("/groups", { body: { name } });

      if (data === undefined) {
        if (response.status === 400) {
          setProblem(problemWith(error, "name"));
          return false;
        }

        if (response.status === 409) {
          setProblem("This installation already holds a group by that name.");
          return false;
        }

        return "This installation refused to make the group.";
      }

      setName("");
      return undefined;
    });
  }

  const rename = (id: string, renamed: string) =>
    act(async () => {
      const { response, error } = await api.PATCH("/groups/{id}", {
        params: { path: { id } },
        body: { name: renamed },
      });

      setRenaming(undefined);

      // A rename moves no project: a project points at the identity rather than
      // at the name, which is what the identity is there for.
      if (response.status === 204 || response.status === 404) {
        return undefined;
      }

      if (response.status === 409) {
        return "This installation already holds a group by that name.";
      }

      return response.status === 400
        ? (problemWith(error, "name") ?? "That is not a name.")
        : "This installation refused the rename.";
    });

  const remove = (id: string) =>
    act(async () => {
      const { response } = await api.DELETE("/groups/{id}", { params: { path: { id } } });

      setRemoving(undefined);

      return response.status === 204 || response.status === 404
        ? undefined
        : "This installation refused to remove the group.";
    });

  return (
    <section>
      <h2>Groups</h2>
      <p>
        A group is a heading the projects are listed under — one product's environments,
        one customer's applications. It is for finding a project and nothing else: it holds
        no retention window, no token, and nothing can be asked of it, because a search
        always names one project.
      </p>

      {state.status === "asking" && <p className="quiet">Reading the groups…</p>}

      {state.status === "unreachable" && (
        <p className="refusal">This installation did not answer.</p>
      )}

      {state.status === "held" && state.groups.length === 0 && (
        <p className="quiet">
          There are no groups. Every project is listed on its own, which is what an
          installation with a handful of them wants.
        </p>
      )}

      {state.status === "held" && state.groups.length > 0 && (
        <table className="listing">
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Projects</th>
              <th scope="col">
                <span className="visually-hidden">Acts</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {state.groups.map((group) => {
              // Counted off the projects this application already holds, so
              // that a project moved a moment ago is in the number.
              const held = projects.status === "held" ? projectsIn(group, projects.projects) : 0;

              return (
                <tr key={group.id}>
                  <th scope="row">
                    {renaming === group.id ? (
                      <Rename
                        group={group}
                        busy={busy}
                        onRename={(renamed) => void rename(group.id, renamed)}
                        onLeave={() => setRenaming(undefined)}
                      />
                    ) : (
                      group.name
                    )}
                  </th>
                  <td>{projects.status === "held" ? held : ""}</td>
                  <td>
                    <button
                      type="button"
                      className="plain"
                      onClick={() => setRenaming(group.id)}
                    >
                      Rename
                    </button>{" "}
                    {removing === group.id ? (
                      <>
                        {/* Nothing is destroyed, so this states what happens
                            rather than asking for the name to be typed — that
                            guard belongs to deleting a project, where entries do
                            not come back (ADR 0039). */}
                        <button
                          type="button"
                          className="plain"
                          disabled={busy}
                          onClick={() => void remove(group.id)}
                        >
                          {held === 0
                            ? "Remove it"
                            : `Remove it — ${held} ${
                                held === 1 ? "project is" : "projects are"
                              } left in no group`}
                        </button>{" "}
                        <button
                          type="button"
                          className="plain"
                          onClick={() => setRemoving(undefined)}
                        >
                          Keep it
                        </button>
                      </>
                    ) : (
                      <button
                        type="button"
                        className="plain"
                        onClick={() => setRemoving(group.id)}
                      >
                        Remove
                      </button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      <p className="quiet">
        Removing a group destroys nothing: the projects in it stay exactly as they were and
        are listed on their own again. A project is put into a group in that project's own
        settings.
      </p>

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      <form onSubmit={create}>
        <label>
          Name for a new group
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            aria-invalid={problem !== undefined || undefined}
          />
        </label>
        {problem !== undefined && <p className="refusal">{problem}</p>}

        <button type="submit" disabled={busy || name.trim() === ""}>
          Make a group
        </button>
      </form>
    </section>
  );
}

/**
 * Renaming a group moves no project: they point at its identity rather than at
 * its name, and a rename is a word on a heading changing and nothing else.
 */
function Rename({
  group,
  busy,
  onRename,
  onLeave,
}: {
  group: HeldGroup;
  busy: boolean;
  onRename: (renamed: string) => void;
  onLeave: () => void;
}) {
  const [renamed, setRenamed] = useState(group.name);

  return (
    <>
      <input
        value={renamed}
        onChange={(e) => setRenamed(e.target.value)}
        aria-label={`Name of ${group.name}`}
      />
      <button
        type="button"
        className="plain"
        disabled={busy || renamed.trim() === ""}
        onClick={() => onRename(renamed)}
      >
        Save
      </button>{" "}
      <button type="button" className="plain" onClick={onLeave}>
        Cancel
      </button>
    </>
  );
}
