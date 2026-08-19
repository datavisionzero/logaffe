import { useEffect, useState } from "react";
import { api, asNumber } from "../api/client";
import { copyToClipboard, whyNotCopied, type Copying } from "../shared/clipboard";
import { formatTimestampWithOffset } from "../shared/time";
import type { Filters, Level } from "./filters";

interface WholeEntry {
  id: number;
  eventTime: string;
  receiptTime: string;
  level: Level;
  loggerName: string | null;
  instance: string | null;
  trace: string | null;
  span: string | null;
  message: string;
  exception: string | null;
  properties: unknown;
  messageTruncated: boolean;
  exceptionTruncated: boolean;
}

/**
 * One entry in full, beside the list.
 *
 * It opens without navigating anywhere: the list keeps its position and the
 * filters stay set. Two actions live here — every field that is a filter
 * narrows to its value, of which the trace is the valuable one because it turns
 * one line into the sequence of entries the request it belonged to produced,
 * and the entry copies as JSON in one action.
 */
export function EntryDetail({
  projectId,
  entryId,
  filters,
  onNarrow,
  onClose,
}: {
  projectId: string;
  entryId: number;
  filters: Filters;
  onNarrow: (filters: Filters) => void;
  onClose: () => void;
}) {
  const [entry, setEntry] = useState<WholeEntry | "asking" | "gone">("asking");
  const [copying, setCopying] = useState<Copying>();

  useEffect(() => {
    let current = true;

    setEntry("asking");
    setCopying(undefined);

    void (async () => {
      try {
        const { data } = await api.GET("/projects/{id}/entries/{entryId}", {
          params: { path: { id: projectId, entryId } },
        });

        if (current) {
          // An entry that aged out between the page and the click looks like
          // this, and it is not a failure worth a red box.
          setEntry(data === undefined ? "gone" : whole(data));
        }
      } catch {
        if (current) {
          setEntry("gone");
        }
      }
    })();

    return () => {
      current = false;
    };
  }, [projectId, entryId]);

  return (
    <aside className="detail" aria-label="Entry">
      <div className="detail-head">
        <button type="button" className="plain" onClick={onClose}>
          Close
        </button>
        {typeof entry === "object" && (
          <button
            type="button"
            className="plain"
            onClick={() => void copyToClipboard(asJson(entry)).then(setCopying)}
          >
            {copying === "copied" ? "Copied" : "Copy as JSON"}
          </button>
        )}
      </div>

      {whyNotCopied(copying) !== undefined && (
        <p className="refusal">{whyNotCopied(copying)}</p>
      )}

      {entry === "asking" && <p className="quiet">Reading the entry…</p>}

      {entry === "gone" && (
        <p className="quiet">
          This entry is no longer here. It may have aged out of the project's retention
          window since the page was read.
        </p>
      )}

      {typeof entry === "object" && (
        <dl className="detail-fields">
          {/* Both timestamps, each named: the sender's clock and ours
              (ADR 0007). The offset is on them, so an instant copied out of
              here stands on its own. */}
          <dt>Event time</dt>
          <dd>
            <time dateTime={entry.eventTime}>
              {formatTimestampWithOffset(new Date(entry.eventTime))}
            </time>
            <span className="quiet"> — the sender's clock</span>
          </dd>

          <dt>Receipt time</dt>
          <dd>
            <time dateTime={entry.receiptTime}>
              {formatTimestampWithOffset(new Date(entry.receiptTime))}
            </time>
            <span className="quiet"> — ours</span>
          </dd>

          <dt>Level</dt>
          <dd>
            <button
              type="button"
              className={`level level-${entry.level} narrows`}
              onClick={() => onNarrow({ ...filters, minimumLevel: entry.level })}
            >
              {entry.level}
            </button>
          </dd>

          <dt>Logger</dt>
          <dd>
            <Narrowing
              value={entry.loggerName}
              onNarrow={(value) => onNarrow({ ...filters, loggerName: value })}
            />
          </dd>

          <dt>Instance</dt>
          <dd>
            <Narrowing
              value={entry.instance}
              onNarrow={(value) => onNarrow({ ...filters, instance: value })}
            />
          </dd>

          <dt>Trace</dt>
          <dd>
            <Narrowing
              value={entry.trace}
              onNarrow={(value) => onNarrow({ ...filters, trace: value })}
            />
          </dd>

          <dt>Span</dt>
          <dd>{entry.span === null ? <span className="quiet">—</span> : <code>{entry.span}</code>}</dd>

          {/* The message template is not shown. It is stored for fidelity and
              never displayed (ADR 0005): the operator reads the sentence, not
              the shape it was made from. */}
          <dt>Message</dt>
          <dd>
            <p className="detail-message">{entry.message}</p>
            {entry.messageTruncated && (
              <p className="notice">
                This message was cut at its cap on the way in. What is above is not where
                the sender stopped writing.
              </p>
            )}
          </dd>

          {entry.exception !== null && (
            <>
              <dt>Exception</dt>
              <dd>
                <pre>{entry.exception}</pre>
                {entry.exceptionTruncated && (
                  <p className="notice">
                    This exception was cut at its cap. The bottom of the stack trace is not
                    here rather than the exception ending where the text does.
                  </p>
                )}
              </dd>
            </>
          )}

          <dt>Properties</dt>
          <dd>
            {entry.properties === null || entry.properties === undefined ? (
              <span className="quiet">None</span>
            ) : (
              // As they were delivered. Nothing here reads inside them and
              // nothing renders them into a sentence (ADR 0012).
              <pre>{JSON.stringify(entry.properties, null, 2)}</pre>
            )}
          </dd>
        </dl>
      )}
    </aside>
  );
}

/** A field that is a filter, which is one click away from being one. */
function Narrowing({
  value,
  onNarrow,
}: {
  value: string | null;
  onNarrow: (value: string) => void;
}) {
  if (value === null) {
    return <span className="quiet">—</span>;
  }

  return (
    <button type="button" className="plain narrows" onClick={() => onNarrow(value)}>
      <code>{value}</code>
    </button>
  );
}

/**
 * What the entry copies as.
 *
 * The message template is left out for the same reason the screen does not show
 * it (ADR 0005): pasted into an issue it would be displayed, and the operator
 * hands over the sentence rather than the shape it was made from.
 */
function asJson(entry: WholeEntry): string {
  return JSON.stringify(entry, null, 2);
}

function whole(entry: {
  id: number | string;
  eventTime: string;
  receiptTime: string;
  level: string;
  loggerName: null | string;
  instance: null | string;
  trace: null | string;
  span: null | string;
  message: string;
  exception: null | string;
  properties?: unknown;
  messageTruncated: boolean;
  exceptionTruncated: boolean;
}): WholeEntry {
  return {
    id: asNumber(entry.id),
    eventTime: entry.eventTime,
    receiptTime: entry.receiptTime,
    level: entry.level as Level,
    loggerName: entry.loggerName,
    instance: entry.instance,
    trace: entry.trace,
    span: entry.span,
    message: entry.message,
    exception: entry.exception,
    properties: entry.properties ?? null,
    messageTruncated: entry.messageTruncated,
    exceptionTruncated: entry.exceptionTruncated,
  };
}
