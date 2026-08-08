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

  return { asked };
}

/** A claimed installation, which is what most screens are reached from. */
export const claimed = { body: { isClaimed: true, windowIsOpen: false, closesAt: null } };

/** One nobody owns, with the window still open. */
export function unclaimed(closesAt: string | null = null) {
  return { body: { isClaimed: false, windowIsOpen: true, closesAt } };
}

/** One nobody owns and nobody can: the thirty minutes are up. */
export const lapsed = {
  body: { isClaimed: false, windowIsOpen: false, closesAt: null },
};

/** A row of the project list, with everything the contract requires on it. */
export function aProject(project: {
  id: string;
  name: string;
  retentionDays?: number;
  createdAt?: string;
  ingestTokens?: number;
  lastReceivedAt?: string | null;
}) {
  return {
    id: project.id,
    name: project.name,
    retentionDays: project.retentionDays ?? 30,
    createdAt: project.createdAt ?? "2026-08-01T09:00:00.000Z",
    ingestTokens: project.ingestTokens ?? 1,
    lastReceivedAt: project.lastReceivedAt ?? null,
  };
}
