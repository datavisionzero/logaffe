import { formatTimestamp } from "../shared/time";
import type { ListedEntry } from "./entries";
import { cn } from "@/lib/utils";
import { Level } from "./Level";

/**
 * The entries a filter set leaves, newest first by event time.
 *
 * One entry is one row, one line, never wrapping. Reading a log is scanning,
 * and a list whose rows change height cannot be scanned: one four-line stack
 * trace in the middle of the page destroys the rhythm that makes the other
 * forty rows readable. The message that does not fit is one keystroke away in
 * the detail.
 */
export function EntryList({
  entries,
  selected,
  onSelect,
  justArrived,
  onNarrowToLogger,
}: {
  entries: ListedEntry[];
  selected: number | null;
  onSelect: (id: number) => void;
  justArrived: ReadonlySet<number>;
  onNarrowToLogger: (loggerName: string) => void;
}) {
  return (
    <ul className="min-w-0" role="listbox" aria-label="Entries">
      {entries.map((entry) => (
        <EntryLine
          key={entry.id}
          entry={entry}
          selected={entry.id === selected}
          justArrived={justArrived.has(entry.id)}
          onSelect={() => onSelect(entry.id)}
          onNarrowToLogger={onNarrowToLogger}
        />
      ))}
    </ul>
  );
}

function EntryLine({
  entry,
  selected,
  justArrived,
  onSelect,
  onNarrowToLogger,
}: {
  entry: ListedEntry;
  selected: boolean;
  justArrived: boolean;
  onSelect: () => void;
  onNarrowToLogger: (loggerName: string) => void;
}) {
  return (
    <li
      role="option"
      aria-selected={selected}
      data-id={entry.id}
      className={cn(
        "grid cursor-default grid-cols-[13.5rem_6.5rem_12rem_1fr_auto] items-baseline gap-2.5 whitespace-nowrap border-b border-border/50 px-2 py-0.5 font-mono text-[0.82rem]",
        selected && "bg-muted outline-1 outline-border",
        // Marked briefly wherever they land, so that something appearing out of
        // eyeline is still something the operator sees appear.
        justArrived && "animate-[just-arrived_4s_ease-out]",
      )}
      onClick={onSelect}
    >
      <time className="text-muted-foreground" dateTime={entry.eventTime.toISOString()}>
        {formatTimestamp(entry.eventTime)}
      </time>

      <Level level={entry.level} />

      <span className="overflow-hidden text-ellipsis">
        {entry.loggerName === null ? (
          <span className="quiet">—</span>
        ) : (
          <button
            type="button"
            className="max-w-full overflow-hidden text-ellipsis underline decoration-dotted underline-offset-2"
            title={entry.loggerName}
            onClick={(event) => {
              event.stopPropagation();
              onNarrowToLogger(entry.loggerName!);
            }}
          >
            {shortened(entry.loggerName)}
          </button>
        )}
      </span>

      {/* Named for what it is rather than by a class: this is the one thing in
          the interface a test reaches for by selector, and a hook that is not a
          style survives the next time the row is redrawn. */}
      <span data-slot="message" className="overflow-hidden text-ellipsis">
        {entry.message}
      </span>

      {/* What the entry says about itself, and never about the application:
          these are the level-warning colour and not the accent. */}
      <span className="text-level-warning">
        {entry.hasException && <span title="Carries an exception">⚠</span>}
        {entry.messageTruncated && <span title="Truncated on the way in">✂</span>}
      </span>
    </li>
  );
}

/**
 * The logger name shortened to its last segments.
 *
 * `Logaffe.Api.Http.EntryEndpoints` is read as `Http.EntryEndpoints`: the
 * segments that tell two loggers apart are at the end, and the ones that repeat
 * on every row are at the front. The whole of it is in the title and in the
 * detail.
 */
export function shortened(loggerName: string): string {
  const segments = loggerName.split(".");

  return segments.length <= 2 ? loggerName : segments.slice(-2).join(".");
}
