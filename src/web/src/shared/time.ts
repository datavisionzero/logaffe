/**
 * Timestamps are shown in the time zone of the browser reading them, absolute
 * and to the millisecond — never relative. "Three minutes ago" is unreadable at
 * the resolution this product works at: the interesting distance between two log
 * entries is regularly under a second, and it is the distance that carries the
 * meaning.
 */

/**
 * ISO 8601 order, deliberately, rather than the reader's locale. A log
 * timestamp has to be unambiguous at a glance, and `03/08` means two different
 * days depending on who is looking at it.
 */
const LOCALE = "sv-SE";

const PARTS: Intl.DateTimeFormatOptions = {
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  fractionalSecondDigits: 3,
  hour12: false,
};

/** The zone the browser is in, named once in the view so no screen is ambiguous. */
export function browserTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

/** What a row and the detail both show. */
export function formatTimestamp(instant: Date, timeZone: string = browserTimeZone()): string {
  return new Intl.DateTimeFormat(LOCALE, { ...PARTS, timeZone }).format(instant);
}

/**
 * When a token or a session was last used, which is written coarsely and is
 * therefore shown coarsely.
 *
 * A use is recorded only when the stored value is absent or more than five
 * minutes old ([ADR 0033]), so the seconds and the milliseconds this module
 * shows everywhere else would be three digits of invention. To the minute is
 * what `docs/ui.md` shows, for exactly that reason: it is accurate enough to say
 * that a token is live or that it has stopped, which is the whole of what these
 * two lists ask of it.
 */
export function formatToTheMinute(
  instant: Date,
  timeZone: string = browserTimeZone(),
): string {
  return new Intl.DateTimeFormat(LOCALE, {
    ...PARTS,
    second: undefined,
    fractionalSecondDigits: undefined,
    timeZone,
  }).format(instant);
}

/**
 * The detail additionally shows the offset, so that an instant copied out of it
 * stands on its own — which is the one comparison against a server console in
 * UTC that the operator ever has to make.
 */
export function formatTimestampWithOffset(
  instant: Date,
  timeZone: string = browserTimeZone(),
): string {
  const formatted = new Intl.DateTimeFormat(LOCALE, {
    ...PARTS,
    timeZone,
    timeZoneName: "longOffset",
  }).format(instant);

  return formatted.replace("GMT", "UTC");
}
