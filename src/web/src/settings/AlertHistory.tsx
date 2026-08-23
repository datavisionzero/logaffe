import { formatTimestamp } from "../shared/time";
import type { AlertCondition, HeldAlerts } from "./alerting";

/** What each condition is called where it is being looked back at. */
const named: Record<AlertCondition, string> = {
  fillingUp: "The store filling up",
  goneQuiet: "Gone quiet",
  flooding: "Delivering far more than it does",
  failing: "Failing far more than it does",
};

/**
 * When each condition last fired, per project and for the machine this
 * installation sits on.
 *
 * **It is the only history there is**, and that is the point of it: alerting
 * fails silently by design, so "is this thing working?" would otherwise be
 * answerable only by waiting for something to go wrong.
 *
 * **It is not an alert list.** An alert leaves the installation and reaches a
 * phone; it does not accumulate on a screen, there is nothing here to mark as
 * read, nothing to acknowledge and nothing to dismiss (`docs/ui.md`). One row per
 * subject per condition, holding the last thing said and nothing before it.
 */
export function AlertHistory({ alerts }: { alerts: HeldAlerts }) {
  return (
    <section>
      <h2>When each condition last fired</h2>

      {alerts.fired.length === 0 ? (
        <p className="quiet">
          Nothing has fired. On an installation with the switches on and nothing wrong,
          that is what this says and it is the ordinary reading — which is also why the
          test notification is worth pressing.
        </p>
      ) : (
        <table className="listing">
          <thead>
            <tr>
              <th scope="col">Project or machine</th>
              <th scope="col">Condition</th>
              <th scope="col">Last fired</th>
            </tr>
          </thead>
          <tbody>
            {alerts.fired.map((fired) => (
              <tr key={`${fired.subjectId}-${fired.condition}`}>
                <th scope="row">{fired.subject}</th>
                <td>{named[fired.condition]}</td>
                <td>
                  <time dateTime={fired.at.toISOString()}>{formatTimestamp(fired.at)}</time>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <p className="quiet">
        A condition that is still holding says nothing more, however long it holds, and
        nothing at all is sent when one clears. So this is when an event started rather
        than when it ended, and there is no second message saying it did.
      </p>
    </section>
  );
}
