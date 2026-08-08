import { useEffect, useState, type FormEvent } from "react";
import {
  chipsOf,
  isLongEnough,
  LEVELS,
  ORDINARY_SPANS,
  SEARCH_MINIMUM,
  SPAN_NAMES,
  type Filters,
  type Level,
  type OrdinarySpan,
} from "./filters";

/**
 * The controls, in the order an operator reaches for them.
 *
 * Time range, level threshold, search text, then the narrowings taken from
 * entries as chips, and finally the exception filter in a box of its own. Every
 * narrowing happens in front of the list it narrows: there is no separate
 * search page and no advanced mode.
 */
export function FilterBar({
  filters,
  onChange,
  counting,
  onCount,
  searchRef,
}: {
  filters: Filters;
  onChange: (filters: Filters) => void;
  counting: boolean;
  onCount: () => void;
  searchRef: React.RefObject<HTMLInputElement | null>;
}) {
  return (
    <div className="filters">
      <div className="filter-row">
        <TimeRange filters={filters} onChange={onChange} />
        <LevelThreshold filters={filters} onChange={onChange} />
        <SearchText filters={filters} onChange={onChange} boxRef={searchRef} />

        <button type="button" onClick={onCount} aria-pressed={counting}>
          Count
        </button>
      </div>

      <Chips filters={filters} onChange={onChange} />

      <ExceptionText filters={filters} onChange={onChange} />
    </div>
  );
}

/**
 * The ordinary spans and an absolute from-and-to.
 *
 * A span is open-ended and keeps growing, which is the live case; an absolute
 * range with an end in the past is history and cannot grow, which is what turns
 * the tail off. The same browser zone that shows a timestamp interprets one
 * typed here.
 */
function TimeRange({
  filters,
  onChange,
}: {
  filters: Filters;
  onChange: (filters: Filters) => void;
}) {
  const absolute = filters.span === null;

  return (
    <span className="filter">
      <label>
        <span className="visually-hidden">Time range</span>
        <select
          value={filters.span ?? "absolute"}
          onChange={(event) =>
            onChange(
              event.target.value === "absolute"
                ? { ...filters, span: null, from: nowMinusAnHour(), until: null }
                : { ...filters, span: event.target.value as OrdinarySpan, from: null, until: null },
            )
          }
        >
          {ORDINARY_SPANS.map((span) => (
            <option key={span} value={span}>
              {SPAN_NAMES[span]}
            </option>
          ))}
          <option value="absolute">From and to…</option>
        </select>
      </label>

      {absolute && (
        <>
          <label>
            <span className="visually-hidden">From</span>
            <input
              type="datetime-local"
              step="1"
              value={asLocalInput(filters.from)}
              onChange={(event) =>
                onChange({ ...filters, from: asInstantText(event.target.value) })
              }
            />
          </label>
          <label>
            <span className="visually-hidden">To</span>
            <input
              type="datetime-local"
              step="1"
              value={asLocalInput(filters.until)}
              onChange={(event) =>
                onChange({ ...filters, until: asInstantText(event.target.value) })
              }
            />
          </label>
        </>
      )}
    </span>
  );
}

/**
 * One control with six positions rather than six checkboxes, opening at
 * everything.
 *
 * A view that hides `Information` by default is a view that shows nothing
 * happened when something did, and an operator who has not yet set a filter
 * should be looking at what actually arrived.
 */
function LevelThreshold({
  filters,
  onChange,
}: {
  filters: Filters;
  onChange: (filters: Filters) => void;
}) {
  return (
    <label className="filter">
      <span className="visually-hidden">Level</span>
      <select
        value={filters.minimumLevel ?? ""}
        onChange={(event) =>
          onChange({
            ...filters,
            minimumLevel: event.target.value === "" ? null : (event.target.value as Level),
          })
        }
      >
        <option value="">Verbose and above</option>
        {LEVELS.slice(1).map((level) => (
          <option key={level} value={level}>
            {level} and above
          </option>
        ))}
      </select>
    </label>
  );
}

/**
 * Grep over the rendered message, and the box `/` puts the cursor in.
 *
 * It is applied when it is submitted rather than as it is typed: a substring
 * search over the largest table in the database is not a thing to run on every
 * keystroke, and the address only changes when the question does.
 */
