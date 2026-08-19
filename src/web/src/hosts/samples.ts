import { useEffect, useState } from "react";
import { api, asInstant, asNumber } from "../api/client";

/** One span of a read, with the peak beside the average. */
export interface HeldSampleBucket {
  start: Date;
  cpuAverage: number;
  cpuPeak: number;
  memoryUsedAverage: number;
  memoryUsedPeak: number;
  memoryTotal: number;
  loadAverage: number;
  loadPeak: number;
}

/** One span of one of the host's filesystems. */
export interface HeldFilesystemBucket {
  start: Date;
  mount: string;
  usedAverage: number;
  usedPeak: number;
  total: number;
}

export interface HeldWindow {
  /** Which machine this is, which the read hands back rather than the project. */
  hostName: string;
  /**
   * How long one span is. Nothing chooses it — a caller names a range and the
   * installation divides it — so it comes back on the answer, and it is what
   * lets the band tell a run the host reported in from a gap it did not.
   */
  bucketSeconds: number;
  samples: HeldSampleBucket[];
  filesystems: HeldFilesystemBucket[];
}

export type SamplesState =
  | { status: "asking" }
  | { status: "held"; window: HeldWindow }
  | { status: "expired"; narrow: string[] }
  | { status: "gone" }
  | { status: "unreachable" };

/**
 * What one host reported over one range.
 *
 * **The range is given rather than chosen here.** Over the entries it is
 * exactly the range the filters state, and on a host's own screen it is the
 * plain one that screen offers — the band configures nothing, picks nothing and
 * saves nothing (`docs/metrics.md`).
 *
 * **Asking again is the caller's too.** A range whose end advances is a new
 * range and asks on its own; a fixed range that has not finished yet — an
 * absolute one ending later today — is the same question with a new answer, and
 * `since` is what says to put it again. Both are the caller advancing the same
 * minute.
 */
export function useSamples(
  hostId: string | null,
  from: Date,
  to: Date,
  since: number = 0,
): SamplesState {
  const [state, setState] = useState<SamplesState>({ status: "asking" });

  // The instants rather than the `Date` objects: a range is built fresh on
  // every render and only the moment it names decides whether to ask again.
  const start = from.toISOString();
  const end = to.toISOString();

  useEffect(() => {
    if (hostId === null) {
      return;
    }

    let current = true;

    void (async () => {
      try {
        const { data, error, response } = await api.GET("/hosts/{id}/samples", {
          params: { path: { id: hostId }, query: { from: start, to: end } },
        });

        if (!current) {
          return;
        }

        if (data !== undefined) {
          setState({ status: "held", window: held(data) });
          return;
        }

        if (response.status === 408) {
          const expired = error as { narrow?: string[] } | undefined;

          setState({ status: "expired", narrow: expired?.narrow ?? [] });
          return;
        }

        // A host that is not there is one deleted from another browser, which
        // is a different sentence from an installation that did not answer.
        if (response.status === 404) {
          setState({ status: "gone" });
          return;
        }

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
  }, [hostId, start, end, since]);

  return state;
}

/**
 * The interval a sample is taken on (`docs/metrics.md`), which is the rate at
 * which there is anything new to draw.
 */
export const SAMPLE_INTERVAL = 60_000;

/**
 * The present, to the minute, for a view whose range keeps growing.
 *
 * **The entries poll every five seconds and this does not, and that is the same
 * rule rather than an exception to it.** The view being read asks for what
 * changed; a sample changes once a minute, and a band redrawn twelve times per
 * reading would be eleven requests for a picture that did not move. Rounding
 * down to the minute is what makes it one request rather than a fetch on every
 * render — the range only names a new moment when there is a new one to name.
 *
 * A closed range is not advanced at all, and neither is a hidden tab: the rule
 * the tail already follows (`docs/ui.md`).
 */
export function useTheMinute(advancing: boolean): Date {
  const [minute, setMinute] = useState(() => theMinute());
  const [visible, setVisible] = useState(() => document.visibilityState !== "hidden");

  useEffect(() => {
    function changed() {
      setVisible(document.visibilityState !== "hidden");
    }

    document.addEventListener("visibilitychange", changed);
    return () => document.removeEventListener("visibilitychange", changed);
  }, []);

  useEffect(() => {
    if (!advancing || !visible) {
      return;
    }

    // Caught up on the way back in, so that a tab returned to after an hour
    // draws the hour rather than what it was showing when it was hidden.
    setMinute(theMinute());

    const timer = setInterval(() => setMinute(theMinute()), SAMPLE_INTERVAL);

    return () => clearInterval(timer);
  }, [advancing, visible]);

  return minute;
}

function theMinute(): Date {
  return new Date(Math.floor(Date.now() / SAMPLE_INTERVAL) * SAMPLE_INTERVAL);
}

function held(window: {
  hostName: string;
  bucketSeconds: number | string;
  samples: {
    start: string;
    cpuAverage: number | string;
    cpuPeak: number | string;
    memoryUsedAverage: number | string;
    memoryUsedPeak: number | string;
    memoryTotal: number | string;
    loadAverage: number | string;
    loadPeak: number | string;
  }[];
  filesystems: {
    start: string;
    mount: string;
    usedAverage: number | string;
    usedPeak: number | string;
    total: number | string;
  }[];
}): HeldWindow {
  return {
    hostName: window.hostName,
    bucketSeconds: asNumber(window.bucketSeconds),
    samples: window.samples.map((bucket) => ({
      start: asInstant(bucket.start),
      cpuAverage: asNumber(bucket.cpuAverage),
      cpuPeak: asNumber(bucket.cpuPeak),
      memoryUsedAverage: asNumber(bucket.memoryUsedAverage),
      memoryUsedPeak: asNumber(bucket.memoryUsedPeak),
      memoryTotal: asNumber(bucket.memoryTotal),
      loadAverage: asNumber(bucket.loadAverage),
      loadPeak: asNumber(bucket.loadPeak),
    })),
    filesystems: window.filesystems.map((bucket) => ({
      start: asInstant(bucket.start),
      mount: bucket.mount,
      usedAverage: asNumber(bucket.usedAverage),
      usedPeak: asNumber(bucket.usedPeak),
      total: asNumber(bucket.total),
    })),
  };
}
