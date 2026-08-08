import { useState, type FormEvent } from "react";
import { api, asNumber, problemWith } from "../api/client";
import type { HeldProject } from "../projects/projects";
import { RETENTION_MAXIMUM, RETENTION_MINIMUM } from "../projects/retention";

/**
 * What the field is waiting on. Only one of these is a screen the operator has
 * to answer, and it is the one that stands in front of a change that destroys
 * data.
 */
type Asked =
  | { at: "settled" }
  | { at: "counting" }
  | { at: "confirming"; days: number; entries: number }
  | { at: "applying" };

/**
 * How long a project keeps its entries, and what lowering it removes.
 *
 * **The warning comes before the change, not after it**: a settings field that
 * silently destroys data is a bad settings field (`docs/projects.md`), so a
 * lowering is counted first and applied second, with the number in between. The
 * count is a route of its own, which is what keeps the change a write with no
 * reading behaviour in it.
 *
 * Raising it is applied straight away. There is nothing to warn about and
 * nothing to bring back — what the sweep has taken is gone — and asking the
 * installation to count the entries outside a wider window would be a query over
 * the largest table in the database to answer *nothing*.
 */
export function RetentionWindow({
  project,
  onChanged,
}: {
  project: HeldProject;
  onChanged: () => void;
}) {
  const [days, setDays] = useState(String(project.retentionDays));
  const [asked, setAsked] = useState<Asked>({ at: "settled" });
  const [problem, setProblem] = useState<string>();
  const [changed, setChanged] = useState<number>();

  const wanted = Number(days);

  async function ask(event: FormEvent) {
    event.preventDefault();
    setProblem(undefined);
    setChanged(undefined);

    // Raising it, or asking for the window it already has: nothing leaves and
    // there is nothing to be told first.
    if (!Number.isInteger(wanted) || wanted >= project.retentionDays) {
      await apply(wanted);
      return;
    }

    setAsked({ at: "counting" });

    try {
      const { data, response, error } = await api.GET("/projects/{id}/retention/outside", {
        params: { path: { id: project.id }, query: { retentionDays: wanted } },
      });

      if (data === undefined) {
        setAsked({ at: "settled" });
        setProblem(refusalFor(response.status, error));
        return;
      }

      // The window is echoed back so that an answer arriving after the operator
      // has moved the field on is recognizable as the answer to the question it
      // was — and this is what recognizes it. A count about a window nobody is
      // asking for any more is dropped rather than shown against the new one.
      if (asNumber(data.retentionDays) !== Number(days)) {
        setAsked({ at: "settled" });
        return;
      }

      const entries = asNumber(data.entries);

      if (entries === 0) {
        await apply(wanted);
        return;
      }

      setAsked({ at: "confirming", days: wanted, entries });
    } catch {
      setAsked({ at: "settled" });
      setProblem("This installation did not answer.");
    }
  }

  async function apply(applying: number) {
    setAsked({ at: "applying" });

    try {
      const { response, error } = await api.PUT("/projects/{id}/retention", {
        params: { path: { id: project.id } },
        body: { retentionDays: applying },
      });

      if (response.status === 204) {
        setChanged(applying);
        onChanged();
        return;
      }

      setProblem(refusalFor(response.status, error));
    } catch {
      setProblem("This installation did not answer.");
    } finally {
      setAsked({ at: "settled" });
    }
  }

  return (
    <section>
      <h2>Kept for</h2>
      <p>
        Counted from receipt time, and time is the only limit there is — no size cap, no
        row quota. The number is yours up to {RETENTION_MAXIMUM} days, which is a ceiling
        no installation can raise.
      </p>
      <p>
        <b>Lowering it removes entries</b>, and you are told how many before it takes
        effect. Raising it again brings nothing back: what the sweep has taken is gone.
      </p>

      <form onSubmit={ask}>
        <label>
          Kept for
          <input
            type="number"
            min={RETENTION_MINIMUM}
            max={RETENTION_MAXIMUM}
            value={days}
            onChange={(e) => {
              setDays(e.target.value);
              setAsked({ at: "settled" });
              setChanged(undefined);
            }}
            aria-invalid={problem !== undefined || undefined}
          />
          days
        </label>
        {problem !== undefined && <p className="refusal">{problem}</p>}

        {changed !== undefined && (
          <p className="quiet">
            Kept for {changed} {changed === 1 ? "day" : "days"} now.
          </p>
        )}

        {asked.at === "confirming" ? (
          <div className="notice">
            <p>
              Lowering this to {asked.days} {asked.days === 1 ? "day" : "days"} puts{" "}
              <b>
                {asked.entries} {asked.entries === 1 ? "entry" : "entries"}
              </b>{" "}
              outside the window. The sweep removes them, and raising the window again
              does not bring them back.
            </p>
            <button type="button" onClick={() => void apply(asked.days)}>
              Lower it and remove them
            </button>
            <button
              type="button"
              className="plain"
              onClick={() => setAsked({ at: "settled" })}
            >
              Leave it at {project.retentionDays}
            </button>
          </div>
        ) : (
          <button
            type="submit"
            disabled={asked.at !== "settled" || wanted === project.retentionDays}
          >
            {asked.at === "counting" ? "Counting what this removes…" : "Change the window"}
          </button>
        )}
      </form>
    </section>
  );
}

function refusalFor(status: number, error: unknown): string {
  if (status === 400) {
    return (
      problemWith(error, "retentionDays") ??
      `A retention window is between ${RETENTION_MINIMUM} and ${RETENTION_MAXIMUM} days.`
    );
  }

  return status === 404
    ? "This project is gone. It may have been deleted from another browser."
    : "This installation refused the change.";
}
