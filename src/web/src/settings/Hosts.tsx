import { useState, type FormEvent } from "react";
import { Link } from "react-router";
import { api, problemWith } from "../api/client";
import { byName, useHosts } from "../hosts/hosts";
import { useProjects } from "../projects/projects";
import { LastUse } from "./LastUse";
import { HostScreen } from "./HostScreen";
import { SampleRetention } from "./SampleRetention";

/**
 * The machines the operator runs their projects on.
 *
 * **They are here for the reason the groups are**: a host is a fact about the
 * installation's projects taken together, and no single project's screen can
 * hold one. A project's own settings say which machine it runs on, which is all
 * a project knows about the matter.
 *
 * They hold more than a group does, though, and that is the whole difference
 * between the two areas: a group is a name, and a host is a name, a token, a
 * collector command and a history of what the machine was doing. So a host has
 * **a screen of its own** inside this area — an address like every other
 * (`docs/ui.md`) — and the list is a list.
 *
 * The window every host's samples are kept for sits here too, because it is one
 * number for the installation rather than one per machine.
 */
export function Hosts({ hostId }: { hostId: string | undefined }) {
  const { state, reload } = useHosts();
  const { reload: reloadProjects } = useProjects();
  const [name, setName] = useState("");
  const [problem, setProblem] = useState<string>();
  const [refusal, setRefusal] = useState<string>();
  const [busy, setBusy] = useState(false);

  function changed() {
    reload();

    // A project carries the identity of the host it runs on, so a deletion
    // changes what the log view draws over its entries. Nothing else here
    // touches them.
    reloadProjects();
  }

  async function create(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);
    setProblem(undefined);

    try {
      const { data, error, response } = await api.POST("/hosts", { body: { name } });

      if (data === undefined) {
        if (response.status === 400) {
          setProblem(problemWith(error, "name"));
        } else if (response.status === 409) {
          setProblem("This installation already holds a host by that name.");
        } else {
          setRefusal("This installation refused to make the host.");
        }

        return;
      }

      setName("");
      reload();
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  // A host's own screen, which is an address inside this area rather than a
  // fourth thing the rail lists.
  if (hostId !== undefined) {
    if (state.status === "asking") {
      return null;
    }

    if (state.status === "unreachable") {
      return <p className="refusal">This installation did not answer.</p>;
    }

    const host = state.hosts.find((held) => held.id === hostId);

    if (host === undefined) {
      return (
        <section>
          <h2>No such host</h2>
          <p>
            This installation holds no host by that identity. It may have been deleted
            from another browser.
          </p>
          <Link to="/settings/hosts">Back to the hosts</Link>
        </section>
      );
    }

    return <HostScreen host={host} onChanged={changed} />;
  }

  return (
    <>
      <section>
        <h2>Hosts</h2>
        <p>
          A host is a machine you run projects on. It holds its samples and the token its
          collector reports with, and what it buys is a band of processor, memory and disk
          above the entries of every project that sits on it.
        </p>
        <p>
          A host that has never reported is an ordinary state, not a fault: it is what a
          host is between being made and its collector being started, and what it becomes
          when its machine is switched off.
        </p>

        {state.status === "asking" && <p className="quiet">Reading the hosts…</p>}

        {state.status === "unreachable" && (
          <p className="refusal">This installation did not answer.</p>
        )}

        {state.status === "held" && state.hosts.length === 0 && (
          <p className="quiet">
            There are no hosts. Every project runs on none, which is what an installation
            that has not asked this question yet looks like.
          </p>
        )}

        {state.status === "held" && state.hosts.length > 0 && (
          <table className="listing">
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Last reported</th>
                <th scope="col">Projects</th>
                <th scope="col">Tokens</th>
              </tr>
            </thead>
            <tbody>
              {byName(state.hosts).map((host) => (
                <tr key={host.id}>
                  <th scope="row">
                    <Link to={`/settings/hosts/${host.id}`}>{host.name}</Link>
                  </th>
                  {/* Read off its newest sample rather than written beside it:
                      a field saying the host reported a minute ago while its
                      newest sample is a day old is the disagreement that comes
                      free with storing the same fact twice (ADR 0039). */}
                  <td>
                    <LastUse at={host.lastReportedAt} />
                  </td>
                  <td>{host.projects}</td>
                  {/* Nothing can report to a host holding none, which is the
                      same closed door the project list names. */}
                  <td className={host.hostTokens === 0 ? "closed" : undefined}>
                    {host.hostTokens}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        <p className="quiet">
          Open a host to draw what it reported, to read its collector command back, to
          rename it or to delete it.
        </p>

        {refusal !== undefined && <p className="refusal">{refusal}</p>}

        <form onSubmit={create}>
          <label>
            Name for a new host
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              aria-invalid={problem !== undefined || undefined}
            />
          </label>
          {problem !== undefined && <p className="refusal">{problem}</p>}

          <button type="submit" disabled={busy || name.trim() === ""}>
            Make a host
          </button>
        </form>

        <p className="quiet">
          Making one does not hand back the command that starts its collector — issuing
          its token does, exactly as an ingest token hands back a delivery snippet. Open
          the host and issue one.
        </p>
      </section>

      <SampleRetention />
    </>
  );
}
