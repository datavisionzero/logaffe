/**
 * The filter set, which is the address.
 *
 * Every filter is in the address bar (`docs/ui.md`): a reload comes back to the
 * same view, the back button walks the narrowings just made, and a bookmark is
 * a filter set that costs nothing to keep. This is what stands in for the saved
 * searches `docs/querying.md` refuses — the browser already has a place for
 * named queries and is better at it than a settings screen would be.
 *
 * The parameter names are the contract's, lowercased at the front. One
 * vocabulary for the address and for the request means a filter set copied out
 * of the bar is one that can be handed to `curl`.
 */

/** The six severities, lowest first, because the filter is a threshold. */
export const LEVELS = [
  "Verbose",
  "Debug",
  "Information",
  "Warning",
  "Error",
  "Fatal",
] as const;

export type Level = (typeof LEVELS)[number];

/** The ordinary spans, in the order the control offers them. */
export const ORDINARY_SPANS = ["15m", "1h", "1d", "1w"] as const;

export type OrdinarySpan = (typeof ORDINARY_SPANS)[number];

const MILLISECONDS: Record<OrdinarySpan, number> = {
  "15m": 15 * 60_000,
  "1h": 60 * 60_000,
  "1d": 24 * 60 * 60_000,
  "1w": 7 * 24 * 60 * 60_000,
};

export const SPAN_NAMES: Record<OrdinarySpan, string> = {
  "15m": "Last 15 minutes",
  "1h": "Last hour",
  "1d": "Last day",
  "1w": "Last week",
};

/**
 * The view opens on the last hour.
 *
 * The level opens at everything for a stated reason — an operator who has not
 * yet set a filter should be looking at what actually arrived — and the same
 * reasoning picks the range: the narrowest span would open an ordinary project
 * on an empty list, which is the reading `docs/ui.md` warns about, and the
 * widest is the most expensive read to run unasked. The hour is the span an
 * incident is looked at in.
 */
export const OPENS_AT: OrdinarySpan = "1h";

/** A shorter one is refused rather than run (ADR 0025). */
export const SEARCH_MINIMUM = 3;

export interface Filters {
  /** The span, or `null` when the range is an absolute one. */
  span: OrdinarySpan | null;
  /** ISO instants, and only meaningful while {@link Filters.span} is `null`. */
  from: string | null;
  until: string | null;
  minimumLevel: Level | null;
  instance: string | null;
  loggerName: string | null;
  trace: string | null;
  search: string | null;
  exception: string | null;
}

export const NO_FILTERS: Filters = {
  span: OPENS_AT,
  from: null,
  until: null,
  minimumLevel: null,
  instance: null,
  loggerName: null,
  trace: null,
  search: null,
  exception: null,
};

/**
 * What the address says.
 *
 * An address carrying `from` or `until` is an absolute range; anything else
 * falls back to the span, and a span nobody wrote is the one the view opens on.
 */
export function filtersIn(params: URLSearchParams): Filters {
  const from = params.get("from");
  const until = params.get("until");
  const absolute = from !== null || until !== null;
  const range = params.get("range");

  return {
    span: absolute ? null : isOrdinary(range) ? range : OPENS_AT,
    from,
    until,
    minimumLevel: isLevel(params.get("minimumLevel")) ? (params.get("minimumLevel") as Level) : null,
    instance: params.get("instance"),
    loggerName: params.get("loggerName"),
    trace: params.get("trace"),
    search: params.get("search"),
    exception: params.get("exception"),
  };
}

/**
 * The address a filter set makes.
 *
 * Only what is set is written, so that the plainest view has the plainest
 * address and a narrowing is visible in the bar as the thing it is.
 */
export function addressOf(filters: Filters): string {
  const params = new URLSearchParams();

  if (filters.span === null) {
    if (filters.from !== null) params.set("from", filters.from);
    if (filters.until !== null) params.set("until", filters.until);
  } else if (filters.span !== OPENS_AT) {
    params.set("range", filters.span);
  }

  if (filters.minimumLevel !== null) params.set("minimumLevel", filters.minimumLevel);
  if (filters.instance !== null) params.set("instance", filters.instance);
  if (filters.loggerName !== null) params.set("loggerName", filters.loggerName);
  if (filters.trace !== null) params.set("trace", filters.trace);
  if (filters.search !== null) params.set("search", filters.search);
  if (filters.exception !== null) params.set("exception", filters.exception);

  const written = params.toString();

  return written === "" ? "" : `?${written}`;
}

