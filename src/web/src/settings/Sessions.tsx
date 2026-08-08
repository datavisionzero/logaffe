import { useCallback, useEffect, useState } from "react";
import { api, asInstant } from "../api/client";
import { formatTimestamp } from "../shared/time";
import { LastUse } from "./LastUse";

interface HeldSession {
  id: string;
  /** Where it last acted from, or `unknown` where there was none to read. */
  lastSeenFrom: string;
  startedAt: Date;
  lastUsedAt: Date;
  expiresAt: Date;
  /** Whether this is the browser reading the list. */
  isCurrent: boolean;
}

type Listing =
  | { status: "asking" }
  | { status: "held"; sessions: HeldSession[] }
  | { status: "unreachable" };

/**
 * The operator's signed-in browsers.
 *
 * **This is a security surface rather than a convenience.** With no email
 * anywhere in the product (ADR 0015) there is no channel a sign-in could be
 * announced on, so this list is the only way the operator can ever notice a
 * session that is not theirs (`docs/sign-in.md`).
 *
 * **The server says which row is this browser**, because nothing else can: the
 * list carries no secret and the cookie carries nothing but one, so there is
 * nothing the interface could compare. Without it "end all others" is a guess
 * and ending a row signs the operator out of the screen they are on.
 */
export function Sessions() {
  const [listing, setListing] = useState<Listing>({ status: "asking" });
  const [ending, setEnding] = useState<string>();
  const [refusal, setRefusal] = useState<string>();
  const [busy, setBusy] = useState(false);

  const read = useCallback(async () => {
    try {
      const { data, response } = await api.GET("/sessions");

      if (data !== undefined) {
        setListing({ status: "held", sessions: data.map(held) });
        return;
      }

      // A 401 here is ordinarily this browser's own session, just ended from
      // the row below. The sign-in is already on its way in front of
      // everything, and there is nothing for this list to say about it.
      if (response.status !== 401) {
        setListing({ status: "unreachable" });
      }
    } catch {
      setListing({ status: "unreachable" });
    }
  }, []);

  useEffect(() => {
    void read();
  }, [read]);

  async function act(perform: () => Promise<boolean>) {
    setBusy(true);
    setRefusal(undefined);

    try {
      if (await perform()) {
        await read();
      } else {
        setRefusal("This installation refused to end that session.");
      }
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setBusy(false);
      setEnding(undefined);
    }
  }

  const end = (id: string) =>
    act(async () => {
      const { response } = await api.DELETE("/sessions/{id}", { params: { path: { id } } });

      // Already gone is another browser, a second click, or the daily sweep,
      // and it is the end this act was asking for either way.
      return response.status === 204 || response.status === 404;
    });

  const endEveryOther = () =>
    act(async () => {
      const { response } = await api.DELETE("/sessions/others");

      return response.status === 204;
    });

  if (listing.status === "asking") {
    return (
      <section>
        <h2>Signed-in browsers</h2>
        <p className="quiet">Reading the sessions…</p>
      </section>
    );
  }

  if (listing.status === "unreachable") {
    return (
      <section>
        <h2>Signed-in browsers</h2>
        <p className="refusal">This installation did not answer.</p>
      </section>
    );
  }

  const others = listing.sessions.filter((session) => !session.isCurrent).length;

  return (
    <section>
      <h2>Signed-in browsers</h2>
      <p>
        Several can exist at once, because one person with a desktop and a laptop is the
        normal case. There is no notification anywhere in this product, so this list is
        the only place a session that is not yours can be noticed — and ending one takes
        effect on that browser's next request.
      </p>

      <table className="listing">
        <thead>
          <tr>
            <th scope="col">Last seen from</th>
            <th scope="col">Started</th>
            <th scope="col">Last used</th>
            <th scope="col">Expires</th>
            <th scope="col">
              <span className="visually-hidden">Acts</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {listing.sessions.map((session) => (
            <tr key={session.id}>
              <th scope="row">
                {session.lastSeenFrom}
                {session.isCurrent && <span className="here"> This browser</span>}
              </th>
              <td>
                <time dateTime={session.startedAt.toISOString()}>
                  {formatTimestamp(session.startedAt)}
                </time>
              </td>
              <td>
                <LastUse at={session.lastUsedAt} />
              </td>
              <td>
                <time dateTime={session.expiresAt.toISOString()}>
                  {formatTimestamp(session.expiresAt)}
                </time>
              </td>
              <td>
                {ending === session.id ? (
                  <>
                    <button
                      type="button"
                      className="plain refusal"
                      disabled={busy}
                      onClick={() => void end(session.id)}
                    >
                      {session.isCurrent
                        ? "End it — this browser signs out"
                        : "End it now"}
                    </button>{" "}
                    <button
                      type="button"
                      className="plain"
                      onClick={() => setEnding(undefined)}
                    >
                      Leave it
                    </button>
                  </>
                ) : (
                  <button
                    type="button"
                    className="plain"
                    onClick={() => setEnding(session.id)}
                  >
                    End
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <p className="quiet">
        A session lasts on the order of thirty days and every use pushes the deadline
        forward. One that has expired is not on this list at all: it admits nothing, so
        there would be nothing to recognize it by.
      </p>

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      {others > 0 && (
        <button type="button" disabled={busy} onClick={() => void endEveryOther()}>
          End every other session
        </button>
      )}
    </section>
  );
}

function held(session: {
  id: string;
  lastSeenFrom: string;
  startedAt: string;
  lastUsedAt: string;
  expiresAt: string;
  isCurrent: boolean;
}): HeldSession {
  return {
    id: session.id,
    lastSeenFrom: session.lastSeenFrom,
    startedAt: asInstant(session.startedAt),
    lastUsedAt: asInstant(session.lastUsedAt),
    expiresAt: asInstant(session.expiresAt),
    isCurrent: session.isCurrent,
  };
}
