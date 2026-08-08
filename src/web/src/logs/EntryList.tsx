import { formatTimestamp } from "../shared/time";
import type { ListedEntry } from "./entries";

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
    <ul className="entries" role="listbox" aria-label="Entries">
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
      className={`entry${selected ? " selected" : ""}${justArrived ? " just-arrived" : ""}`}
      onClick={onSelect}
    >
      <time className="entry-time" dateTime={entry.eventTime.toISOString()}>
        {formatTimestamp(entry.eventTime)}
      </time>

      {/* The level as a word with a colour behind it, and never as a colour
          alone. */}
      <span className={`level level-${entry.level}`}>{entry.level}</span>

      <span className="entry-logger">
        {entry.loggerName === null ? (
          <span className="quiet">—</span>
        ) : (
          <button
            type="button"
            className="plain"
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

      <span className="entry-message">{entry.message}</span>

      <span className="entry-marks">
        {entry.hasException && (
          <span className="mark mark-exception" title="Carries an exception">
            ⚠
          </span>
        )}
        {entry.messageTruncated && (
          <span className="mark mark-truncated" title="Truncated on the way in">
            ✂
          </span>
        )}
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
