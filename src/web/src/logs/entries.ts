import { useCallback, useEffect, useRef, useState } from "react";
import { api, asInstant, asNumber } from "../api/client";
import { addressOf, keepsGrowing, queryOf, type Filters, type Level } from "./filters";

/** One entry as a line of the list reads it. */
export interface ListedEntry {
  id: number;
  eventTime: Date;
  level: Level;
  loggerName: string | null;
  instance: string | null;
  trace: string | null;
  message: string;
  messageTruncated: boolean;
  hasException: boolean;
}

/**
 * The interval the view polls on, which is also where the five seconds a read
 * gets comes from: a read that takes longer than the refresh has stopped being
 * an interface (ADR 0026).
 */
const INTERVAL = 5_000;

/** How long a newly arrived row stays marked. */
const MARKED = 4_000;

export type Reading =
  | { status: "asking" }
  | { status: "answered" }
  | { status: "expired"; narrow: string[] }
  | { status: "unreachable" };

export interface Entries {
  reading: Reading;
  entries: ListedEntry[];
  /** The cursor for the page below this one, or `null` at the end of the log. */
  older: string | null;
  loadOlder: () => void;
  loadingOlder: boolean;
  /** Arrived while the tail was paused, and shown when it is asked for. */
  waiting: number;
  showWaiting: () => void;
  /** Marked briefly wherever they landed, which may not be at the top. */
  justArrived: ReadonlySet<number>;
  /** The poll filled its cap: the interval is not keeping up with delivery. */
  behind: boolean;
  /** Whether the tail is running at all, which a closed range turns off. */
  following: boolean;
}

/**
 * One project's entries under one filter set, and the tail that keeps them
 * current.
 *
 * The tail is the only request this interface repeats on its own (`docs/ui.md`),
 * and it is a poll rather than anything held open. It follows **receipt time**
 * while the list stays ordered by **event time**, which is what makes an entry
 * delivered late take its place among the entries it belongs with rather than
 * at the top (ADR 0009) — the case an operator watching an outage recover is
 * most likely to be looking for.
 */
