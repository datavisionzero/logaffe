import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, asInstant, problemWith } from "../api/client";
import { copyToClipboard, whyNotCopied, type Copying } from "../shared/clipboard";
import { formatTimestamp } from "../shared/time";
import { LastUse } from "./LastUse";

interface HeldToken {
  id: string;
  /** A label for this list and nothing the server acts on. */
  name: string;
  issuedAt: Date;
  lastUsedAt: Date | null;
}

type Listing =
  | { status: "asking" }
  | { status: "held"; tokens: HeldToken[] }
  | { status: "unreachable" };

/** The configuration block, which is here only because it was asked for. */
interface ReadBack {
  id: string;
  token: string;
  configuration: string;
}

/**
 * The tokens agents read with.
 *
 * **They live here rather than inside a project** because an agent token reads
 * every project — putting it under one of them would say something untrue about
 * what it can do (`docs/ui.md`). It is otherwise the same credential as an
 * ingest token pointing the other way: issued by the operator, readable again
 * whenever it is wanted, and revocable individually and immediately (ADR 0021).
 *
 * What is handed over is the **finished client configuration** rather than the
 * bare token. Assembling that from an address, a header name and a string is the
 * fiddliest part of connecting an agent and the part most likely to be got wrong
 * in a way that reports nothing useful (`docs/mcp.md`).
 */
