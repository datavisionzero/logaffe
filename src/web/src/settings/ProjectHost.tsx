import { useState } from "react";
import { Link } from "react-router";
import { api } from "../api/client";
import { byName, useHosts } from "../hosts/hosts";
import type { HeldProject } from "../projects/projects";

/** The value the select carries for a project on no host. */
const none = "";

/**
 * Which machine a project runs on.
 *
 * It is the whole of the relation (`docs/metrics.md`): what it buys is the band
 * over this project's entries, and nothing else changes. **A host is not a
 * scope** — no query takes one, no filter narrows by one, and naming two
 * projects onto one machine does not make them askable together.
 *
 * **A project replicated across two machines names one of them or neither.**
 * That is a real limitation and not an oversight: the truthful owner of a host
 * is the instance, which is a property a sender writes into its own entries
 * rather than something the installation manages.
 *
 * It is chosen here and the host is made in the installation's settings, for
 * the reason the group already has: a screen about one project is the wrong
 * place to bring into existence a thing that outlives it.
 */
export function ProjectHost({
  project,
  onMoved,
}: {
  project: HeldProject;
  onMoved: () => void;
}) {
  const { state } = useHosts();
  const [problem, setProblem] = useState<string>();
  const [moved, setMoved] = useState(false);
  const [moving, setMoving] = useState(false);

  async function put(hostId: string) {
    setProblem(undefined);
    setMoved(false);
    setMoving(true);

    try {
      const { response } = await api.PUT("/projects/{id}/host", {
        params: { path: { id: project.id } },
        body: { hostId: hostId === none ? null : hostId },
      });

      if (response.status === 204) {
        setMoved(true);
        onMoved();
        return;
      }

      setProblem(
        response.status === 404
          ? "This project or that host is gone. It may have been changed from another browser."
          : "This installation refused the change.",
      );
    } catch {
      setProblem("This installation did not answer.");
    } finally {
      setMoving(false);
    }
  }

  return (
    <section>
      <h2>Runs on</h2>
      <p>
        The machine this project runs on, which is what puts a band of that machine's
        processor, memory and disk above this project's entries. It is not a scope: no
        search narrows by a host, and two projects on one machine are still two projects.
      </p>

      {state.status === "asking" && <p className="quiet">Reading the hosts…</p>}

      {state.status === "unreachable" && (
        <p className="refusal">This installation did not answer.</p>
      )}

      {state.status === "held" && state.hosts.length === 0 ? (
        <p className="quiet">
          This installation holds no hosts.{" "}
          <Link to="/settings/hosts">Make a host in the installation's settings</Link>,
          start its collector, and this project can be put on it here.
        </p>
      ) : (
        <label>
          Host
          <select
            value={project.hostId ?? none}
            disabled={moving || state.status !== "held"}
            onChange={(e) => void put(e.target.value)}
            aria-invalid={problem !== undefined || undefined}
          >
            <option value={none}>No host</option>
            {state.status === "held" &&
              byName(state.hosts).map((host) => (
                <option key={host.id} value={host.id}>
                  {host.name}
                </option>
              ))}
          </select>
        </label>
      )}

      {problem !== undefined && <p className="refusal">{problem}</p>}
      {moved && <p className="quiet">Saved.</p>}
    </section>
  );
}
