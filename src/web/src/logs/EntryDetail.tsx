import { useEffect, useState, type ReactNode } from "react";
import { api, asNumber } from "../api/client";
import { copyToClipboard, whyNotCopied, type Copying } from "../shared/clipboard";
import { formatTimestampWithOffset } from "../shared/time";
import type { Filters, Level } from "./filters";
import { cn } from "@/lib/utils";
import { Button } from "../components/ui/button";
import { Callout } from "../components/Page";
import { chip } from "./Level";

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
    <aside
      className="max-w-[45vw] flex-[0_0_30rem] overflow-auto rounded-lg border px-3 py-2.5"
      aria-label="Entry"
    >
      <div className="flex justify-between gap-4">
        <Button type="button" variant="link" size="sm" onClick={onClose}>
          Close
        </Button>
        {typeof entry === "object" && (
          <Button
            type="button"
            variant="link"
            size="sm"
            onClick={() => void copyToClipboard(asJson(entry)).then(setCopying)}
          >
            {copying === "copied" ? "Copied" : "Copy as JSON"}
          </Button>
        )}
      </div>

      {whyNotCopied(copying) !== undefined && (
        <p className="refusal text-sm">{whyNotCopied(copying)}</p>
      )}

      {entry === "asking" && <p className="quiet">Reading the entry…</p>}

      {entry === "gone" && (
        <p className="quiet">
          This entry is no longer here. It may have aged out of the project's retention
          window since the page was read.
        </p>
      )}

      {typeof entry === "object" && (
        <dl className="mt-3 grid grid-cols-[7rem_1fr] gap-x-3 gap-y-1.5">
          {/* Both timestamps, each named: the sender's clock and ours
              (ADR 0007). The offset is on them, so an instant copied out of
              here stands on its own. */}
          <Term>Event time</Term>
          <Value>
            <time dateTime={entry.eventTime}>
              {formatTimestampWithOffset(new Date(entry.eventTime))}
            </time>
            <span className="quiet"> — the sender's clock</span>
          </Value>

          <Term>Receipt time</Term>
          <Value>
            <time dateTime={entry.receiptTime}>
              {formatTimestampWithOffset(new Date(entry.receiptTime))}
            </time>
            <span className="quiet"> — ours</span>
          </Value>

          <Term>Level</Term>
          <Value>
            <button
              type="button"
              className={cn(chip(entry.level), "underline decoration-dotted underline-offset-2")}
              onClick={() => onNarrow({ ...filters, minimumLevel: entry.level })}
            >
              {entry.level}
            </button>
          </Value>

          <Term>Logger</Term>
          <Value>
            <Narrowing
              value={entry.loggerName}
              onNarrow={(value) => onNarrow({ ...filters, loggerName: value })}
            />
          </Value>

          <Term>Instance</Term>
          <Value>
            <Narrowing
              value={entry.instance}
              onNarrow={(value) => onNarrow({ ...filters, instance: value })}
            />
          </Value>

          <Term>Trace</Term>
          <Value>
            <Narrowing
              value={entry.trace}
              onNarrow={(value) => onNarrow({ ...filters, trace: value })}
            />
          </Value>

          <Term>Span</Term>
          <Value>
            {entry.span === null ? (
              <span className="quiet">—</span>
            ) : (
              <code className="font-mono text-xs">{entry.span}</code>
            )}
          </Value>

          {/* The message template is not shown. It is stored for fidelity and
              never displayed (ADR 0005): the operator reads the sentence, not
              the shape it was made from. */}
          <Term>Message</Term>
          <Value>
            <p>{entry.message}</p>
            {entry.messageTruncated && (
              <Callout>
                This message was cut at its cap on the way in. What is above is not where
                the sender stopped writing.
              </Callout>
            )}
          </Value>

          {entry.exception !== null && (
            <>
              <Term>Exception</Term>
              <Value>
                <Trace>{entry.exception}</Trace>
                {entry.exceptionTruncated && (
                  <Callout>
                    This exception was cut at its cap. The bottom of the stack trace is not
                    here rather than the exception ending where the text does.
                  </Callout>
                )}
              </Value>
            </>
          )}

          <Term>Properties</Term>
          <Value>
            {entry.properties === null || entry.properties === undefined ? (
              <span className="quiet">None</span>
            ) : (
              // As they were delivered. Nothing here reads inside them and
              // nothing renders them into a sentence (ADR 0012).
              <Trace>{JSON.stringify(entry.properties, null, 2)}</Trace>
            )}
          </Value>
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
    <button
      type="button"
      className="max-w-full overflow-hidden text-ellipsis underline decoration-dotted underline-offset-2"
      onClick={() => onNarrow(value)}
    >
      <code className="font-mono text-xs">{value}</code>
    </button>
  );
}

/** What a field is called, down the left of the pair. */
function Term({ children }: { children: ReactNode }) {
  return <dt className="text-xs text-muted-foreground">{children}</dt>;
}

/** The field itself, which is what the eye comes here for. */
function Value({ children }: { children: ReactNode }) {
  return <dd className="min-w-0">{children}</dd>;
}

/**
 * A stack trace or a bag of properties, as they arrived. Nothing reads inside
 * either of them and nothing renders them into a sentence (ADR 0012), so this
 * is a box with a scrollbar and no cleverness.
 */
function Trace({ children }: { children: ReactNode }) {
  return (
    <pre className="max-h-88 overflow-auto rounded-md border bg-muted p-2 font-mono text-xs break-words whitespace-pre-wrap">
      {children}
    </pre>
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
