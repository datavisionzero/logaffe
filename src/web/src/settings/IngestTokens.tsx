import { useCallback, useEffect, useState } from "react";
import { api, asInstant } from "../api/client";
import { copyToClipboard, whyNotCopied, type Copying } from "../shared/clipboard";
import { formatTimestamp } from "../shared/time";
import { LastUse } from "./LastUse";
import { Button } from "../components/ui/button";
import { Snippet } from "../components/Page";
import { About, Area, Cell, Head, Listing, RowName } from "./Area";

/** One of a project's tokens as the list carries it, which is no secret at all. */
interface HeldToken {
  id: string;
  /**
   * The non-secret middle of the token's text, which is how the operator tells
   * the two tokens of a rotation apart — an ingest token has no name.
   */
  identifier: string;
  issuedAt: Date;
  lastUsedAt: Date | null;
}

type Listing =
  | { status: "asking" }
  | { status: "held"; tokens: HeldToken[] }
  | { status: "gone" }
  | { status: "unreachable" };

/** The token itself, which is here only because it was asked for. */
interface ReadBack {
  id: string;
  token: string;
  snippet: string;
}

/**
 * What a project can currently receive on.
 *
 * A project holds **one token, and two while it is being rotated**. This is the
 * screen where an operator watches the old one stop being used: the last use is
 * what says a rotation is finished, and without it the last step is guesswork
 * about whether something forgotten is still delivering on the token about to be
 * revoked (`docs/projects.md`).
 *
 * **The delivery arrives with the token rather than being assembled here.**
 * Issuing one returns it and reading one back returns it again, because reading
 * a token back and being able to use it are one errand — which is also what
 * makes a mislaid token something to look up instead of something to rotate
 * (ADR 0022).
 */
