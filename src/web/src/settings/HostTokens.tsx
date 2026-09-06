import { useCallback, useEffect, useState } from "react";
import { api, asInstant } from "../api/client";
import { copyToClipboard, whyNotCopied, type Copying } from "../shared/clipboard";
import { formatTimestamp } from "../shared/time";
import { LastUse } from "./LastUse";
import { Button } from "../components/ui/button";
import { Snippet } from "../components/Page";
import { About, Area, Cell, Head, Listing, RowName, Said } from "./Area";

/** One of a host's tokens as the list carries it, which is no secret at all. */
interface HeldToken {
  id: string;
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
  command: string;
}

/**
 * What a host can currently report on, and the command that starts the
 * collector reporting.
 *
 * A host holds **one token, and two while it is being rotated** — the ingest
 * token's model, entire. The token *is* the host as far as the collector is
 * concerned: it says which host a delivery belongs to, and there is nothing
 * else for the collector to be told beyond an address (`docs/metrics.md`).
 *
 * **The command arrives with the token rather than being assembled here**, with
 * this installation's address, this host's token and the two read-only mounts
 * already in it. Which mounts a collector needs is not something the operator
 * should have to know, and it is the one part of connecting a machine most
 * likely to be got wrong in a way that reports nothing.
 *
 * It is also why **making a host does not hand this back and issuing its token
 * does** — the same order an ingest token has with its delivery snippet. The
 * screen makes both calls, which is what `docs/ui.md` describes.
 */
export function HostTokens({
  hostId,
  onChanged,
}: {
  hostId: string;
  /** The host list carries the token count, so an act here changes it. */
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
      const { data, response } = await api.GET("/hosts/{hostId}/host-tokens", {
        params: { path: { hostId } },
      });

      if (data !== undefined) {
        setListing({ status: "held", tokens: data.map(held) });
        return;
      }

      if (response.status === 404) {
        setListing({ status: "gone" });
      } else if (response.status !== 401) {
        setListing({ status: "unreachable" });
      }
    } catch {
      setListing({ status: "unreachable" });
    }
  }, [hostId]);

  useEffect(() => {
    void read();
  }, [read]);

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
      const { data, response } = await api.POST("/hosts/{hostId}/host-tokens", {
        params: { path: { hostId } },
      });

      if (data === undefined) {
        return response.status === 409
          ? "This host already holds two tokens. Revoke one before issuing another — "
              + "two is what moving a collector over one machine at a time needs."
          : "This installation refused to issue a token.";
      }

      // Straight to the command, because issuing a token is something an
      // operator does on the way to pasting it into a shell.
      setShown({ id: data.id, token: data.token, command: data.collectorCommand });
      setCopying(undefined);

      return undefined;
    });

  const revoke = (id: string) =>
    act(async () => {
      const { response } = await api.DELETE("/host-tokens/{id}", { params: { path: { id } } });

      setRevoking(undefined);

      if (shown?.id === id) {
        setShown(undefined);
      }

      return response.status === 204 || response.status === 404
        ? undefined
        : "This installation refused the revocation.";
    });

  async function show(id: string) {
    setRefusal(undefined);
    setCopying(undefined);

    try {
      const { data } = await api.GET("/host-tokens/{id}/token", { params: { path: { id } } });

      if (data === undefined) {
        setRefusal("This token is gone. It may have been revoked from another browser.");
        return;
      }

      setShown({ id, token: data.token, command: data.collectorCommand });
    } catch {
      setRefusal("This installation did not answer.");
    }
  }

  return (
    <Area title="Host tokens">
      <About>
        A token is what admits a sample to this host, and it reads nothing at all. There
        is one ordinarily and two while it is being rotated: issue the second, restart the
        collector with it, watch the old one's last use stop moving, and revoke it.
      </About>

      {listing.status === "asking" && <p className="quiet">Reading the host's tokens…</p>}

      {listing.status === "unreachable" && (
        <p className="refusal text-sm">This installation did not answer.</p>
      )}

      {listing.status === "gone" && (
        <p className="refusal text-sm">
          This host is gone. It may have been deleted from another browser.
        </p>
      )}

      {listing.status === "held" && listing.tokens.length === 0 && (
        <p className="closed">
          This host holds no token, so nothing can report to it. Issuing one hands back
          the command that starts its collector.
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
                  <code>{token.identifier}</code>
                </RowName>
                <Cell>
                  <time dateTime={token.issuedAt.toISOString()}>
                    {formatTimestamp(token.issuedAt)}
                  </time>
                </Cell>
                <Cell>
                  <LastUse at={token.lastUsedAt} />
                </Cell>
                <Cell>
                  <Button
                    type="button"
                    variant="link"
                    size="sm"
                    onClick={() => void show(token.id)}
                  >
                    Show the command
                  </Button>{" "}
                  {revoking === token.id ? (
                    <>
                      <Button
                        type="button"
                        variant="link"
                        size="sm" className="text-destructive"
                        disabled={busy}
                        onClick={() => void revoke(token.id)}
                      >
                        Revoke it — a collector still reporting with it gets 401
                      </Button>{" "}
                      <Button
                        type="button"
                        variant="link"
                        size="sm"
                        onClick={() => setRevoking(undefined)}
                      >
                        Keep it
                      </Button>
                    </>
                  ) : (
                    <Button
                      type="button"
                      variant="link"
                      size="sm"
                      onClick={() => setRevoking(token.id)}
                    >
                      Revoke
                    </Button>
                  )}
                </Cell>
              </tr>
            ))}
          </tbody>
        </Listing>
      )}

      <Said problem={refusal} />

      {listing.status === "held" && listing.tokens.length < 2 && (
        <Button type="button" disabled={busy} onClick={() => void issue()} className="w-fit">
          {listing.tokens.length === 0 ? "Issue a host token" : "Issue a second token"}
        </Button>
      )}

      {shown !== undefined && (
        <div className="grid gap-3 rounded-lg border bg-card p-4">
          <h3 className="text-sm font-semibold">The collector</h3>
          <p>
            The address, the token and the two mounts are already in it. Run it on the
            machine this host stands for, and the first sample arrives within a minute.
          </p>
          <Snippet>{shown.command}</Snippet>
          <p className="quiet">
            The two mounts are the whole of what it asks for: the host's{" "}
            <code className="font-mono">/proc</code> and its root filesystem, both
            read-only. It is not privileged, it does not join
            the host's process namespace, it does not touch the Docker socket, and it opens
            no port — it posts outbound and is never connected to.
          </p>
          <p className="quiet">
            The token by itself:{" "}
            <code className="font-mono break-all">{shown.token}</code>
          </p>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="outline"
              onClick={() => void copyToClipboard(shown.command).then(setCopying)}
            >
              {copying === "copied" ? "Copied" : "Copy the command"}
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
