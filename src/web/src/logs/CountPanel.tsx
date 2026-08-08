import { useEffect, useState } from "react";
import { api, asNumber } from "../api/client";
import { formatTimestamp } from "../shared/time";
import { addressOf, queryOf, type Filters, type Level } from "./filters";

/** The groupings a count takes. The trace is not among them, deliberately. */
const GROUPINGS = ["None", "Level", "LoggerName", "Instance", "Time"] as const;

type Grouping = (typeof GROUPINGS)[number];

const GROUPING_NAMES: Record<Grouping, string> = {
  None: "Ungrouped",
  Level: "By level",
  LoggerName: "By logger",
  Instance: "By instance",
  Time: "Over time",
};

/** Three, aligned to the clock rather than to the range asked for. */
const BUCKETS = ["Minute", "Hour", "Day"] as const;

type Bucket = (typeof BUCKETS)[number];

const BUCKET_MILLISECONDS: Record<Bucket, number> = {
  Minute: 60_000,
  Hour: 60 * 60_000,
  Day: 24 * 60 * 60_000,
};

interface Group {
  value: string | null;
  entries: number;
}

type Counting =
  | { status: "counting" }
  | { status: "counted"; groups: Group[] }
  | { status: "expired"; narrow: string[] }
  | { status: "unreachable" };

/**
 * The count, which is asked for.
 *
 * It is a button beside the filters and not a number that accompanies the page:
 * computed once because somebody asked for it, rather than maintained because a
 * screen wanted it. There is deliberately no histogram sitting above the list,
 * which would be this same grouped count run on every view open, over the
 * largest table in the database, to draw a shape the operator did not request.
 *
 * Every row narrows to itself — the closest thing this product has to a facet.
 */
export function CountPanel({
  projectId,
  filters,
  onNarrow,
  onClose,
}: {
  projectId: string;
  filters: Filters;
  onNarrow: (filters: Filters) => void;
  onClose: () => void;
}) {
  const [grouping, setGrouping] = useState<Grouping>("None");
  const [bucket, setBucket] = useState<Bucket>("Hour");
  const [counting, setCounting] = useState<Counting>({ status: "counting" });

  // The address rather than the object: a filter set is built fresh on every
  // render, and only its content decides whether the question changed.
  const address = addressOf(filters);

  useEffect(() => {
    let current = true;

    setCounting({ status: "counting" });

    void (async () => {
      try {
        const { data, error, response } = await api.GET("/projects/{id}/entries/count", {
          params: {
            path: { id: projectId },
            query: {
              ...queryOf(filters),
              groupBy: grouping === "None" ? undefined : grouping,
              bucket: grouping === "Time" ? bucket : undefined,
            },
          },
        });

        if (!current) {
          return;
        }

        if (data !== undefined) {
          setCounting({
            status: "counted",
            groups: data.groups.map((group) => ({
              value: group.value,
              entries: asNumber(group.entries),
            })),
          });
          return;
        }

        if (response.status === 408) {
          const expired = error as { narrow?: string[] } | undefined;

          setCounting({ status: "expired", narrow: expired?.narrow ?? [] });
          return;
        }

        setCounting({ status: "unreachable" });
      } catch {
        if (current) {
          setCounting({ status: "unreachable" });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, [projectId, address, grouping, bucket]);

  return (
    <section className="count" aria-label="Count">
      <div className="count-head">
        <label>
          <span className="visually-hidden">Grouping</span>
          <select value={grouping} onChange={(e) => setGrouping(e.target.value as Grouping)}>
            {GROUPINGS.map((one) => (
              <option key={one} value={one}>
                {GROUPING_NAMES[one]}
              </option>
            ))}
          </select>
        </label>

        {grouping === "Time" && (
          <label>
            <span className="visually-hidden">Bucket</span>
            <select value={bucket} onChange={(e) => setBucket(e.target.value as Bucket)}>
              {BUCKETS.map((one) => (
                <option key={one} value={one}>
                  Per {one.toLowerCase()}
                </option>
              ))}
            </select>
          </label>
        )}

        <button type="button" className="plain" onClick={onClose}>
          Close
        </button>
      </div>

      {counting.status === "counting" && <p className="quiet">Counting…</p>}

      {counting.status === "unreachable" && (
        <p className="refusal">This installation did not answer the count.</p>
      )}

      {counting.status === "expired" && <ReadExpired narrow={counting.narrow} />}

      {counting.status === "counted" && (
        <table className="count-table">
          <tbody>
            {counting.groups.map((group) => (
              <tr key={group.value ?? "all"}>
                <td>
                  {grouping === "None" || group.value === null ? (
                    <span className="quiet">{grouping === "None" ? "Matching" : "No value"}</span>
                  ) : (
                    <button
                      type="button"
                      className="plain narrows"
                      onClick={() => onNarrow(narrowedTo(filters, grouping, bucket, group.value!))}
                    >
                      {grouping === "Time" ? formatTimestamp(new Date(group.value)) : group.value}
                    </button>
                  )}
                </td>
                <td className="count-number">{group.entries}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

/**
 * A read that used up its five seconds, in the terms of the filters that are
 * set. It is never a database error and never a failed request in a corner.
 */
export function ReadExpired({ narrow }: { narrow: string[] }) {
  return (
    <div className="expired">
      <p>This read took longer than the five seconds it gets. The filters are unchanged.</p>
      <ul>
        {narrow.map((one) => (
          <li key={one}>{SENTENCES[one] ?? one}</li>
        ))}
      </ul>
    </div>
  );
}

/**
 * The narrowings the query surface names, as the sentences a screen writes.
 * The endpoint hands back values rather than prose so that the agent gets the
 * fact and the operator's screen writes this (ADR 0012).
 */
const SENTENCES: Record<string, string> = {
  TimeRange: "Set a time range — this one ran with an open end.",
  SmallerTimeRange: "Make the time range a shorter one.",
  ExceptionText: "Take the exception filter off; no index serves it.",
};

/** Every row narrows to itself, which is what makes this the product's facet. */
function narrowedTo(
  filters: Filters,
  grouping: Grouping,
  bucket: Bucket,
  value: string,
): Filters {
  switch (grouping) {
    case "Level":
      // The level filter is a threshold and there is no other one, so narrowing
      // to a row is asking for that level and above.
      return { ...filters, minimumLevel: value as Level };
    case "LoggerName":
      return { ...filters, loggerName: value };
    case "Instance":
      return { ...filters, instance: value };
    case "Time": {
      const from = new Date(value);
      const until = new Date(from.getTime() + BUCKET_MILLISECONDS[bucket]);

      // A bucket is a closed range, so this is also the narrowing that turns
      // the tail off — which is right: a bucket in the past cannot grow.
      return { ...filters, span: null, from: from.toISOString(), until: until.toISOString() };
    }
    default:
      return filters;
  }
}
