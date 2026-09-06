import { useVirtualizer, type Virtualizer } from "@tanstack/react-virtual";
import type { RefObject } from "react";
import { formatTimestamp } from "../shared/time";
import type { ListedEntry } from "./entries";
import { cn } from "@/lib/utils";
import { Level } from "./Level";

/**
 * The height of one row, in pixels: the line, the padding around it, and the
 * rule under it.
 *
 * A number rather than a measurement, because the row is one line that never
 * wraps: every entry in this list is exactly as tall as every other, and the
 * cheapest correct answer to *where does row nine thousand start* is
 * multiplication. It is also the height each row is drawn at — the window hands
 * it back to them — so what the arithmetic assumes and what the screen does are
 * the same number, and the row's own line height is stated (`text-…/5`) rather
 * than inherited so that it stays that way.
 */
const ROW = 25;

/**
 * How many rows outside the viewport are kept in the DOM on each side.
 *
 * Enough that a held arrow key and a flick of the wheel land on something that
 * is already there, and few enough that the DOM stays a screenful.
 */
const OVERSCAN = 12;

/**
 * The window over the entries, which the view that owns the scroller opens.
 *
 * It lives outside `EntryList` because the keyboard is the other thing that
 * moves this list: a row the arrow keys walked to may not be in the DOM at all,
 * and `scrollToIndex` — not `scrollIntoView` on an element that is not there —
 * is what brings it in.
 */
export function useEntryRows(
  entries: ListedEntry[],
  scroller: RefObject<HTMLDivElement | null>,
): Virtualizer<HTMLDivElement, Element> {
  return useVirtualizer({
    count: entries.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => ROW,
    overscan: OVERSCAN,
    // The identity of a row rather than its position: the tail inserts entries
    // into the middle of this list, and an index would move every row below the
    // insertion onto a different key.
    getItemKey: (index) => entries[index]?.id ?? index,
  });
}

/**
 * The entries a filter set leaves, newest first by event time.
 *
 * One entry is one row, one line, never wrapping. Reading a log is scanning,
 * and a list whose rows change height cannot be scanned: one four-line stack
 * trace in the middle of the page destroys the rhythm that makes the other
 * forty rows readable. The message that does not fit is one keystroke away in
 * the detail.
 *
 * Only the rows in front of the operator are in the DOM. A log is the one list
 * of this product that really does grow — a page is a thousand entries and the
 * tail keeps adding to it — and a thousand rows of five columns each is where
 * scrolling stops being smooth. The list keeps its full height either way, so
 * the scrollbar describes the entries that are loaded and not the ones that
 * happen to be rendered.
 */
export function EntryList({
  entries,
  rows,
  selected,
  onSelect,
  justArrived,
  onNarrowToLogger,
}: {
  entries: ListedEntry[];
  rows: Virtualizer<HTMLDivElement, Element>;
  selected: number | null;
  onSelect: (id: number) => void;
  justArrived: ReadonlySet<number>;
  onNarrowToLogger: (loggerName: string) => void;
}) {
  return (
    <ul
      className="relative min-w-0"
      style={{ height: rows.getTotalSize() }}
      role="listbox"
      aria-label="Entries"
    >
      {rows.getVirtualItems().map((row) => {
        const entry = entries[row.index]!;

        return (
          <EntryLine
            key={row.key}
            entry={entry}
            top={row.start}
            height={row.size}
            selected={entry.id === selected}
            justArrived={justArrived.has(entry.id)}
            onSelect={() => onSelect(entry.id)}
            onNarrowToLogger={onNarrowToLogger}
          />
        );
      })}
    </ul>
  );
}

function EntryLine({
  entry,
  top,
  height,
  selected,
  justArrived,
  onSelect,
  onNarrowToLogger,
}: {
  entry: ListedEntry;
  top: number;
  height: number;
  selected: boolean;
  justArrived: boolean;
  onSelect: () => void;
  onNarrowToLogger: (loggerName: string) => void;
}) {
  return (
    <li
      role="option"
      aria-selected={selected}
      // Placed rather than stacked: the row is lifted out of the flow onto the
      // offset the window gives it, which is what lets the list be as tall as
      // every entry while holding only the ones being read.
      style={{ transform: `translateY(${top}px)`, height }}
      className={cn(
        "absolute inset-x-0 top-0 grid cursor-default grid-cols-[13.5rem_6.5rem_12rem_1fr_auto] items-baseline gap-2.5 overflow-hidden whitespace-nowrap border-b border-border/50 px-2 py-0.5 font-mono text-[0.82rem]/5",
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