/**
 * What survives a change of project.
 *
 * The time range and the level threshold are questions about the world — *the
 * last fifteen minutes*, *warnings and worse* — and carrying them over is what
 * makes "the same five minutes in the other service" one click. An instance, a
 * logger name, a trace or a search text belongs to the project it was found in,
 * and carrying it into another one would produce an empty list that looks like
 * an outage.
 */
export function carriedToAnotherProject(filters: Filters): Filters {
  return {
    ...NO_FILTERS,
    span: filters.span,
    from: filters.from,
    until: filters.until,
    minimumLevel: filters.minimumLevel,
  };
}

/**
 * The seven filters as the query surface takes them.
 *
 * A span becomes a `From` computed now, which is what makes it keep growing:
 * the end stays open and the beginning is a distance from the present rather
 * than an instant somebody wrote down.
 */
export function queryOf(filters: Filters, now: Date = new Date()) {
  const range =
    filters.span === null
      ? { From: filters.from ?? undefined, Until: filters.until ?? undefined }
      : {
          From: new Date(now.getTime() - MILLISECONDS[filters.span]).toISOString(),
          Until: undefined,
        };

  return {
    ...range,
    MinimumLevel: filters.minimumLevel ?? undefined,
    Instance: filters.instance ?? undefined,
    LoggerName: filters.loggerName ?? undefined,
    Trace: filters.trace ?? undefined,
    // A text below the minimum is not sent at all: the view says so where it
    // was typed rather than spending a request to be told.
    Search: isLongEnough(filters.search) ? filters.search! : undefined,
    Exception: isLongEnough(filters.exception) ? filters.exception! : undefined,
  };
}

/**
 * Whether the range can still grow, which is what decides the tail.
 *
 * A span is open-ended by construction. An absolute range is history exactly
 * when its end is in the past, and a closed range cannot grow — so the tail
 * never starts on one (`docs/ui.md`).
 */
export function keepsGrowing(filters: Filters, now: Date = new Date()): boolean {
  if (filters.span !== null || filters.until === null) {
    return true;
  }

  return new Date(filters.until).getTime() > now.getTime();
}

export function isLongEnough(text: string | null): boolean {
  return text !== null && text.trim().length >= SEARCH_MINIMUM;
}

/** The narrowings taken from entries, which are the ones shown as chips. */
export function chipsOf(filters: Filters): { of: keyof Filters; name: string; value: string }[] {
  const chips: { of: keyof Filters; name: string; value: string }[] = [];

  if (filters.instance !== null) chips.push({ of: "instance", name: "Instance", value: filters.instance });
  if (filters.loggerName !== null) chips.push({ of: "loggerName", name: "Logger", value: filters.loggerName });
  if (filters.trace !== null) chips.push({ of: "trace", name: "Trace", value: filters.trace });

  return chips;
}

/**
 * Which filters an empty answer is on. Naming them is what keeps an operator
 * from concluding their integration is broken when the range is set to
 * yesterday.
 */
export function namesOfSetFilters(filters: Filters): string[] {
  const set: string[] = [];

  set.push(filters.span === null ? "the time range" : SPAN_NAMES[filters.span].toLowerCase());
  if (filters.minimumLevel !== null) set.push(`${filters.minimumLevel} and above`);
  if (filters.instance !== null) set.push(`instance ${filters.instance}`);
  if (filters.loggerName !== null) set.push(`logger ${filters.loggerName}`);
  if (filters.trace !== null) set.push(`trace ${filters.trace}`);
  if (isLongEnough(filters.search)) set.push(`search “${filters.search}”`);
  if (isLongEnough(filters.exception)) set.push(`exception “${filters.exception}”`);

  return set;
}

function isOrdinary(value: string | null): value is OrdinarySpan {
  return value !== null && (ORDINARY_SPANS as readonly string[]).includes(value);
}

function isLevel(value: string | null): boolean {
  return value !== null && (LEVELS as readonly string[]).includes(value);
}