export function AgentTokens() {
  const [listing, setListing] = useState<Listing>({ status: "asking" });
  const [shown, setShown] = useState<ReadBack>();
  const [renaming, setRenaming] = useState<string>();
  const [revoking, setRevoking] = useState<string>();
  const [name, setName] = useState("");
  const [problem, setProblem] = useState<string>();
  const [refusal, setRefusal] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [copying, setCopying] = useState<Copying>();

  const read = useCallback(async () => {
    try {
      const { data, response } = await api.GET("/agent-tokens");

      if (data !== undefined) {
        setListing({ status: "held", tokens: data.map(held) });
      } else if (response.status !== 401) {
        setListing({ status: "unreachable" });
      }
    } catch {
      setListing({ status: "unreachable" });
    }
  }, []);

  useEffect(() => {
    void read();
  }, [read]);

  /**
   * Every act here ends by reading the list back, and a refusal ends instead.
   * `false` is a refusal that has already been placed where it belongs — the
   * field it is about — and reading the list back after one would be asking the
   * installation for something nobody asked for.
   */
  async function act(perform: () => Promise<string | undefined | false>) {
    setBusy(true);
    setRefusal(undefined);
    setProblem(undefined);

    try {
      const refused = await perform();

      if (refused === undefined) {
        await read();
      } else if (refused !== false) {
        setRefusal(refused);
      }
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  async function issue(event: FormEvent) {
    event.preventDefault();

    await act(async () => {
      // Reading, and said rather than left out: what an agent is given unless
      // the operator decides otherwise (`VISION.md`). Choosing the other kind is
      // not on this screen yet.
      const { data, error, response } = await api.POST("/agent-tokens", {
        body: { name, kind: "reading", mayDestroy: false },
      });

      if (data === undefined) {
        if (response.status === 400) {
          setProblem(problemWith(error, "name"));
          return false;
        }

        return "This installation refused to issue a token.";
      }

      // Straight to the block, because issuing one is something an operator
      // does on the way to pasting it into an agent.
      setShown({ id: data.id, token: data.token, configuration: data.clientConfiguration });
      setCopying(undefined);
      setName("");

      return undefined;
    });
  }

  const rename = (id: string, renamed: string) =>
    act(async () => {
      const { response, error } = await api.PATCH("/agent-tokens/{id}", {
        params: { path: { id } },
        body: { name: renamed },
      });

      setRenaming(undefined);

      if (response.status === 204 || response.status === 404) {
        return undefined;
      }

      return response.status === 400
        ? (problemWith(error, "name") ?? "That is not a name.")
        : "This installation refused the rename.";
    });

  const revoke = (id: string) =>
    act(async () => {
      const { response } = await api.DELETE("/agent-tokens/{id}", {
        params: { path: { id } },
      });

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
      const { data } = await api.GET("/agent-tokens/{id}/token", {
        params: { path: { id } },
      });

      if (data === undefined) {
        setRefusal("This token is gone. It may have been revoked from another browser.");
        return;
      }

      setShown({ id, token: data.token, configuration: data.clientConfiguration });
    } catch {
      setRefusal("This installation did not answer.");
    }
  }

  return (
    <section>
      <h2>Agent tokens</h2>
      <p>
        An agent authenticates with one of these and can do nothing but read entries and
        count them. Each reads every project, which is why they are here rather than in
        one project's settings. Several can exist at once, so a terminal agent and a
        desktop agent can be retired one at a time.
      </p>

      {listing.status === "asking" && <p className="quiet">Reading the agent tokens…</p>}

      {listing.status === "unreachable" && (
        <p className="refusal">This installation did not answer.</p>
      )}

      {listing.status === "held" && listing.tokens.length === 0 && (
        <p className="quiet">No agent has been given a token yet.</p>
      )}

      {listing.status === "held" && listing.tokens.length > 0 && (
        <table className="listing">
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Issued</th>
              <th scope="col">Last used</th>
              <th scope="col">
                <span className="visually-hidden">Acts</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {listing.tokens.map((token) => (
              <tr key={token.id}>
                <th scope="row">
                  {renaming === token.id ? (
                    <Rename
                      token={token}
                      busy={busy}
                      onRename={(renamed) => void rename(token.id, renamed)}
                      onLeave={() => setRenaming(undefined)}
                    />
                  ) : (
                    token.name
                  )}
                </th>
                <td>
                  <time dateTime={token.issuedAt.toISOString()}>
                    {formatTimestamp(token.issuedAt)}
                  </time>
                </td>
                <td>
                  <LastUse at={token.lastUsedAt} />
                </td>
                <td>
                  <button type="button" className="plain" onClick={() => void show(token.id)}>
                    Show the configuration
                  </button>{" "}
                  <button
                    type="button"
                    className="plain"
                    onClick={() => setRenaming(token.id)}
                  >
                    Rename
                  </button>{" "}
                  {revoking === token.id ? (
                    <>
                      <button
                        type="button"
                        className="plain refusal"
                        disabled={busy}
                        onClick={() => void revoke(token.id)}
                      >
                        Revoke it — the agent using it stops reading
                      </button>{" "}
                      <button
                        type="button"
                        className="plain"
                        onClick={() => setRevoking(undefined)}
                      >
                        Keep it
                      </button>
                    </>
                  ) : (
                    <button
                      type="button"
                      className="plain"
                      onClick={() => setRevoking(token.id)}
                    >
                      Revoke
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <p className="quiet">
        A token that has not been used in months is one to revoke, and this list is the
        only place that fact is visible. The last use is recorded within five minutes of
        the read that earned it and is shown no finer than that (ADR 0033).
      </p>

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      <form onSubmit={issue}>
        <label>
          Name for a new token
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            aria-invalid={problem !== undefined || undefined}
          />
        </label>
        {problem !== undefined && <p className="refusal">{problem}</p>}

        <button type="submit" disabled={busy || name.trim() === ""}>
          Issue an agent token
        </button>
      </form>

      {shown !== undefined && (
        <section>
          <h3>The client configuration</h3>
          <p>
            Paste this into the agent. This installation's address and this token are
            already in it, and the same block comes back whenever the token is read back.
          </p>
          <pre>{shown.configuration}</pre>
          <p className="quiet">
            The token by itself: <code>{shown.token}</code>
          </p>
          <button
            type="button"
            onClick={() => void copyToClipboard(shown.configuration).then(setCopying)}
          >
            {copying === "copied" ? "Copied" : "Copy the configuration"}
          </button>
          {whyNotCopied(copying) !== undefined && (
            <p className="refusal">{whyNotCopied(copying)}</p>
          )}
          <button type="button" className="plain" onClick={() => setShown(undefined)}>
            Hide it
          </button>
        </section>
      )}
    </section>
  );
}

/**
 * The name is a label for this list: it does not identify the token to the
 * server, and changing it changes nothing else — so an agent whose token is
 * renamed does not notice and nothing has to be reconnected.
 */
function Rename({
  token,
  busy,
  onRename,
  onLeave,
}: {
  token: HeldToken;
  busy: boolean;
  onRename: (renamed: string) => void;
  onLeave: () => void;
}) {
  const [renamed, setRenamed] = useState(token.name);

  return (
    <>
      <input
        value={renamed}
        onChange={(e) => setRenamed(e.target.value)}
        aria-label={`Name of ${token.name}`}
      />
      <button
        type="button"
        className="plain"
        disabled={busy || renamed.trim() === ""}
        onClick={() => onRename(renamed)}
      >
        Save
      </button>{" "}
      <button type="button" className="plain" onClick={onLeave}>
        Cancel
      </button>
    </>
  );
}

function held(token: {
  id: string;
  name: string;
  issuedAt: string;
  lastUsedAt: string | null;
}): HeldToken {
  return {
    id: token.id,
    name: token.name,
    issuedAt: asInstant(token.issuedAt),
    lastUsedAt: token.lastUsedAt === null ? null : asInstant(token.lastUsedAt),
  };
}
