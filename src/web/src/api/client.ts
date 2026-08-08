import createClient from "openapi-fetch";
import type { paths } from "./schema";

/**
 * The one way this application reaches the installation.
 *
 * The types it is built on are generated from `docs/api/openapi.json` before
 * every build, which is what makes that document load-bearing rather than
 * descriptive (`docs/codebase.md`): a route that changed shape stops compiling
 * here rather than failing on a screen. Nothing hand-writes a URL or a body.
 *
 * The installation it reaches is the one that served this page. In development
 * Vite forwards what belongs to the backend, so that stays true there too —
 * which is also what lets the session cookie through, `SameSite=Strict` being
 * a rule about one origin.
 */
export const api = createClient<paths>({
  baseUrl: window.location.origin,

  // Reached through `globalThis` when a request is made rather than captured
  // when this module loads. That is what lets a test stand an installation in
  // front of the generated client rather than in place of it — the bodies a
  // screen sends and reads still go through the types the contract produced.
  fetch: (request) => globalThis.fetch(request),
});

/**
 * The session ended somewhere other than the screen the operator is on: it
 * expired, it was revoked from another browser, or the password changed under
 * it. Every request answers `401` from that moment, and the application has one
 * place to notice rather than one per call.
 */
type SignedOutListener = () => void;

let signedOutListener: SignedOutListener | undefined;

export function whenSignedOut(listener: SignedOutListener): () => void {
  signedOutListener = listener;

  return () => {
    if (signedOutListener === listener) {
      signedOutListener = undefined;
    }
  };
}

/**
 * The two surfaces a stranger can reach answer `401` for a wrong password
 * rather than for a session that ended, and turning that into "you have been
 * signed out" would be answering a question nobody asked.
 */
function isPublic(url: string): boolean {
  const { pathname } = new URL(url, "http://installation.invalid");

  return pathname === "/sign-in" || pathname.startsWith("/claim");
}

api.use({
  onResponse({ request, response }) {
    if (response.status === 401 && !isPublic(request.url)) {
      signedOutListener?.();
    }

    return undefined;
  },
});

/**
 * ASP.NET's OpenAPI document types an `int32` as an integer *or* a string, so
 * the generated types carry `number | string` wherever the contract carries a
 * number. Reading one back through here keeps that shape in the API layer
 * instead of letting it out into every screen that shows a count.
 */
export function asNumber(value: number | string): number {
  return typeof value === "number" ? value : Number(value);
}

/** An instant off the wire, as a `Date` the formatters take. */
export function asInstant(value: string): Date {
  return new Date(value);
}

/**
 * The field messages of a `400`. Endpoints taking something an operator typed
 * name the box that is wrong (`docs/setup.md`), and a form that swallowed that
 * into "something went wrong" would be throwing away the only useful part.
 */
export type FieldProblems = Record<string, string[]>;

export function problemsIn(error: unknown): FieldProblems {
  if (typeof error !== "object" || error === null || !("errors" in error)) {
    return {};
  }

  const { errors } = error as { errors?: unknown };

  return typeof errors === "object" && errors !== null ? (errors as FieldProblems) : {};
}

/** The first thing said about one field, which is all a form has room for. */
export function problemWith(error: unknown, field: string): string | undefined {
  return problemsIn(error)[field]?.[0];
}
