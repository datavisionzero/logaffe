import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { Field } from "../components/Field";
import { About, Area, Said } from "./Area";

/**
 * Moving this account to another address (ADR 0053).
 *
 * **Nothing changes until the link is used.** It goes to the new address —
 * what it proves is that somebody reads mail there — and until it is redeemed
 * the old address is still the one that signs in. A change asked for and never
 * confirmed costs nothing.
 *
 * **It asks for the password**, like everything else in this panel: an unlocked
 * browser must not be able to move an account somewhere its owner cannot read.
 */
export function ChangeAddress() {
  const [password, setPassword] = useState("");
  const [email, setEmail] = useState("");
  const [problems, setProblems] = useState<{ password?: string; email?: string }>({});
  const [refusal, setRefusal] = useState<string>();
  const [note, setNote] = useState<string>();
  const [sending, setSending] = useState(false);

  async function send(event: FormEvent) {
    event.preventDefault();
    setProblems({});
    setRefusal(undefined);
    setNote(undefined);
    setSending(true);

    try {
      const { response, error } = await api.POST("/address", {
        body: { password, email },
      });

      if (response.status === 204) {
        setNote(
          `A link is on its way to ${email}. Until it is used, your current address is `
            + "still the one that signs in.",
        );
        setPassword("");
        setEmail("");
        return;
      }

      if (response.status === 400) {
        setProblems({
          password: problemWith(error, "password"),
          email: problemWith(error, "email"),
        });
        return;
      }

      setRefusal(
        response.status === 409
          ? "Somebody holds that address, or is already moving to it."
          : response.status === 503
            ? "This installation has no SMTP configured, so it cannot send the link."
            : "This installation refused that.",
      );
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setSending(false);
    }
  }

  return (
    <Area title="Your email address">
      <About>
        The address you sign in with. Changing it sends a link to the new one, and nothing
        changes until you use it.
      </About>

      <form onSubmit={send} className="grid max-w-md gap-3">
        <Field label="Your password" said={problems.password}>
          <Input
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={problems.password !== undefined || undefined}
          />
        </Field>

        <Field label="The new address" said={problems.email}>
          <Input
            type="email"
            autoComplete="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aria-invalid={problems.email !== undefined || undefined}
          />
        </Field>

        <Said problem={refusal} note={note} />

        <Button type="submit" disabled={sending} className="w-fit">
          Send the link
        </Button>
      </form>
    </Area>
  );
}
