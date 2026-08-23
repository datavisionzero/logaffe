import { AlertConditions } from "./AlertConditions";
import { AlertHistory } from "./AlertHistory";
import { AlertNotifier } from "./AlertNotifier";
import { useAlerts } from "./alerting";

/**
 * What this installation says unasked, and where it says it.
 *
 * Everything else in this product waits to be asked. Three things do not fit
 * under that and they have one property in common: **the whole point of them is
 * that the operator does not know** — the store is filling up, an application
 * has stopped delivering, or a project is suddenly writing far more than it does
 * (`docs/alerts.md`).
 *
 * The area is deliberately small furniture: a notifier, three switches, and one
 * checkbox per project that lives on that project's own screen. **There is no
 * notification bell and no alert list**, here or anywhere else in this
 * interface: an alert leaves the installation and reaches a phone, and a copy
 * waiting on a screen would be a second inbox nobody asked for.
 */
export function Alerts() {
  const { state, reload } = useAlerts();

  if (state.status === "asking") {
    return <p className="quiet">Reading the alerts…</p>;
  }

  if (state.status === "unreachable") {
    return <p className="refusal">This installation did not answer.</p>;
  }

  return (
    <>
      <AlertNotifier alerts={state.alerts} onChanged={reload} />
      <AlertConditions alerts={state.alerts} onChanged={reload} />
      <AlertHistory alerts={state.alerts} />
    </>
  );
}
