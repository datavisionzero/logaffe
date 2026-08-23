import { useEffect, useState } from "react";
import { Link } from "react-router";
import { api } from "../api/client";
import { byName, useHosts } from "../hosts/hosts";
import { hours, type HeldAlerts } from "./alerting";

/** The value the host select carries for an installation that names none. */
const none = "";

/**
 * The four conditions, each with the switch that turns it on and what it
 * currently works out to in this installation's own numbers.
 *
 * **The set is closed.** There is no rule to write, no threshold to type,
 * nothing to attach to a filter and no fifth condition — what each of them
 * compares against is derived from this installation's own recent history rather
 * than from a number somebody guessed, which is the whole case for a closed set
 * (`docs/alerts.md`). All four are off until they are switched on.
 *
 * **Each switch says what it will actually do**, in projects the operator can
 * name and hours they can picture, because a switch whose behaviour has to be
 * looked up in a document is one that gets turned on once and then distrusted —
 * and distrust is the failure mode the whole feature is designed against.
 *
 * **A condition that cannot be evaluated says so here**, rather than sitting
 * switched on and silent. An operator who believes a disk is being watched when
 * it is not is worse off than one who was never offered the switch.
 */
export function AlertConditions({
  alerts,
  onChanged,
}: {
  alerts: HeldAlerts;
  onChanged: () => void;
}) {
  const { switches, store, quiet, flood, failure } = alerts;
  const { state: hostState } = useHosts();

  const [mounts, setMounts] = useState<string[]>([]);
  const [chosen, setChosen] = useState(store.hostId ?? none);
  const [problem, setProblem] = useState<string>();
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (chosen === none) {
      setMounts([]);
      return;
    }

    let current = true;

    void (async () => {
      try {
        const { data } = await api.GET("/hosts/{id}/mounts", {
          params: { path: { id: chosen } },
        });

        if (current) {
          setMounts(data ?? []);
        }
      } catch {
        if (current) {
          setMounts([]);
        }
      }
    })();

    return () => {
      current = false;
    };
  }, [chosen]);

  async function flip(
    which: "fillingUp" | "goneQuiet" | "flooding" | "failing",
    on: boolean,
  ) {
    setProblem(undefined);
    setBusy(true);

    try {
      // All four every time, because they are one setting with four parts.
      const { response } = await api.PUT("/alerts/switches", {
        body: { ...switches, [which]: on },
      });

      if (response.status === 204) {
        onChanged();
        return;
      }

      setProblem("This installation refused the change.");
    } catch {
      setProblem("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  /**
   * The pair goes together: a mount without a machine is a string, and a machine
   * without a mount does not say which of its filesystems the database is on. So
   * choosing a machine loads what it reports and writes nothing, and choosing one
   * of those mounts is what names both.
   */
  async function name(hostId: string | null, mount: string | null) {
    setProblem(undefined);
    setBusy(true);

    try {
      const { response } = await api.PUT("/alerts/host", { body: { hostId, mount } });

      if (response.status === 204) {
        onChanged();
        return;
      }

      setProblem(
        response.status === 404
          ? "That host is gone. It may have been deleted from another browser."
          : "This installation refused the change.",
      );
    } catch {
      setProblem("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  // The mount already named is offered even when the machine has stopped
  // reporting it, so that it is on the screen it has to be corrected on rather
  // than silently absent from it — but only while the machine on the screen is
  // still the one it belongs to, because a mount of the old machine offered
  // under the new one is a path that was never on it.
  const mount = chosen === store.hostId ? (store.mount ?? none) : none;
  const offered =
    mount === none || mounts.includes(mount) ? mounts : [...mounts, mount];

  return (
    <section>
      <h2>The conditions</h2>
      <p>
        Four things this installation will say something about unasked, and no others.
        There is no rule to write and no threshold to type: what each of them compares
        against comes from this installation's own recent history, which is why there is
        nothing here to guess wrong. All four are off until you switch them on.
      </p>

      {problem !== undefined && <p className="refusal">{problem}</p>}

      <fieldset>
        <legend>The store is filling up</legend>

        <label className="confirm">
          <input
            type="checkbox"
            checked={switches.fillingUp}
            disabled={busy}
            onChange={(e) => void flip("fillingUp", e.target.checked)}
          />
          Say something when the disk under this installation fills
        </label>

        <p className="quiet">
          Once when the filesystem holding this installation's database crosses{" "}
          {store.firstThreshold} per cent, and again at {store.secondThreshold}. It is read
          off what that machine's collector already reports every minute, so there is no
          disk size to type in and nothing to go stale.
        </p>

        <label>
          The machine this installation runs on
          <select
            value={chosen}
            disabled={busy || hostState.status !== "held"}
            onChange={(e) => {
              setChosen(e.target.value);

              if (e.target.value === none) {
                void name(null, null);
              }
            }}
          >
            <option value={none}>No machine</option>
            {hostState.status === "held"
              && byName(hostState.hosts).map((host) => (
                <option key={host.id} value={host.id}>
                  {host.name}
                </option>
              ))}
          </select>
        </label>

        {chosen !== none && (
          <label>
            The mount holding its database
            <select
              value={mount}
              disabled={busy || offered.length === 0}
              onChange={(e) => void name(chosen, e.target.value)}
            >
              <option value={none} disabled>
                Pick a mount
              </option>
              {offered.map((mount) => (
                <option key={mount} value={mount}>
                  {mount}
                </option>
              ))}
            </select>
          </label>
        )}

        {chosen !== none && mounts.length === 0 && (
          <p className="quiet">
            That machine is reporting no filesystems. Its mounts are named in its
            collector's configuration, so this is a collector that was told to watch none —
            or one that has not reported yet.
          </p>
        )}

        <Blind alerts={alerts} />

        <p className="quiet">
          The machine itself is an ordinary host, made and named{" "}
          <Link to="/settings/hosts">with the others</Link>. Naming it here is what lets
          this installation read its own disk, and nothing else about it changes.
        </p>
      </fieldset>

      <fieldset>
        <legend>A project has gone quiet</legend>

        <label className="confirm">
          <input
            type="checkbox"
            checked={switches.goneQuiet}
            disabled={busy}
            onChange={(e) => void flip("goneQuiet", e.target.checked)}
          />
          Say something when a project stops delivering
        </label>

        <p className="quiet">
          Nothing received for more than {quiet.multiple} times the project's own longest
          quiet stretch of the last {quiet.baselineDays} days, and never sooner than{" "}
          {hours(quiet.leastToleratedHours)}. A project that is idle every night is
          described by its nights rather than woken for them.
        </p>

        {quiet.busiest === null || quiet.quietest === null ? (
          <p className="quiet">
            No project here has enough history for this to fire yet, so switching it on
            says nothing until one has.
          </p>
        ) : (
          <p className="notice">
            As things stand: <strong>{quiet.busiest.name}</strong> would be noticed after{" "}
            {hours(quiet.busiest.toleratedHours)} of silence, and{" "}
            <strong>{quiet.quietest.name}</strong> after{" "}
            {hours(quiet.quietest.toleratedHours)}. Both are judged on the hour, so the
            notification arrives some time after that.
          </p>
        )}

        <Fortnight alerts={alerts} />
      </fieldset>

      <fieldset>
        <legend>A project is delivering far more than it does</legend>

        <label className="confirm">
          <input
            type="checkbox"
            checked={switches.flooding}
            disabled={busy}
            onChange={(e) => void flip("flooding", e.target.checked)}
          />
          Say something when a project floods
        </label>

        <p className="quiet">
          A closed hour above {flood.multiple} times the median of that hour of the day
          across the last {flood.baselineDays} days, and above a floor of{" "}
          {flood.floor.toLocaleString()} entries. The median is by hour of the day, so a
          batch job at three in the morning is normal at three in the morning — and the
          floor is absolute, so two entries becoming twenty is never an incident.
        </p>

        <Fortnight alerts={alerts} />
      </fieldset>

      <fieldset>
        <legend>A project is failing far more than it does</legend>

        <label className="confirm">
          <input
            type="checkbox"
            checked={switches.failing}
            disabled={busy}
            onChange={(e) => void flip("failing", e.target.checked)}
          />
          Say something when a project starts failing
        </label>

        <p className="quiet">
          {failure.multiple} times the median of that hour of the day across the last{" "}
          {failure.baselineDays} days, counted over entries at Error or above, with a
          floor of {failure.floor.toLocaleString()} under it — and true of{" "}
          {failure.consecutiveHours === 2 ? "two" : failure.consecutiveHours} closed hours
          in a row. It is the condition above narrowed to what went wrong and slowed down.
        </p>

        <p className="quiet">
          The second hour is what a deploy and a retry storm do not survive: both are over
          inside one hour, so neither says anything. What that costs is the time —
          something that starts failing at the top of an hour is said two hours later, and
          up to three when it starts too late in an hour to reach the floor in what is
          left of it. A late true alarm beats a false one.
        </p>

        <Fortnight alerts={alerts} />
      </fieldset>

      <p className="quiet">
        A project can be taken out of the last three on its own settings screen, beside its
        group and its host. That is the whole of what varies per project: there is no
        threshold here, no schedule and no quiet hours — the conditions already learn a
        project's normal by hour of the day, which is the same idea and needs nothing
        entered.
      </p>
    </section>
  );
}

/**
 * What stands between the condition about the disk and a reading, said where the
 * switch is rather than left to be discovered.
 */
function Blind({ alerts }: { alerts: HeldAlerts }) {
  const { store } = alerts;

  if (store.blindness === "none") {
    return (
      <p className="quiet">
        That mount last reported <strong>{store.percent} per cent</strong> full.
      </p>
    );
  }

  if (!alerts.switches.fillingUp) {
    // Off and unable to see is not a warning: it is a switch nobody has turned
    // on, and the two selects above already say what it is missing.
    return null;
  }

  const said =
    store.blindness === "noHostNamed"
      ? "This condition is on and cannot see: no machine is named above, so there is no disk to read."
      : store.blindness === "notReporting"
        ? "This condition is on and cannot see: that machine has not reported in the last hour."
        : "This condition is on and cannot see: the mount named above is not among what that machine reports. It was renamed, or taken out of its collector.";

  return <p className="refusal">{said}</p>;
}

/** The guard that keeps the first fortnight of every project quiet. */
function Fortnight({ alerts }: { alerts: HeldAlerts }) {
  const { withoutAFortnight, baselineDays } = alerts.quiet;

  if (withoutAFortnight === 0) {
    return null;
  }

  return (
    <p className="quiet">
      {withoutAFortnight === 1
        ? "One project has"
        : `${withoutAFortnight} projects have`}{" "}
      less than {baselineDays} days of history, so this cannot fire for{" "}
      {withoutAFortnight === 1 ? "it" : "them"} yet, however{" "}
      {withoutAFortnight === 1 ? "it behaves" : "they behave"}.
    </p>
  );
}
