import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, asNumber, problemWith } from "../api/client";
import { RETENTION_MAXIMUM, RETENTION_MINIMUM } from "../projects/retention";
import { Footprint, type ReadFootprint } from "./Footprint";

/**
 * What the field is waiting on. Only one of these is a screen the operator has
 * to answer, and it is the one that stands in front of a change that destroys
 * data.
 */
type Asked =
  | { at: "reading" }
  | { at: "settled" }
  | { at: "counting" }
  | { at: "confirming"; days: number; samples: number }
  | { at: "applying" }
  | { at: "unreachable" };

/**
 * How long this installation keeps every host's samples.
 *
 * **It is one number for the installation and not one per host**, because there
 * is no reason to keep one machine's numbers longer than another's
 * (`docs/metrics.md`) — which is also why it is here, on the area that lists
 * the hosts, rather than on any one of them.
 *
 * It is capped at the same year every window here is, for the reason a
 * project's is: a settings box without a ceiling is how a product that is not a
 * multi-year archive becomes one without anyone deciding it should (ADR 0020).
 * And it says what it will cost while it is being chosen, which is what the
 * ceiling used to be doing badly (ADR 0048) — here the arithmetic is the sample
 * tables rather than the entries: a row a minute per machine, and one beside it
 * for each filesystem it reports.
 *
 * **The warning comes before the change, not after it.** A lowering is counted
 * first and applied second, with the number in between — the arrangement a
 * project's retention already has, and for the same reason.
 */
export function SampleRetention() {
  const [held, setHeld] = useState<number>();
  const [days, setDays] = useState("");
  const [asked, setAsked] = useState<Asked>({ at: "reading" });
  const [problem, setProblem] = useState<string>();
  const [changed, setChanged] = useState<number>();

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const { data, response } = await api.GET("/samples/retention");

        if (!current) {
          return;
        }

        if (data !== undefined) {
          setHeld(asNumber(data.retentionDays));
          setDays(String(asNumber(data.retentionDays)));
          setAsked({ at: "settled" });
          return;
        }

        if (response.status !== 401) {
          setAsked({ at: "unreachable" });
        }
      } catch {
        if (current) {
          setAsked({ at: "unreachable" });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, []);

  const wanted = Number(days);

  const readFootprint: ReadFootprint = useCallback(async (windowDays, signal) => {
    const { data } = await api.GET("/samples/retention/footprint", {
      params: { query: { retentionDays: windowDays } },
      signal,
    });

    return data === undefined
      ? undefined
      : {
          retentionDays: asNumber(data.retentionDays),
          heldBytes: asNumber(data.heldBytes),
          impliedBytes: data.impliedBytes === null ? null : asNumber(data.impliedBytes),
          diskFreeBytes: data.diskFreeBytes === null ? null : asNumber(data.diskFreeBytes),
          diskTotalBytes:
            data.diskTotalBytes === null ? null : asNumber(data.diskTotalBytes),
        };
  }, []);

  async function ask(event: FormEvent) {
    event.preventDefault();
    setProblem(undefined);
    setChanged(undefined);

    // Raising it, or asking for the window it already has: nothing leaves and
    // there is nothing to be told first. Raising brings nothing back either —
    // what the sweep has taken is gone.
    if (!Number.isInteger(wanted) || held === undefined || wanted >= held) {
      await apply(wanted);
      return;
    }

    setAsked({ at: "counting" });

    try {
      const { data, response, error } = await api.GET("/samples/retention/outside", {
        params: { query: { retentionDays: wanted } },
      });

      if (data === undefined) {
        setAsked({ at: "settled" });
        setProblem(refusalFor(response.status, error));
        return;
      }

      // The window is echoed back so that an answer arriving after the operator
      // has moved the field on is recognizable as the answer to the question it
      // was, and dropped when it is not.
      if (asNumber(data.retentionDays) !== Number(days)) {
        setAsked({ at: "settled" });
        return;
      }

      const samples = asNumber(data.samples);

      if (samples === 0) {
        await apply(wanted);
        return;
      }

      setAsked({ at: "confirming", days: wanted, samples });
    } catch {
      setAsked({ at: "settled" });
      setProblem("This installation did not answer.");
    }
  }

  async function apply(applying: number) {
    setAsked({ at: "applying" });

    try {
      const { response, error } = await api.PUT("/samples/retention", {
        body: { retentionDays: applying },
      });

      if (response.status === 204) {
        setHeld(applying);
        setChanged(applying);
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
      <h2>Samples are kept for</h2>
      <p>
        One number for this installation, counted from receipt and capped at{" "}
        {RETENTION_MAXIMUM} days. There is no reason to keep one machine's numbers longer
        than another's, so there is nothing to set per host. What the window will cost is
        below, worked out from what the collectors are reporting.
      </p>
      <p>
        <b>Lowering it removes samples</b>, across every host, and you are told how many
        before it takes effect. Raising it again brings nothing back.
      </p>

      {asked.at === "reading" && <p className="quiet">Reading the window…</p>}

      {asked.at === "unreachable" && (
        <p className="refusal">This installation did not answer.</p>
      )}

      {held !== undefined && (
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

          <Footprint read={readFootprint} days={wanted} counting="samples" />

          {changed !== undefined && (
            <p className="quiet">
              Samples are kept for {changed} {changed === 1 ? "day" : "days"} now.
            </p>
          )}

          {asked.at === "confirming" ? (
            <div className="notice">
              <p>
                Lowering this to {asked.days} {asked.days === 1 ? "day" : "days"} puts{" "}
                <b>
                  {asked.samples} {asked.samples === 1 ? "sample" : "samples"}
                </b>{" "}
                outside the window, across every host. The sweep removes them, and raising
                the window again does not bring them back.
              </p>
              <button type="button" onClick={() => void apply(asked.days)}>
                Lower it and remove them
              </button>
              <button
                type="button"
                className="plain"
                onClick={() => setAsked({ at: "settled" })}
              >
                Leave it at {held}
              </button>
            </div>
          ) : (
            <button type="submit" disabled={asked.at !== "settled" || wanted === held}>
              {asked.at === "counting" ? "Counting what this removes…" : "Change the window"}
            </button>
          )}
        </form>
      )}
    </section>
  );
}

function refusalFor(status: number, error: unknown): string {
  return status === 400
    ? (problemWith(error, "retentionDays") ??
        `A retention window is between ${RETENTION_MINIMUM} and ${RETENTION_MAXIMUM} days.`)
    : "This installation refused the change.";
}