export function useEntries(projectId: string, filters: Filters, atTop: boolean): Entries {
  const [reading, setReading] = useState<Reading>({ status: "asking" });
  const [entries, setEntries] = useState<ListedEntry[]>([]);
  const [older, setOlder] = useState<string | null>(null);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [waiting, setWaiting] = useState(0);
  const [justArrived, setJustArrived] = useState<ReadonlySet<number>>(new Set());
  const [behind, setBehind] = useState(false);
  const [visible, setVisible] = useState(() => document.visibilityState !== "hidden");

  // The address is the dependency, because the filter set is a fresh object on
  // every render and only its content decides whether the view changed.
  const address = addressOf(filters);
  const following = keepsGrowing(filters) && visible;

  // Read inside the poll rather than depended on: scrolling would otherwise
  // re-arm the tail on every wheel event and lose the position it was watching.
  const atTopRef = useRef(atTop);
  atTopRef.current = atTop;

  const held = useRef<ListedEntry[]>([]);
  const marking = useRef<number | undefined>(undefined);

  // The position the tail has already seen, kept across a pause rather than in
  // the effect that polls. A hidden tab stops the polling; re-arming on the way
  // back would make what arrived meanwhile the one thing the live view omits,
  // and resuming from here is what a cursor is for.
  const since = useRef<string | undefined>(undefined);

  // It stops when the browser tab is hidden: nothing polls a hidden tab.
  useEffect(() => {
    const watch = () => setVisible(document.visibilityState !== "hidden");

    document.addEventListener("visibilitychange", watch);
    return () => document.removeEventListener("visibilitychange", watch);
  }, []);

  useEffect(() => {
    let current = true;

    setReading({ status: "asking" });
    setEntries([]);
    setOlder(null);
    setWaiting(0);
    setBehind(false);
    held.current = [];
    since.current = undefined;

    void (async () => {
      try {
        const { data, error, response } = await api.GET("/projects/{id}/entries", {
          params: { path: { id: projectId }, query: queryOf(filters) },
        });

        if (!current) {
          return;
        }

        if (data !== undefined) {
          setEntries(data.entries.map(listed));
          setOlder(data.next);
          setReading({ status: "answered" });
          return;
        }

        setReading(refusalOf(response.status, error));
      } catch {
        if (current) {
          setReading({ status: "unreachable" });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, [projectId, address]);

  const arrived = useCallback((fresh: ListedEntry[]) => {
    // The tail follows the top of the list. Scrolling away pauses it, because a
    // list that moves while it is being read is unusable; what arrived while it
    // was paused is counted, and returning to the top shows it.
    if (!atTopRef.current) {
      held.current = merge(held.current, fresh);
      setWaiting(held.current.length);
      return;
    }

    setEntries((held) => merge(held, fresh));
    setJustArrived((marked) => new Set([...marked, ...fresh.map((entry) => entry.id)]));

    window.clearTimeout(marking.current);
    marking.current = window.setTimeout(() => setJustArrived(new Set()), MARKED);
  }, []);

  useEffect(() => {
    if (!following || reading.status !== "answered") {
      return;
    }

    let current = true;
    let timer: number | undefined;

    // The first poll of a view arms the tail: no cursor, no entries, and what
    // comes back is where the project's arrival order currently ends. Every
    // poll after it is a loop over the last answer, so nothing here keeps a
    // position of its own beyond the one it was handed.
    async function poll() {
      try {
        const { data } = await api.GET("/projects/{id}/entries/tail", {
          params: {
            path: { id: projectId },
            query: { ...queryOf(filters), since: since.current },
          },
        });

        if (!current) {
          return;
        }

        if (data !== undefined) {
          since.current = data.next;
          setBehind(data.more);

          if (data.entries.length > 0) {
            arrived(data.entries.map(listed));
          }
        }
      } catch {
        // A poll that did not answer is not an error on the screen. The view is
        // what the last page said, and the next poll is five seconds away.
      }

      if (current) {
        timer = window.setTimeout(() => void poll(), INTERVAL);
      }
    }

    void poll();

    return () => {
      current = false;
      window.clearTimeout(timer);
    };
  }, [projectId, address, following, reading.status, arrived]);

  useEffect(() => () => window.clearTimeout(marking.current), []);

  const loadOlder = useCallback(() => {
    if (older === null || loadingOlder) {
      return;
    }

    setLoadingOlder(true);

    void (async () => {
      try {
        const { data } = await api.GET("/projects/{id}/entries", {
          params: { path: { id: projectId }, query: { ...queryOf(filters), cursor: older } },
        });

        if (data !== undefined) {
          // Appended rather than merged: this page is below the one being read,
          // and paging by cursor means it cannot overlap it.
          setEntries((held) => [...held, ...data.entries.map(listed)]);
          setOlder(data.next);
        }
      } finally {
        setLoadingOlder(false);
      }
    })();
  }, [projectId, address, older, loadingOlder]);

  const showWaiting = useCallback(() => {
    const arrived = held.current;

    held.current = [];
    setWaiting(0);
    setEntries((entries) => merge(entries, arrived));
    setJustArrived(new Set(arrived.map((entry) => entry.id)));

    window.clearTimeout(marking.current);
    marking.current = window.setTimeout(() => setJustArrived(new Set()), MARKED);
  }, []);

  return {
    reading,
    entries,
    older,
    loadOlder,
    loadingOlder,
    waiting,
    showWaiting,
    justArrived,
    behind,
    following,
  };
}

/**
 * Newest first by event time, with the identity breaking ties — the order the
 * query surface answers in and the one the view keeps. New arrivals are placed
 * into it rather than put on top of it.
 */
function merge(into: ListedEntry[], fresh: ListedEntry[]): ListedEntry[] {
  const known = new Set(into.map((entry) => entry.id));
  const unseen = fresh.filter((entry) => !known.has(entry.id));

  if (unseen.length === 0) {
    return into;
  }

  return [...unseen, ...into].sort(
    (a, b) => b.eventTime.getTime() - a.eventTime.getTime() || b.id - a.id,
  );
}

function refusalOf(status: number, error: unknown): Reading {
  // Not a database error, and never a failed request in a corner: the filters
  // are what has to change, and the answer says which ones (ADR 0026).
  if (status === 408) {
    const expired = error as { narrow?: string[] } | undefined;

    return { status: "expired", narrow: expired?.narrow ?? [] };
  }

  return { status: "unreachable" };
}

export function listed(entry: {
  id: number | string;
  eventTime: string;
  level: string;
  loggerName: null | string;
  instance: null | string;
  trace: null | string;
  message: string;
  messageTruncated: boolean;
  hasException: boolean;
}): ListedEntry {
  return {
    id: asNumber(entry.id),
    eventTime: asInstant(entry.eventTime),
    level: entry.level as Level,
    loggerName: entry.loggerName,
    instance: entry.instance,
    trace: entry.trace,
    message: entry.message,
    messageTruncated: entry.messageTruncated,
    hasException: entry.hasException,
  };
}