function SearchText({
  filters,
  onChange,
  boxRef,
}: {
  filters: Filters;
  onChange: (filters: Filters) => void;
  boxRef: React.RefObject<HTMLInputElement | null>;
}) {
  const [typed, setTyped] = useState(filters.search ?? "");

  useEffect(() => setTyped(filters.search ?? ""), [filters.search]);

  const tooShort = typed.trim().length > 0 && !isLongEnough(typed);

  function apply(event: FormEvent) {
    event.preventDefault();

    if (!tooShort) {
      onChange({ ...filters, search: typed.trim() === "" ? null : typed.trim() });
    }
  }

  return (
    <form className="filter" onSubmit={apply}>
      <label>
        <span className="visually-hidden">Search the message</span>
        <input
          ref={boxRef}
          type="search"
          placeholder="Search the message"
          value={typed}
          onChange={(event) => setTyped(event.target.value)}
          aria-invalid={tooShort || undefined}
        />
      </label>
      {tooShort && (
        <span className="refusal">A search text is at least {SEARCH_MINIMUM} characters.</span>
      )}
    </form>
  );
}

/**
 * The narrowings taken from entries, removed one at a time.
 *
 * There is no dropdown listing every logger a project has seen (ADR 0029):
 * these arrive by clicking the value on a line that is already on the screen,
 * and they leave here.
 */
function Chips({
  filters,
  onChange,
}: {
  filters: Filters;
  onChange: (filters: Filters) => void;
}) {
  const chips = chipsOf(filters);

  if (chips.length === 0) {
    return null;
  }

  return (
    <div className="chips">
      {chips.map((chip) => (
        <span key={chip.of} className="chip">
          {chip.name} <code>{chip.value}</code>
          <button
            type="button"
            aria-label={`Remove the ${chip.name.toLowerCase()} filter`}
            onClick={() => onChange({ ...filters, [chip.of]: null })}
          >
            ×
          </button>
        </span>
      ))}
    </div>
  );
}

/**
 * The one filter that can be slow, in a box of its own.
 *
 * It is visibly separate rather than a mode of the search box because the two
 * match different fields and the operator has to know which one finds
 * `nullreference` (ADR 0028). A stack trace is kilobytes where a message is a
 * line, so no index serves this one — deliberately, so that every ordinary
 * search does not pay for the rare one.
 */
function ExceptionText({
  filters,
  onChange,
}: {
  filters: Filters;
  onChange: (filters: Filters) => void;
}) {
  const [typed, setTyped] = useState(filters.exception ?? "");

  useEffect(() => setTyped(filters.exception ?? ""), [filters.exception]);

  const tooShort = typed.trim().length > 0 && !isLongEnough(typed);

  function apply(event: FormEvent) {
    event.preventDefault();

    if (!tooShort) {
      onChange({ ...filters, exception: typed.trim() === "" ? null : typed.trim() });
    }
  }

  return (
    <form className="exception-filter" onSubmit={apply}>
      <label>
        Exception
        <input
          type="search"
          placeholder="nullreference"
          value={typed}
          onChange={(event) => setTyped(event.target.value)}
          aria-invalid={tooShort || undefined}
        />
      </label>
      <span className="quiet">
        Searches the exception, not the message — and it is the one filter that can be slow.
      </span>
      {tooShort && (
        <span className="refusal">An exception text is at least {SEARCH_MINIMUM} characters.</span>
      )}
    </form>
  );
}

/** What the absolute range opens on, so that the two boxes are never empty. */
function nowMinusAnHour(): string {
  return new Date(Date.now() - 60 * 60_000).toISOString();
}

/**
 * An instant as `datetime-local` wants it, which is local wall-clock text with
 * no zone on it — the browser's zone, which is the one every timestamp in this
 * view is in.
 */
function asLocalInput(instant: string | null): string {
  if (instant === null) {
    return "";
  }

  const at = new Date(instant);
  const pad = (value: number) => String(value).padStart(2, "0");

  return (
    `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}` +
    `T${pad(at.getHours())}:${pad(at.getMinutes())}:${pad(at.getSeconds())}`
  );
}

function asInstantText(local: string): string | null {
  if (local === "") {
    return null;
  }

  const at = new Date(local);

  return Number.isNaN(at.getTime()) ? null : at.toISOString();
}
