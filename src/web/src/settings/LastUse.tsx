import { formatToTheMinute } from "../shared/time";

/**
 * When a credential was last used, which is the field both token lists and the
 * session list are read for.
 *
 * It is shown **to the minute** and never finer, because that is how accurately
 * it is recorded: a use writes the timestamp only when the stored one is absent
 * or more than five minutes old
 * ([ADR 0033](docs/adr/0033-the-last-use-of-a-token-is-written-coarsely.md)).
 * Everywhere else in this interface a timestamp is to the millisecond, and
 * showing this one that way would be three digits the installation never had.
 *
 * **Never used is its own answer.** A token issued and never deployed is not a
 * token that has gone quiet, and the first use always writes — so the null is
 * the difference between a rotation nobody has started and one that is finished.
 */
export function LastUse({ at }: { at: Date | null }) {
  return at === null ? (
    <span className="quiet">Never used</span>
  ) : (
    <time dateTime={at.toISOString()}>{formatToTheMinute(at)}</time>
  );
}
