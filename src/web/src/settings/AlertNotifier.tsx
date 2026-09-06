import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import type { HeldAlerts, NotifierProof } from "./alerting";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { Field } from "../components/Field";
import { About, Area, Said } from "./Area";
import { Callout } from "../components/Page";

/** What each way a test send can end says to the person who pressed it. */
const proofs: Record<NotifierProof, string> = {
  sent: "The notifier took it. It should be on whatever you subscribe with.",
  noNotifier: "This installation has no notifier, so there was nowhere to send it.",
  refused:
    "The server answered and said no. The address is right; the token is wrong or "
    + "missing, or this token may not publish to that topic.",
  unreachable:
    "It could not be reached at all. That is the address, the network between here and "
    + "it, or a server that is not running.",
};

/**
 * The one place this installation's notifications go.
 *
 * There is **one notifier and it is ntfy**: a server, a topic and an optional
 * access token, set once for the installation. It pushes, it needs no inbound
 * port, it is self-hostable and it reaches a phone — and a notification that is
 * a name, three numbers and a URL formats identically everywhere, so the case
 * for a second integration is not that this one renders poorly
 * (`docs/alerts.md`).
 *
 * **The test send is here** because a notifier nobody has proved is one that
 * gets discovered broken on the night it mattered. Everything else about
 * alerting fails silently by design — a failed send is one line in this
 * installation's own log file, with no retry and no queue — so this is the one
 * send that answers.
 *
 * **The token is not shown until it is asked for**, which is how every other
 * secret in this product is read back (ADR 0022). Leaving the box empty keeps
 * whatever is already sealed, so correcting a topic is not re-typing a token.
 */
export function AlertNotifier({
  alerts,
  onChanged,
}: {
  alerts: HeldAlerts;
  onChanged: () => void;
}) {
  const held = alerts.notifier;

  const [server, setServer] = useState(held?.server ?? "");
  const [topic, setTopic] = useState(held?.topic ?? "");
  const [token, setToken] = useState("");
  const [shown, setShown] = useState(false);

  const [serverProblem, setServerProblem] = useState<string>();
  const [topicProblem, setTopicProblem] = useState<string>();
  const [refusal, setRefusal] = useState<string>();
  const [said, setSaid] = useState<string>();
  const [busy, setBusy] = useState(false);

  function starting() {
    setServerProblem(undefined);
    setTopicProblem(undefined);
    setRefusal(undefined);
    setSaid(undefined);
    setBusy(true);
  }

  /**
   * `null` keeps whatever is sealed, the empty string is the public topic that
   * needs none, and anything else is a token to seal. A box the operator did not
   * type in is the first of the three, which is what lets them correct a server
   * without being shown a secret they were not asking for.
   */
  async function save(accessToken: string | null) {
    starting();

    try {
      const { error, response } = await api.PUT("/alerts/notifier", {
        body: { server, topic, accessToken },
      });

      if (response.status === 204) {
        setToken("");
        setShown(false);
        onChanged();
        return;
      }

      if (response.status === 400) {
        setServerProblem(problemWith(error, "server"));
        setTopicProblem(problemWith(error, "topic"));
        return;
      }

      setRefusal("This installation refused the notifier.");
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  async function clear() {
    starting();

    try {
      const { response } = await api.DELETE("/alerts/notifier");

      if (response.status === 204) {
        setServer("");
        setTopic("");
        setToken("");
        setShown(false);
        onChanged();
        return;
      }

      setRefusal("This installation refused to clear the notifier.");
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  async function reveal() {
    starting();

    try {
      const { data } = await api.GET("/alerts/notifier/token");

      if (data === undefined) {
        setRefusal("This notifier holds no token.");
        return;
      }

      setToken(data.token);
      setShown(true);
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  async function test() {
    starting();

    try {
      const { data } = await api.POST("/alerts/notifier/test", {});

      if (data === undefined) {
        setRefusal("This installation refused to send anything.");
        return;
      }

      setSaid(proofs[data.proof]);
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Area title="The notifier">
      <About>
        Where an alert goes. There is one, and it is ntfy: a server, a topic and — if the
        topic is not a public one — an access token. Nothing else about a notification is
        configurable, because a notification here is a name, three numbers and a link, and
        that formats the same everywhere.
      </About>
      <p className="quiet">
        A public topic is readable by anyone who guesses the word. That is the plainest
        reason an alert carries no log content: what an outsider on your topic learns is
        that a project went quiet, and never a line of what it logged.
      </p>

      {held === null && (
        <p className="quiet">
          This installation has no notifier. A condition that fires without one costs a
          line in this installation's own log file and goes no further.
        </p>
      )}

      <form
        onSubmit={(event: FormEvent) => {
          event.preventDefault();
          void save(token === "" ? null : token);
        }}
      >
        <Field label="Server">
          <Input
            value={server}
            placeholder="https://ntfy.sh"
            onChange={(e) => setServer(e.target.value)}
            aria-invalid={serverProblem !== undefined || undefined}
          />
        </Field>
        <Said problem={serverProblem} />

        <Field label="Topic">
          <Input
            value={topic}
            onChange={(e) => setTopic(e.target.value)}
            aria-invalid={topicProblem !== undefined || undefined}
          />
        </Field>
        <Said problem={topicProblem} />

        <Field label="Access token">
          <Input
            type={shown ? "text" : "password"}
            value={token}
            placeholder={
              held?.hasAccessToken === true
                ? "Kept as it is unless you type one"
                : "None, which is what a public topic needs"
            }
            onChange={(e) => setToken(e.target.value)}
          />
        </Field>

        {held?.hasAccessToken === true && !shown && (
          <Button
            type="button"
            variant="link"
            size="sm"
            disabled={busy}
            onClick={() => void reveal()}
          >
            Show the token
          </Button>
        )}

        <Button
          type="submit"
          disabled={busy || server.trim() === "" || topic.trim() === ""}
          className="w-fit"
        >
          {held === null ? "Set the notifier" : "Save the notifier"}
        </Button>
      </form>

      <Said problem={refusal} />
      {said !== undefined && <Callout>{said}</Callout>}

      {held !== null && (
        <p>
          <Button type="button" disabled={busy} onClick={() => void test()} className="w-fit">
            Send a test notification
          </Button>
        </p>
      )}

      {held !== null && (
        <p className="quiet">
          It is the shape a real alert has and it belongs to no condition. Nothing about it
          is kept: what a notifier did five minutes ago is not evidence about what it will
          do tonight.
        </p>
      )}

      {held?.hasAccessToken === true && (
        <p>
          <Button
            type="button"
            variant="link"
            size="sm"
            disabled={busy}
            onClick={() => void save("")}
          >
            Publish without a token
          </Button>
        </p>
      )}

      {held !== null && (
        <p>
          <Button
            type="button"
            variant="link"
            size="sm"
            disabled={busy}
            onClick={() => void clear()}
          >
            Remove the notifier
          </Button>
        </p>
      )}

      {held !== null && (
        <p className="quiet">
          Removing it takes the token with it and leaves the switches where they are.
        </p>
      )}
    </Area>
  );
}