export function IngestTokens({
  projectId,
  onChanged,
}: {
  projectId: string;
  /** The project list carries the token count, so an act here changes it. */
  onChanged: () => void;
}) {
  const [listing, setListing] = useState<Listing>({ status: "asking" });
  const [shown, setShown] = useState<ReadBack>();
  const [revoking, setRevoking] = useState<string>();
  const [refusal, setRefusal] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [copying, setCopying] = useState<Copying>();

  const read = useCallback(async () => {
    try {
      const { data, response } = await api.GET("/projects/{projectId}/ingest-tokens", {
        params: { path: { projectId } },
      });

      if (data !== undefined) {
        setListing({ status: "held", tokens: data.map(held) });
        return;
      }

      // A project that is not there answers 404 rather than an empty list: a
      // closed door and a deleted project are two different readings, and one
      // of them is the settings of something gone.
      if (response.status === 404) {
        setListing({ status: "gone" });
      } else if (response.status !== 401) {
        setListing({ status: "unreachable" });
      }
    } catch {
      setListing({ status: "unreachable" });
    }
  }, [projectId]);

  useEffect(() => {
    void read();
  }, [read]);

  /** Every act here ends the same way: read the list back and tell the shell. */
  async function act(perform: () => Promise<string | undefined>) {
    setBusy(true);
    setRefusal(undefined);

    try {
      const refused = await perform();

      if (refused !== undefined) {
        setRefusal(refused);
        return;
      }

      await read();
      onChanged();
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  const issue = () =>
    act(async () => {
      const { data, response } = await api.POST("/projects/{projectId}/ingest-tokens", {
        params: { path: { projectId } },
      });

      if (data === undefined) {
        return response.status === 409
          ? "This project already holds two tokens. Revoke one before issuing another — "
              + "two is what moving deployments over one at a time needs."
          : "This installation refused to issue a token.";
      }

      // Straight to the delivery, because issuing a token is something an
      // operator does on the way to pasting it somewhere.
      setShown({ id: data.id, token: data.token, snippet: data.deliverySnippet });
      setCopying(undefined);

      return undefined;
    });

  const revoke = (id: string) =>
    act(async () => {
      const { response } = await api.DELETE("/ingest-tokens/{id}", {
        params: { path: { id } },
      });

      setRevoking(undefined);

      if (shown?.id === id) {
        setShown(undefined);
      }

      // Already gone is a second click or another tab, and the end this act was
      // asking for either way.
      return response.status === 204 || response.status === 404
        ? undefined
        : "This installation refused the revocation.";
    });

  async function show(id: string) {
    setRefusal(undefined);
    setCopying(undefined);

    try {
      const { data } = await api.GET("/ingest-tokens/{id}/token", {
        params: { path: { id } },
      });

      if (data === undefined) {
        setRefusal("This token is gone. It may have been revoked from another browser.");
        return;
      }

      setShown({ id, token: data.token, snippet: data.deliverySnippet });
    } catch {
      setRefusal("This installation did not answer.");
    }
  }

  return (
    <Area title="Ingest tokens">
      <About>
        A token is what admits a delivery to this project, and it writes and reads
        nothing. There is one ordinarily and two while it is being rotated: issue the
        second, move the applications over, watch the old one's last use stop moving, and
        revoke it. Revocation takes effect immediately.
      </About>

      {listing.status === "asking" && <p className="quiet">Reading the project's tokens…</p>}

      {listing.status === "unreachable" && (
        <p className="refusal text-sm">This installation did not answer.</p>
      )}

      {listing.status === "gone" && (
        <p className="refusal text-sm">
          This project is gone. It may have been deleted from another browser.
        </p>
      )}

      {listing.status === "held" && listing.tokens.length === 0 && (
        <p className="closed">
          This project holds no token, so nothing can deliver to it. That is a door an
          operator can leave closed — issuing one opens it.
        </p>
      )}

      {listing.status === "held" && listing.tokens.length > 0 && (
        <Listing>
          <thead>
            <tr>
              <Head>Token</Head>
              <Head>Issued</Head>
              <Head>Last used</Head>
              <Head>
                <span className="sr-only">Acts</span>
              </Head>
            </tr>
          </thead>
          <tbody>
            {listing.tokens.map((token) => (
              <tr key={token.id}>
                <RowName>
                  <code className="font-mono text-xs">{token.identifier}</code>
                </RowName>
                <Cell>
                  <time dateTime={token.issuedAt.toISOString()} className="font-mono text-xs">
                    {formatTimestamp(token.issuedAt)}
                  </time>
                </Cell>
                <Cell>
                  <LastUse at={token.lastUsedAt} />
                </Cell>
                <Cell className="whitespace-normal">
                  <Button variant="link" size="xs" onClick={() => void show(token.id)}>
                    Show the delivery
                  </Button>{" "}
                  {revoking === token.id ? (
                    <>
                      <Button
                        variant="link"
                        size="xs"
                        className="text-destructive"
                        disabled={busy}
                        onClick={() => void revoke(token.id)}
                      >
                        Revoke it — anything still delivering with it gets 401
                      </Button>{" "}
                      <Button variant="link" size="xs" onClick={() => setRevoking(undefined)}>
                        Keep it
                      </Button>
                    </>
                  ) : (
                    <Button variant="link" size="xs" onClick={() => setRevoking(token.id)}>
                      Revoke
                    </Button>
                  )}
                </Cell>
              </tr>
            ))}
          </tbody>
        </Listing>
      )}

      <p className="quiet text-xs">
        A last use is recorded within five minutes of the delivery that earned it, and is
        shown no finer than that (ADR 0033).
      </p>

      {refusal !== undefined && <p className="refusal text-sm">{refusal}</p>}

      {listing.status === "held" && listing.tokens.length < 2 && (
        <Button type="button" className="w-fit" disabled={busy} onClick={() => void issue()}>
          {listing.tokens.length === 0 ? "Issue an ingest token" : "Issue a second token"}
        </Button>
      )}

      {shown !== undefined && (
        <div className="grid gap-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">The delivery</h3>
          <p className="max-w-prose text-muted-foreground">
            The address, the header and the token are already in it. This is the same
            block the first-run guide hands over, and it comes back whenever the token is
            read back.
          </p>
          <Snippet>{shown.snippet}</Snippet>
          <p className="quiet text-xs">
            The token by itself:{" "}
            <code className="font-mono break-all">{shown.token}</code>
          </p>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="outline"
              onClick={() => void copyToClipboard(shown.snippet).then(setCopying)}
            >
              {copying === "copied" ? "Copied" : "Copy the delivery"}
            </Button>
            <Button type="button" variant="link" size="sm" onClick={() => setShown(undefined)}>
              Hide it
            </Button>
          </div>
          {whyNotCopied(copying) !== undefined && (
            <p className="refusal text-sm">{whyNotCopied(copying)}</p>
          )}
        </div>
      )}
    </Area>
  );
}

function held(token: {
  id: string;
  identifier: string;
  issuedAt: string;
  lastUsedAt: string | null;
}): HeldToken {
  return {
    id: token.id,
    identifier: token.identifier,
    issuedAt: asInstant(token.issuedAt),
    lastUsedAt: token.lastUsedAt === null ? null : asInstant(token.lastUsedAt),
  };
}
