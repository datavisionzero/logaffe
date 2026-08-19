import { SampleBand } from "../hosts/SampleBand";
import { useSamples, useTheMinute } from "../hosts/samples";
import { ReadExpired } from "./CountPanel";
import { keepsGrowing, queryOf, type Filters } from "./filters";

const AN_HOUR = 60 * 60_000;

/**
 * What the machine was doing, above the entries that were written on it.
 *
 * **This is the feature** (`docs/metrics.md`): everything behind it exists so
 * that an operator looking at four minutes of errors sees, without leaving the
 * screen or opening a second tool, that memory went to the ceiling three
 * minutes before the first one.
 *
 * It is drawn for the host the open project sits on, over **exactly the range
 * the filters already state**. It moves when the range moves, and it is absent
 * for a project that sits on no host — which is every project until the
 * operator says otherwise, and costs that project nothing else.
 *
 * **The band and the entries are drawn against different clocks**, and that is
 * accepted rather than fixed: the entries are ordered by event time, which is
 * the sender's, and a sample carries one clock and it is the installation's
 * (`docs/metrics.md`). Where the two machines disagree the band is offset by
 * that much, and trusting a third clock to settle it is how the wrong one
 * becomes load-bearing.
 */
export function HostBand({ hostId, filters }: { hostId: string | null; filters: Filters }) {
  // A span is open-ended and keeps growing, so the end of the band is the
  // present and the present advances. An absolute range with an end in the past
  // is history and is asked for once, and a project on no host is not asked at
  // all.
  const minute = useTheMinute(hostId !== null && keepsGrowing(filters));

  const range = queryOf(filters, minute);
  const to = range.Until === undefined ? minute : new Date(range.Until);

  // A range with an end and no beginning is *everything up to then*, which is a
  // sensible thing to ask a log and not a thing a band can draw — a machine has
  // reported since the day it was made. The hour before that end is what is
  // drawn, which is the span the view itself opens on.
  const from =
    range.From === undefined ? new Date(to.getTime() - AN_HOUR) : new Date(range.From);

  // The minute rides along, because a range that is fixed at both ends can
  // still be one the machine has not finished reporting into — an absolute
  // range ending later today is the same question with a new answer.
  const samples = useSamples(hostId, from, to, minute.getTime());

  if (hostId === null) {
    return null;
  }

  return (
    <div className="band-holder">
      {samples.status === "asking" && <p className="quiet">Reading the machine…</p>}

      {samples.status === "unreachable" && (
        <p className="refusal">This installation did not answer for the machine.</p>
      )}

      {/* The host the project sits on was deleted from another browser. The
          entries are untouched by that and stay on the screen: a project on no
          host loses the band and nothing else. */}
      {samples.status === "gone" && (
        <p className="quiet">
          The host this project sat on is gone. It may have been deleted from another
          browser.
        </p>
      )}

      {samples.status === "expired" && <ReadExpired narrow={samples.narrow} />}

      {samples.status === "held" && <SampleBand window={samples.window} from={from} to={to} />}
    </div>
  );
}
