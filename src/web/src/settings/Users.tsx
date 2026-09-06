import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, asNumber } from "../api/client";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { Field } from "../components/Field";
import { About, Area, Cell, Confirming, Head, Listing, RowName, Said } from "./Area";
import { ProjectAccess } from "./ProjectAccess";

interface HeldUser {
  id: string;
  name: string;
  email: string;
  state: "invited" | "active" | "deactivated";
  administrator: boolean;
  hasSecondFactor: boolean;
  projects: number;
}

type Held =
  | { status: "asking" }
  | { status: "held"; users: HeldUser[] }
  | { status: "unreachable" };

/**
 * The people on this installation, and what an administrator does to them
 * (ADR 0052).
 *
 * **Nothing here deletes.** An account that should not be used is deactivated,
 * so that every project assignment and every record of who changed something
 * keeps pointing at somebody. The word on the button says so.
 *
 * **Nothing here reaches somebody else's credentials.** No screen in this
 * product sets another person's password, enrols their second factor or reads
 * their sessions — those are that account's own (`docs/sign-in.md`). What an
 * administrator sees of them is whether a second factor is enrolled, because
 * somebody who cannot see that cannot have the conversation.
 *
 * **There is always one active administrator.** The last one cannot be
 * deactivated or stripped of the role, and the installation says so rather than
 * the screen guessing at it: the count is the rule, and it is counted where the
 * rows are.
 */
