import { useCallback, useEffect, useState } from "react";
import { api, asInstant, asNumber } from "../api/client";
import type { components } from "../api/schema";

/** Why the condition about the disk cannot be evaluated, and `none` when it can. */
export type Blindness = components["schemas"]["Blindness"];

/** One of the three things this installation will say something about unasked. */
export type AlertCondition = components["schemas"]["AlertCondition"];

/** How the notification the operator asked for ended. */
export type NotifierProof = components["schemas"]["NotifierProof"];

/** One project and how long its silence has to last before anything is said. */
export interface ToleratedSilence {
  projectId: string;
  name: string;
  toleratedHours: number;
}

/**
 * Everything the alerts area shows, with the contract's numbers and instants
 * already turned into what a screen states.
 */
export interface HeldAlerts {
  /** The one place notifications go, and `null` on an installation with none. */
  notifier: { server: string; topic: string; hasAccessToken: boolean } | null;
  switches: { fillingUp: boolean; goneQuiet: boolean; flooding: boolean };
  store: {
    blindness: Blindness;
    hostId: string | null;
    hostName: string | null;
    mount: string | null;
    /** `null` when there is no reading to say it with. */
    percent: number | null;
    firstThreshold: number;
    secondThreshold: number;
  };
  quiet: {
    /** The project noticed soonest, and `null` when none can fire this yet. */
    busiest: ToleratedSilence | null;
    quietest: ToleratedSilence | null;
    withoutAFortnight: number;
    multiple: number;
    leastToleratedHours: number;
    baselineDays: number;
  };
  flood: { multiple: number; floor: number; baselineDays: number };
  /** When each condition last fired, which is the only history there is. */
  fired: { subjectId: string; subject: string; condition: AlertCondition; at: Date }[];
}

export type AlertsState =
  | { status: "asking" }
  | { status: "held"; alerts: HeldAlerts }
  | { status: "unreachable" };

/**
 * The alerts area, asked for by the area itself.
 *
 * **One read for the whole of it**, because the parts are one sentence on the
 * screen: the switch, what it currently works out to for this installation's own
 * projects, and whether it can see anything at all. Three requests would put
 * them on the screen in a different order every time.
 *
 * The access token is not in it. It is a secret and it is asked for by pressing
 * something, exactly as an ingest token is (ADR 0022).
 */
export function useAlerts(): { state: AlertsState; reload: () => void } {
  const [state, setState] = useState<AlertsState>({ status: "asking" });
  const [asked, setAsked] = useState(0);

  const reload = useCallback(() => setAsked((n) => n + 1), []);

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const { data, response } = await api.GET("/alerts");

        if (!current) {
          return;
        }

        if (data !== undefined) {
          setState({ status: "held", alerts: held(data) });
          return;
        }

        // A `401` is the session ending, and the sign-in screen is already on
        // its way in front of everything.
        if (response.status !== 401) {
          setState({ status: "unreachable" });
        }
      } catch {
        if (current) {
          setState({ status: "unreachable" });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, [asked]);

  return { state, reload };
}

function held(alerts: components["schemas"]["AlertSettingsResponse"]): HeldAlerts {
  return {
    notifier: alerts.notifier,
    switches: alerts.switches,
    store: {
      blindness: alerts.store.blindness,
      hostId: alerts.store.hostId,
      hostName: alerts.store.hostName,
      mount: alerts.store.mount,
      percent: alerts.store.percent === null ? null : asNumber(alerts.store.percent),
      firstThreshold: asNumber(alerts.store.firstThreshold),
      secondThreshold: asNumber(alerts.store.secondThreshold),
    },
    quiet: {
      busiest: tolerance(alerts.quiet.busiest),
      quietest: tolerance(alerts.quiet.quietest),
      withoutAFortnight: asNumber(alerts.quiet.withoutAFortnight),
      multiple: asNumber(alerts.quiet.multiple),
      leastToleratedHours: asNumber(alerts.quiet.leastToleratedHours),
      baselineDays: asNumber(alerts.quiet.baselineDays),
    },
    flood: {
      multiple: asNumber(alerts.flood.multiple),
      floor: asNumber(alerts.flood.floor),
      baselineDays: asNumber(alerts.flood.baselineDays),
    },
    fired: alerts.fired.map((fired) => ({
      subjectId: fired.subjectId,
      subject: fired.subject,
      condition: fired.condition,
      at: asInstant(fired.at),
    })),
  };
}

function tolerance(
  held: components["schemas"]["ToleratedSilenceResponse"] | null,
): ToleratedSilence | null {
  return held === null
    ? null
    : {
        projectId: held.projectId,
        name: held.name,
        toleratedHours: asNumber(held.toleratedHours),
      };
}

/** Hours as a screen says them, which is a number and the word. */
export function hours(count: number): string {
  return count === 1 ? "one hour" : `${count} hours`;
}
