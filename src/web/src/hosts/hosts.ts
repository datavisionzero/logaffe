import { useCallback, useEffect, useState } from "react";
import { api, asInstant, asNumber } from "../api/client";

/** One host as the settings area and a project's host field both read it. */
export interface HeldHost {
  id: string;
  name: string;
  createdAt: Date;
  /** One ordinarily, two while its collector is being moved over. */
  hostTokens: number;
  /**
   * When it last reported, read off its newest sample and `null` for a host
   * that never has — which is an ordinary state, not an error
   * (`docs/metrics.md`).
   */
  lastReportedAt: Date | null;
  /** How many projects say they run on it, which is what deleting one names. */
  projects: number;
}

export type HostsState =
  | { status: "asking" }
  | { status: "held"; hosts: HeldHost[] }
  | { status: "unreachable" };

/**
 * The hosts, asked for by the screen that is showing them.
 *
 * **This is not a provider beside the projects and the groups**, and the
 * difference is what the answer costs. A group is a name and the list is three
 * rows; a host carries when it last reported, which is read off the sample
 * table for every host at once. Two screens want it — the settings area and the
 * host field on a project — and both of them are opened deliberately, so the
 * request belongs to them rather than to every sign-in (`docs/ui.md`).
 *
 * The band over a project's entries is not one of them: it reads one host's
 * samples, and the name it draws rides along with that read.
 */
export function useHosts(): { state: HostsState; reload: () => void } {
  const [state, setState] = useState<HostsState>({ status: "asking" });
  const [asked, setAsked] = useState(0);

  const reload = useCallback(() => setAsked((n) => n + 1), []);

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const { data, response } = await api.GET("/hosts");

        if (!current) {
          return;
        }

        if (data !== undefined) {
          setState({ status: "held", hosts: data.map(held) });
          return;
        }

        // A `401` is the session having ended, and the sign-in screen is
        // already on its way in front of everything.
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

/** The hosts in the order they are listed, which is the order of their names. */
export function byName(hosts: HeldHost[]): HeldHost[] {
  return [...hosts].sort((one, other) => one.name.localeCompare(other.name));
}

function held(host: {
  id: string;
  name: string;
  createdAt: string;
  hostTokens: number | string;
  lastReportedAt: string | null;
  projects: number | string;
}): HeldHost {
  return {
    id: host.id,
    name: host.name,
    createdAt: asInstant(host.createdAt),
    hostTokens: asNumber(host.hostTokens),
    lastReportedAt: host.lastReportedAt === null ? null : asInstant(host.lastReportedAt),
    projects: asNumber(host.projects),
  };
}