export function Users({ me }: { me: string | undefined }) {
  const [held, setHeld] = useState<Held>({ status: "asking" });
  const [refusal, setRefusal] = useState<string>();
  const [note, setNote] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [assigning, setAssigning] = useState<string>();

  const read = useCallback(async () => {
    try {
      const { data } = await api.GET("/users");

      setHeld(
        data === undefined
          ? { status: "unreachable" }
          : { status: "held", users: data.map(held_) },
      );
    } catch {
      setHeld({ status: "unreachable" });
    }
  }, []);

  useEffect(() => void read(), [read]);

  async function act(what: () => Promise<Response>, said: string) {
    setRefusal(undefined);
    setNote(undefined);
    setBusy(true);

    try {
      const response = await what();

      if (response.status === 204) {
        setNote(said);
        await read();
        return;
      }

      setRefusal(
        response.status === 409
          ? "There is always at least one active administrator, and that is the last one."
          : response.status === 503
            ? "This installation has no SMTP configured, so it cannot send a link."
            : "This installation refused that.",
      );
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Area title="People">
        <About>
          Everybody with an account here. Inviting somebody creates the account and sends
          them a link to set their own password — they arrive with no project access until
          you give them some.
        </About>

        <Invite onInvited={read} onSaid={(what) => setNote(what)} />

        {held.status === "unreachable" && (
          <Said problem="This installation did not answer." />
        )}

        {held.status === "held" && (
          <Listing>
            <thead>
              <tr>
                <Head>Name</Head>
                <Head>Address</Head>
                <Head>State</Head>
                <Head>Second factor</Head>
                <Head>Projects</Head>
                <Head>{""}</Head>
              </tr>
            </thead>
            <tbody>
              {held.users.map((user) => (
                <tr key={user.id}>
                  <RowName>
                    {user.name}
                    {user.administrator && (
                      <Badge variant="secondary" className="ml-2">
                        Administrator
                      </Badge>
                    )}
                    {user.id === me && <span className="quiet ml-2 text-xs">you</span>}
                  </RowName>
                  <Cell className="font-mono text-xs">{user.email}</Cell>
                  <Cell>{said(user.state)}</Cell>
                  <Cell className="quiet">{user.hasSecondFactor ? "Enrolled" : "None"}</Cell>
                  <Cell className="quiet">{user.projects}</Cell>
                  <Cell className="text-right">
                    <div className="flex justify-end gap-1">
                      {user.state === "invited" && (
                        <Button
                          type="button"
                          variant="link"
                          size="sm"
                          disabled={busy}
                          onClick={() =>
                            act(
                              () =>
                                api
                                  .POST("/users/{id}/invitation", {
                                    params: { path: { id: user.id } },
                                  })
                                  .then((r) => r.response),
                              `A fresh invitation is on its way to ${user.email}.`,
                            )
                          }
                        >
                          Invite again
                        </Button>
                      )}

                      <Button
                        type="button"
                        variant="link"
                        size="sm"
                        disabled={busy}
                        onClick={() =>
                          act(
                            () =>
                              api
                                .PUT("/users/{id}/role", {
                                  params: { path: { id: user.id } },
                                  body: { administrator: !user.administrator },
                                })
                                .then((r) => r.response),
                            user.administrator
                              ? `${user.name} no longer administers this installation.`
                              : `${user.name} administers this installation.`,
                          )
                        }
                      >
                        {user.administrator ? "Take the role" : "Make administrator"}
                      </Button>

                      <Button
                        type="button"
                        variant="link"
                        size="sm"
                        disabled={busy}
                        onClick={() => setAssigning(assigning === user.id ? undefined : user.id)}
                      >
                        Projects
                      </Button>

                      {user.state === "deactivated" ? (
                        <Button
                          type="button"
                          variant="link"
                          size="sm"
                          disabled={busy}
                          onClick={() =>
                            act(
                              () =>
                                api
                                  .DELETE("/users/{id}/deactivation", {
                                    params: { path: { id: user.id } },
                                  })
                                  .then((r) => r.response),
                              `${user.name} can sign in again.`,
                            )
                          }
                        >
                          Reactivate
                        </Button>
                      ) : (
                        <Button
                          type="button"
                          variant="link"
                          size="sm"
                          disabled={busy}
                          onClick={() =>
                            act(
                              () =>
                                api
                                  .POST("/users/{id}/deactivation", {
                                    params: { path: { id: user.id } },
                                  })
                                  .then((r) => r.response),
                              `${user.name} is deactivated. Their sessions are over and their `
                                + "agents are silent.",
                            )
                          }
                        >
                          Deactivate
                        </Button>
                      )}
                    </div>
                  </Cell>
                </tr>
              ))}
            </tbody>
          </Listing>
        )}

        <Said problem={refusal} note={note} />

        {assigning !== undefined && (
          <Confirming>
            <ProjectAccess
              userId={assigning}
              name={
                held.status === "held"
                  ? (held.users.find((user) => user.id === assigning)?.name ?? "")
                  : ""
              }
              onDone={() => {
                setAssigning(undefined);
                void read();
              }}
            />
          </Confirming>
        )}
      </Area>
    </>
  );
}

/**
 * Inviting somebody. The role is decided here rather than afterwards, because
 * an administrator inviting another administrator should not have to remember a
 * second step.
 */
function Invite({
  onInvited,
  onSaid,
}: {
  onInvited: () => Promise<void>;
  onSaid: (what: string) => void;
}) {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [administrator, setAdministrator] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [inviting, setInviting] = useState(false);

  async function invite(event: FormEvent) {
    event.preventDefault();
    setRefusal(undefined);
    setInviting(true);

    try {
      const { response } = await api.POST("/users", {
        body: { name, email, administrator },
      });

      if (response.status === 204) {
        onSaid(`An invitation is on its way to ${email}.`);
        setName("");
        setEmail("");
        setAdministrator(false);
        await onInvited();
        return;
      }

      setRefusal(
        response.status === 409
          ? "Somebody already holds that address."
          : response.status === 503
            ? "This installation has no SMTP configured, so it cannot send an invitation. "
              + "See docs/setup.md."
            : "That is not a name and an email address.",
      );
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setInviting(false);
    }
  }

  return (
    <form onSubmit={invite} className="grid max-w-md gap-3">
      <Field label="Name">
        <Input value={name} onChange={(e) => setName(e.target.value)} />
      </Field>

      <Field label="Email address">
        <Input
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
      </Field>

      <label className="flex w-fit items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={administrator}
          onChange={(e) => setAdministrator(e.target.checked)}
        />
        They administer this installation
      </label>

      <Said problem={refusal} />

      <Button type="submit" disabled={inviting} className="w-fit">
        Send an invitation
      </Button>
    </form>
  );
}

/** What a state is called on the screen. */
function said(state: HeldUser["state"]): string {
  switch (state) {
    case "invited":
      return "Invited";
    case "deactivated":
      return "Deactivated";
    default:
      return "Active";
  }
}

/**
 * One row off the wire. The count arrives as a number or as a string, the way
 * every other count in this application does — the contract's `int64` is wider
 * than JSON's number, so it is read rather than assumed.
 */
function held_(user: {
  id: string;
  name: string;
  email: string;
  state: HeldUser["state"];
  administrator: boolean;
  hasSecondFactor: boolean;
  projects: number | string;
}): HeldUser {
  return {
    id: user.id,
    name: user.name,
    email: user.email,
    state: user.state,
    administrator: user.administrator,
    hasSecondFactor: user.hasSecondFactor,
    projects: asNumber(user.projects),
  };
}
