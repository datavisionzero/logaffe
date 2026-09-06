import { useState, type FormEvent } from "react";
import { api } from "../api/client";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { Field } from "../components/Field";
import { Gate, PageTitle } from "../components/Page";

/**
 * Asking for a password back (ADR 0053).
 *
 * **It says the same thing whatever happened.** An address nobody holds, one
 * belonging to somebody who was invited and never arrived, and one whose account
 * has been deactivated get the sentence an address with an account behind it
 * gets. Saying otherwise would turn a public form into a way of asking who is
 * here.
 *
 * That is also why there is nothing to report when the installation has no mail
 * configured: an answer about *that* would be an answer about this
 * installation, and the person who needs to know is the one running it.
 */
export function RecoveryScreen({ onDone }: { onDone: () => void }) {
  const [email, setEmail] = useState("");
  const [asked, setAsked] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [asking, setAsking] = useState(false);

  async function ask(event: FormEvent) {
    event.preventDefault();
    setRefusal(undefined);
    setAsking(true);

    try {
      const { response } = await api.POST("/recovery", { body: { email } });

      if (response.status === 204) {
        setAsked(true);
        return;
      }

      setRefusal(
        response.status === 429
          ? "Too many attempts. Wait a few minutes and try again."
          : "This installation refused that.",
      );
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setAsking(false);
    }
  }

  if (asked) {
    return (
      <Gate>
        <PageTitle>Check your mail</PageTitle>
        <p>
          If there is an account at that address, a link is on its way. It works once and
          expires in an hour.
        </p>
        <Button type="button" className="w-fit" onClick={onDone}>
          Back to signing in
        </Button>
      </Gate>
    );
  }

  return (
    <Gate>
      <div className="grid gap-3">
        <PageTitle>Get your password back</PageTitle>
        <p>Give the address you sign in with, and a link to set a new password follows.</p>
      </div>

      <form onSubmit={ask} className="grid max-w-md gap-3">
        <Field label="Email address">
          <Input
            type="email"
            name="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        </Field>

        {refusal !== undefined && <p className="refusal text-sm">{refusal}</p>}

        <Button type="submit" disabled={asking} className="mt-1 w-fit">
          Send the link
        </Button>

        <Button
          type="button"
          variant="link"
          size="sm"
          className="w-fit px-0"
          onClick={onDone}
        >
          Back to signing in
        </Button>
      </form>
    </Gate>
  );
}
