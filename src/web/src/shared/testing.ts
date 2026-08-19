import { vi } from "vitest";

/** One answer an installation gives, as a screen would receive it. */
export interface Answer {
  status?: number;
  body?: unknown;
}

/** `"GET /projects"` — the method and the path, which is what a route is here. */
type Route = string;

/**
 * An installation that answers from a table.
 *
 * The seam is `fetch` rather than anything of the API layer's, because the
 * generated client is the thing under test as much as the screens are: a body
 * that stopped matching the contract has to fail here rather than be waved
 * through by a substitute that agrees with whatever it is handed.
 *
 * A route given several answers hands them out in order and then repeats the
 * last, which is how a list read again after an act is written.
 */
export function anInstallationAnswering(routes: Record<Route, Answer | Answer[]>) {
  const asked: Route[] = [];
  const sent = new Map<Route, unknown[]>();

  const queued = new Map<Route, Answer[]>(
    Object.entries(routes).map(([route, answer]) => [
      route,
      Array.isArray(answer) ? [...answer] : [answer],
    ]),
  );

  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const request = new Request(input, init);
      const { pathname } = new URL(request.url, "http://installation.invalid");
      const route = `${request.method} ${pathname}`;

      asked.push(route);

      // Kept for the acts whose body is the point rather than the call: what a
      // screen actually sent, parsed, in the order it sent it.
      const text = await request.text();
      if (text !== "") {
        sent.set(route, [...(sent.get(route) ?? []), JSON.parse(text)]);
      }

      const answers = queued.get(route);

      if (answers === undefined || answers.length === 0) {
        throw new Error(`Nothing here answers ${route}.`);
      }

      const answer = answers.length === 1 ? answers[0]! : answers.shift()!;
      const body = answer.body === undefined ? null : JSON.stringify(answer.body);

      return new Response(body, {
        status: answer.status ?? (body === null ? 204 : 200),
        headers: body === null ? undefined : { "Content-Type": "application/json" },
      });
    }),
  );

  return { asked, sentTo: (route: Route): unknown[] => sent.get(route) ?? [] };
}

/** A claimed installation, which is what most screens are reached from. */
export const claimed = {
  body: { isClaimed: true, canBeClaimed: false, needsSecret: false, closesAt: null },
};

/** One nobody owns, guarded by a claim secret, which is the default (ADR 0040). */
export function unclaimed(closesAt: string | null = null) {
  return {
    body: { isClaimed: false, canBeClaimed: true, needsSecret: true, closesAt },
  };
}

/** One nobody owns, guarded by an open window instead. */
export function unclaimedInWindowMode(closesAt: string | null = null) {
  return {
    body: { isClaimed: false, canBeClaimed: true, needsSecret: false, closesAt },
  };
}

/** One nobody owns and nobody can: the thirty minutes are up. */
export const lapsed = {
  body: { isClaimed: false, canBeClaimed: false, needsSecret: false, closesAt: null },
};

/** An operator who has enrolled a second factor, which the shell asks about. */
export const withSecondFactor = {
  body: { isEnrolled: true, enrolledAt: "2026-08-16T09:00:00Z" },
};

/** One who has not, which is the state the banner exists for (ADR 0041). */
export const withoutSecondFactor = { body: { isEnrolled: false, enrolledAt: null } };

/** A row of the project list, with everything the contract requires on it. */
export function aProject(project: {
  id: string;
  name: string;
  groupId?: string | null;
  hostId?: string | null;
  retentionDays?: number;
  createdAt?: string;
  ingestTokens?: number;
  lastReceivedAt?: string | null;
}) {
  return {
    id: project.id,
    name: project.name,
    groupId: project.groupId ?? null,
    hostId: project.hostId ?? null,
    retentionDays: project.retentionDays ?? 30,
    createdAt: project.createdAt ?? "2026-08-01T09:00:00.000Z",
    ingestTokens: project.ingestTokens ?? 1,
    lastReceivedAt: project.lastReceivedAt ?? null,
  };
}

/**
 * A row of the group list, which every signed-in screen reads once.
 *
 * It carries no count of its projects: how many a group holds is a fact about
 * the projects, and the screens count it off the project list they already have
 * (ADR 0039).
 */
export function aGroup(group: { id: string; name: string; createdAt?: string }) {
  return {
    id: group.id,
    name: group.name,
    createdAt: group.createdAt ?? "2026-08-01T09:00:00.000Z",
  };
}

/**
 * An installation holding no groups, which is what most screens are read
 * against: the feature is invisible until an operator makes one.
 */
export const noGroups: Answer = { body: [] };

/**
 * A row of the host list.
 *
 * When it last reported is on it because that is read off its newest sample
 * rather than written beside it (ADR 0039), and a host that never has is an
 * ordinary state rather than a fault (`docs/metrics.md`) — so the default here
 * is `null`.
 */
export function aHost(host: {
  id: string;
  name: string;
  createdAt?: string;
  hostTokens?: number;
  lastReportedAt?: string | null;
  projects?: number;
}) {
  return {
    id: host.id,
    name: host.name,
    createdAt: host.createdAt ?? "2026-08-01T09:00:00.000Z",
    hostTokens: host.hostTokens ?? 1,
    lastReportedAt: host.lastReportedAt ?? null,
    projects: host.projects ?? 0,
  };
}

/** An installation holding no hosts, which is what every project starts on. */
export const noHosts: Answer = { body: [] };

/** One span of a read, with the peak beside the average. */
export function aSampleBucket(bucket: {
  start: string;
  cpuAverage?: number;
  cpuPeak?: number;
  memoryUsedAverage?: number;
  memoryUsedPeak?: number;
  memoryTotal?: number;
  loadAverage?: number;
  loadPeak?: number;
}) {
  return {
    start: bucket.start,
    cpuAverage: bucket.cpuAverage ?? 0.42,
    cpuPeak: bucket.cpuPeak ?? bucket.cpuAverage ?? 0.42,
    memoryUsedAverage: bucket.memoryUsedAverage ?? 6115295232,
    memoryUsedPeak: bucket.memoryUsedPeak ?? bucket.memoryUsedAverage ?? 6115295232,
    memoryTotal: bucket.memoryTotal ?? 16769712128,
    loadAverage: bucket.loadAverage ?? 0.52,
    loadPeak: bucket.loadPeak ?? bucket.loadAverage ?? 0.52,
  };
}

/**
 * What a host reported over a range.
 *
 * The span is on it because nothing asks for one: a caller names a range and
 * the installation says how it divided it, which is what lets a band tell a run
 * from a gap.
 */
export function aSampleWindow(window: {
  hostName?: string;
  bucketSeconds?: number;
  samples?: ReturnType<typeof aSampleBucket>[];
  filesystems?: {
    start: string;
    mount: string;
    usedAverage: number;
    usedPeak: number;
    total: number;
  }[];
}): Answer {
  return {
    body: {
      hostName: window.hostName ?? "web-01",
      bucketSeconds: window.bucketSeconds ?? 60,
      samples: window.samples ?? [],
      filesystems: window.filesystems ?? [],
    },
  };
}
