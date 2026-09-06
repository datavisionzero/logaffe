import { useCallback, useEffect, useState } from "react";
import { api, asInstant } from "../api/client";
import { formatTimestamp } from "../shared/time";
import { LastUse } from "./LastUse";
import { Button } from "../components/ui/button";
import { About, Area, Cell, Head, Listing, RowName, Said } from "./Area";

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
      <Area title="Signed-in browsers">
        <p className="quiet">Reading the sessions…</p>
      </Area>
    );
  }

  if (listing.status === "unreachable") {
    return (
      <Area title="Signed-in browsers">
        <p className="refusal text-sm">This installation did not answer.</p>
      </Area>
    );
  }

  const others = listing.sessions.filter((session) => !session.isCurrent).length;

  return (
    <Area title="Signed-in browsers">
      <About>
        Several can exist at once, because one person with a desktop and a laptop is the
        normal case. There is no notification anywhere in this product, so this list is
        the only place a session that is not yours can be noticed — and ending one takes
        effect on that browser's next request.
      </About>

      <Listing>
        <thead>
          <tr>
            <Head>Last seen from</Head>
            <Head>Started</Head>
            <Head>Last used</Head>
            <Head>Expires</Head>
            <Head>
              <span className="sr-only">Acts</span>
            </Head>
          </tr>
        </thead>
        <tbody>
          {listing.sessions.map((session) => (
            <tr key={session.id}>
              <RowName>
                {session.lastSeenFrom}
                {session.isCurrent && <span className="here"> This browser</span>}
              </RowName>
              <Cell>
                <time dateTime={session.startedAt.toISOString()}>
                  {formatTimestamp(session.startedAt)}
                </time>
              </Cell>
              <Cell>
                <LastUse at={session.lastUsedAt} />
              </Cell>
              <Cell>
                <time dateTime={session.expiresAt.toISOString()}>
                  {formatTimestamp(session.expiresAt)}
                </time>
              </Cell>
              <Cell>
                {ending === session.id ? (
                  <>
                    <Button
                      type="button"
                      variant="link"
                      size="sm" className="text-destructive"
                      disabled={busy}
                      onClick={() => void end(session.id)}
                    >
                      {session.isCurrent
                        ? "End it — this browser signs out"
                        : "End it now"}
                    </Button>{" "}
                    <Button
                      type="button"
                      variant="link"
                      size="sm"
                      onClick={() => setEnding(undefined)}
                    >
                      Leave it
                    </Button>
                  </>
                ) : (
                  <Button
                    type="button"
                    variant="link"
                    size="sm"
                    onClick={() => setEnding(session.id)}
                  >
                    End
                  </Button>
                )}
              </Cell>
            </tr>
          ))}
        </tbody>
      </Listing>

      <p className="quiet">
        A session lasts on the order of thirty days and every use pushes the deadline
        forward. One that has expired is not on this list at all: it admits nothing, so
        there would be nothing to recognize it by.
      </p>

      <Said problem={refusal} />

      {others > 0 && (
        <Button
          type="button"
          disabled={busy}
          onClick={() => void endEveryOther()}
          className="w-fit"
        >
          End every other session
        </Button>
      )}
    </Area>
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
