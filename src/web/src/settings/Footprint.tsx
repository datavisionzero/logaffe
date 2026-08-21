import { useEffect, useState } from "react";
import { formatBytes } from "../hosts/readings";
import { RETENTION_MAXIMUM, RETENTION_MINIMUM } from "../projects/retention";

/**
 * What a window costs, as the installation answers it.
 *
 * Two of the three are absent on installations that cannot answer them, and
 * absent is a state rather than a failure — the field shows the numbers it has
 * rather than refusing to render.
 */
export interface WindowFootprint {
  retentionDays: number;
  heldBytes: number;
  impliedBytes: number | null;
  diskFreeBytes: number | null;
  diskTotalBytes: number | null;
}

/**
 * Reading it. Each screen brings its own, because the two routes are two routes
 * and the generated client is typed per path.
 */
export type ReadFootprint = (
  days: number,
  signal: AbortSignal,
) => Promise<WindowFootprint | undefined>;

/**
 * How long the operator has to stop typing before the installation is asked.
 * Long enough that `365` is one question rather than three, short enough that
 * the answer is there before they have moved to the button.
 */
const SETTLES_IN = 250;

/**
 * What the window in the field will cost, beside the field.
 *
 * **This is what the ceiling used to do**
 * ([ADR 0048](docs/adr/0048-retentions-ceiling-is-a-year-and-the-setting-says-what-it-costs.md)).
 * Ninety days was never a bound on anything the product pays for — it permitted
 * one noisy project ninety gibibytes and refused a quiet one a year that costs
 * two — so the ceiling is a year and the field states the cost instead.
 *
 * **It refuses nothing.** There is no threshold here, no colour that means too
 * much and no button that stops working: the operator sees three numbers and
 * decides. What the field will not do is stay silent about them.
 *
 * **It follows the field rather than the change**, so a window that is being
 * considered costs what it says before anything is applied — and a number about
 * a window the operator has since moved on from is dropped rather than shown
 * against the new one, which is the arrangement the count of what a lowering
 * removes already has.
 */
export function Footprint({
  read,
  days,
  counting,
}: {
  read: ReadFootprint;
  days: number;
  counting: "entries" | "samples";
}) {
  const [footprint, setFootprint] = useState<WindowFootprint>();
  const [unreachable, setUnreachable] = useState(false);

  const asked =
    Number.isInteger(days) && days >= RETENTION_MINIMUM && days <= RETENTION_MAXIMUM
      ? days
      : undefined;

  useEffect(() => {
    if (asked === undefined) {
      return;
    }

    const aborting = new AbortController();
    const settling = setTimeout(() => {
      void (async () => {
        try {
          const answer = await read(asked, aborting.signal);

          if (aborting.signal.aborted) {
            return;
          }

          // The window is echoed back, so an answer about one the operator has
          // moved on from is recognizable as the answer to the question it was.
          if (answer === undefined || answer.retentionDays !== asked) {
            setUnreachable(answer === undefined);
            return;
          }

          setFootprint(answer);
          setUnreachable(false);
        } catch {
          if (!aborting.signal.aborted) {
            setUnreachable(true);
          }
        }
      })();
    }, SETTLES_IN);

    return () => {
      clearTimeout(settling);
      aborting.abort();
    };
  }, [asked, read]);

  if (unreachable && footprint === undefined) {
    // One line and no alarm: what this says is worth having and nothing here
    // depends on it, so a screen that cannot get it says so and moves on.
    return <p className="quiet">This installation did not say what it holds.</p>;
  }

  if (footprint === undefined) {
    return <p className="quiet">Working out what this costs…</p>;
  }

  const { heldBytes, impliedBytes, diskFreeBytes, diskTotalBytes } = footprint;

  return (
    <dl className="footprint">
      <div>
        <dt>Held now</dt>
        <dd>{formatBytes(heldBytes)}</dd>
      </div>

      <div>
        <dt>{footprint.retentionDays} days would hold</dt>
        <dd>
          {impliedBytes === null ? (
            <span className="quiet">
              {counting === "entries"
                ? "not yet — this project has less than a fortnight of history behind it"
                : "not yet — no machine has reported to this installation"}
            </span>
          ) : (
            formatBytes(impliedBytes)
          )}
        </dd>
      </div>

      {diskFreeBytes !== null && diskTotalBytes !== null && (
        <div>
          <dt>Free on the disk</dt>
          <dd>
            {formatBytes(diskFreeBytes)} of {formatBytes(diskTotalBytes)}
          </dd>
        </div>
      )}
    </dl>
  );
}
