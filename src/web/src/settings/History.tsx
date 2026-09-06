import { useCallback, useEffect, useState } from "react";
import { api, asInstant, asNumber } from "../api/client";
import { formatTimestamp } from "../shared/time";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";
import { About, Area, Cell, Head, Listing, RowName, Said } from "./Area";
import type { components } from "../api/schema";

type Subject = components["schemas"]["Subject"];
type Act = components["schemas"]["Act"];

interface HeldChange {
  id: number;
  actorKind: components["schemas"]["IdentityKind"];
  actorName: string;
  at: Date;
  subject: Subject;
  subjectName: string;
  act: Act;
  field: string | null;
  from: string | null;
  to: string | null;
}

type Held =
  | { status: "asking" }
  | { status: "held"; changes: HeldChange[]; more: boolean }
  | { status: "unreachable" };

/** What each kind of thing is called in the sentence a row reads as. */
const named: Record<Subject, string> = {
  project: "project",
  group: "group",
  host: "host",
  ingestToken: "ingest token for",
  hostToken: "host token for",
  agentToken: "agent token",
  user: "account",
  projectAccess: "access to",
  installation: "installation setting",
};

/** The verb, in the past tense, as it is read rather than as it is stored. */
const did: Record<Act, string> = {
  created: "Created",
  renamed: "Renamed",
  changed: "Changed",
  removed: "Deleted",
  issued: "Issued an",
  revoked: "Revoked an",
  invited: "Invited",
  deactivated: "Deactivated",
  reactivated: "Reactivated",
  granted: "Granted",
  withdrawn: "Withdrew",
};

/**
 * What was changed on this installation and by whom.
 *
 * **It reads as sentences and not as rows of enumerations.** The question this
 * exists to answer is asked in words — *who deleted `orders-api`*, *who rotated
 * that ingest token* — so a row says so, and the parts it is assembled from stay
 * on the wire (`CONTEXT.md`, Change).
 *
 * **A row outlives the thing it names.** The deletions are the rows most worth
 * having and they are exactly the ones whose subject is gone, so nothing here
 * links anywhere: what is shown is the name as it read at the time.
 *
 * **It is a page and a button, not an infinite scroll and not a filter.** The
 * newest hundred is what anybody wants, "show more" walks back from there, and
 * an installation this size has no third question to ask of the list.
 */
export function History() {
  const [held, setHeld] = useState<Held>({ status: "asking" });
  const [busy, setBusy] = useState(false);

  const read = useCallback(async (before?: number) => {
    setBusy(true);

    try {
      const { data } = await api.GET("/history", {
        params: { query: before === undefined ? {} : { before } },
      });

      if (data === undefined) {
        setHeld({ status: "unreachable" });
        return;
      }

      const page = data.map(held_);

      setHeld((was) => ({
        status: "held",
        changes: was.status === "held" && before !== undefined
          ? [...was.changes, ...page]
          : page,
        // A short page is the end of the history. A full one may or may not be,
        // and asking for the next is how anybody finds out — which is cheaper
        // than a count over a table nothing else counts.
        more: page.length === PAGE,
      }));
    } catch {
      setHeld({ status: "unreachable" });
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => void read(), [read]);

  return (
    <Area title="History">
      <About>
        Everything anybody has changed about this installation, newest first. Log entries
        are never here: they are written once and never altered, so there is nothing about
        one to record.
      </About>

      {held.status === "unreachable" && <Said problem="This installation did not answer." />}

      {held.status === "held" && held.changes.length === 0 && (
        <p className="quiet">Nothing has been changed on this installation yet.</p>
      )}

      {held.status === "held" && held.changes.length > 0 && (
        <>
          <Listing>
            <thead>
              <tr>
                <Head>When</Head>
                <Head>Who</Head>
                <Head>What</Head>
              </tr>
            </thead>
            <tbody>
              {held.changes.map((change) => (
                <tr key={change.id}>
                  <Cell className="whitespace-nowrap font-mono text-xs">
                    <time dateTime={change.at.toISOString()}>
                      {formatTimestamp(change.at)}
                    </time>
                  </Cell>
                  <RowName>
                    {change.actorName}
                    {/* An agent acts with its owner's authority, so a row that
                        did not say an agent held the keyboard would be true and
                        misleading at once (ADR 0052). */}
                    {change.actorKind === "agent" && (
                      <Badge variant="secondary" className="ml-2">
                        Agent
                      </Badge>
                    )}
                  </RowName>
                  <Cell>{sentence(change)}</Cell>
                </tr>
              ))}
            </tbody>
          </Listing>

          {held.more && (
            <Button
              type="button"
              variant="link"
              size="sm"
              disabled={busy}
              onClick={() => void read(held.changes[held.changes.length - 1]?.id)}
            >
              Show more
            </Button>
          )}
        </>
      )}
    </Area>
  );
}

/** What the installation answers with at once, and what "show more" adds. */
const PAGE = 100;

/** One row as the sentence it reads as. */
function sentence(change: HeldChange): string {
  const what = `${named[change.subject]} ${change.subjectName}`;

  if (change.act === "renamed") {
    return `Renamed ${named[change.subject]} ${change.from} to ${change.to}`;
  }

  if (change.act === "changed") {
    return `Changed ${change.field} on ${what} from ${change.from} to ${change.to}`;
  }

  return `${did[change.act]} ${what}`;
}

/**
 * One row off the wire. The id arrives as a number or as a string, the way every
 * other `int64` in this application does — it is the cursor, so it is read
 * rather than assumed.
 */
function held_(change: components["schemas"]["ChangeResponse"]): HeldChange {
  return {
    id: asNumber(change.id),
    actorKind: change.actorKind,
    actorName: change.actorName,
    at: asInstant(change.at),
    subject: change.subject,
    subjectName: change.subjectName,
    act: change.act,
    field: change.field,
    from: change.from,
    to: change.to,
  };
}
