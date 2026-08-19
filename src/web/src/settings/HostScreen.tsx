import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { api } from "../api/client";
import { SampleBand } from "../hosts/SampleBand";
import { useSamples, useTheMinute } from "../hosts/samples";
import type { HeldHost } from "../hosts/hosts";
import { ReadExpired } from "../logs/CountPanel";
import { formatTimestamp } from "../shared/time";
import { HostTokens } from "./HostTokens";

/** The plain range this screen offers, which is the whole of what it offers. */
const SPANS = {
  "1h": { name: "Last hour", milliseconds: 60 * 60_000 },
  "1d": { name: "Last day", milliseconds: 24 * 60 * 60_000 },
  "1w": { name: "Last week", milliseconds: 7 * 24 * 60 * 60_000 },
} as const;

type Span = keyof typeof SPANS;

/**
 * One host: what it reported, what it reports on, and its end.
 *
 * It draws **the same numbers the band over a project's entries does**, over a
 * plain range rather than over a filter set — for the times the question is
 * about the machine rather than about a project (`docs/metrics.md`). There is
 * nothing here to arrange, save or pick beyond how far back to look, and that
 * is deliberate: this is the second and last place a sample is drawn, not a
 * dashboard that grew out of the band.
 */
export function HostScreen({
  host,
  onChanged,
}: {
  host: HeldHost;
  onChanged: () => void;
}) {
  const [span, setSpan] = useState<Span>("1h");

  // Every range this screen offers is open-ended, so its end is the present and
  // the present advances — once a minute, which is how often there is a new
  // reading to draw.
  const minute = useTheMinute(true);
  const to = minute;
  const from = new Date(minute.getTime() - SPANS[span].milliseconds);

  const samples = useSamples(host.id, from, to, minute.getTime());

  return (
    <section>
      <h2>{host.name}</h2>

      <p className="quiet">
        <Link to="/settings/hosts">Back to the hosts</Link>
      </p>

      <p>
        Last reported{" "}
        {host.lastReportedAt === null ? (
          <b>never</b>
        ) : (
          <time dateTime={host.lastReportedAt.toISOString()}>
            {formatTimestamp(host.lastReportedAt)}
          </time>
        )}
        . {host.projects === 0 ? "No project runs" : host.projects === 1 ? "One project runs" : `${host.projects} projects run`}{" "}
        on it.
      </p>

      <label>
        Over
        <select value={span} onChange={(e) => setSpan(e.target.value as Span)}>
          {Object.entries(SPANS).map(([key, offered]) => (
            <option key={key} value={key}>
              {offered.name}
            </option>
          ))}
        </select>
      </label>

      {samples.status === "asking" && <p className="quiet">Reading the machine…</p>}

      {samples.status === "unreachable" && (
        <p className="refusal">This installation did not answer.</p>
      )}

      {samples.status === "gone" && (
        <p className="refusal">
          This host is gone. It may have been deleted from another browser.
        </p>
      )}

      {samples.status === "expired" && <ReadExpired narrow={samples.narrow} />}

      {samples.status === "held" && (
        <SampleBand window={samples.window} from={from} to={to} />
      )}

      <HostTokens hostId={host.id} onChanged={onChanged} />

      <RenameHost host={host} onRenamed={onChanged} />

      <DeleteHost host={host} onDeleted={onChanged} />
    </section>
  );
}

/** A rename moves no sample: they hang off the identity, not off the name. */
function RenameHost({ host, onRenamed }: { host: HeldHost; onRenamed: () => void }) {
  const [name, setName] = useState(host.name);
  const [problem, setProblem] = useState<string>();
  const [renaming, setRenaming] = useState(false);
  const [renamed, setRenamed] = useState(false);

  async function rename() {
    setProblem(undefined);
    setRenamed(false);
    setRenaming(true);

    try {
      const { response } = await api.PATCH("/hosts/{id}", {
        params: { path: { id: host.id } },
        body: { name },
      });

      if (response.status === 204) {
        setRenamed(true);
        onRenamed();
        return;
      }

      setProblem(
        response.status === 409
          ? "This installation already holds a host by that name."
          : response.status === 404
            ? "This host is gone. It may have been deleted from another browser."
            : "That is not a name.",
      );
    } catch {
      setProblem("This installation did not answer.");
    } finally {
      setRenaming(false);
    }
  }

  return (
    <section>
      <h3>Name</h3>
      <p>
        What this machine is called here. Renaming it moves nothing: the samples, the
        token and the projects on it all point at the host's identity rather than at its
        name.
      </p>

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

      <button
        type="button"
        disabled={renaming || name.trim() === "" || name === host.name}
        onClick={() => void rename()}
      >
        Rename it
      </button>
    </section>
  );
}

/**
 * Ending a host, which is immediate and irreversible.
 *
 * **It is confirmed by typing the name, unlike removing a group**, and the
 * difference is what is destroyed: a group holds nothing, a host holds its
 * samples, and the guard is proportionate to what does not come back
 * (`docs/metrics.md`). The guard is this screen's and the endpoint sees nothing
 * of it, exactly as a project's deletion works.
 *
 * The projects that sat on it are left sitting on none and lose nothing but the
 * band over their entries.
 */
function DeleteHost({ host, onDeleted }: { host: HeldHost; onDeleted: () => void }) {
  const navigate = useNavigate();
  const [typed, setTyped] = useState("");
  const [refusal, setRefusal] = useState<string>();
  const [deleting, setDeleting] = useState(false);

  async function remove() {
    setRefusal(undefined);
    setDeleting(true);

    try {
      const { response } = await api.DELETE("/hosts/{id}", { params: { path: { id: host.id } } });

      // Already gone is another browser or a second click, and it is the end
      // this act was asking for either way.
      if (response.status === 204 || response.status === 404) {
        onDeleted();
        void navigate("/settings/hosts");
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
      <h3>Delete this host</h3>
      <p>
        Immediate and irreversible. The host, its token and its samples go, and there is
        no undelete. A collector still reporting with its token gets <code>401</code> from
        its next sample and carries on doing nothing else.
      </p>
      <p>
        {host.projects === 0
          ? "No project sits on it."
          : `${host.projects} ${host.projects === 1 ? "project is" : "projects are"} left sitting on no host. Nothing else about them changes — they lose the band over their entries and keep every entry they hold.`}
      </p>

      <label>
        Type <b>{host.name}</b> to confirm
        <input value={typed} onChange={(e) => setTyped(e.target.value)} />
      </label>

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      <button
        type="button"
        className="grave"
        disabled={deleting || typed.trim() !== host.name}
        onClick={() => void remove()}
      >
        Delete {host.name}
      </button>
    </section>
  );
}
