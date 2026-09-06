import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { Field } from "../components/Field";
import { Gate, PageTitle } from "../components/Page";
import { PASSWORD_MINIMUM } from "../session/password";

/**
 * The one act the bootstrap token buys: the first administrator's password, and
 * a session to go on with (ADR 0054).
 *
 * **There is no first-run screen and this is not one.** The administrator was
 * created by the installation on its first start, out of the configuration
 * whoever installed it wrote — this screen creates nobody. What it does is take
 * the value from that same configuration and exchange it, once, for a password
 * that never travelled through a compose file.
 *
 * **It is reached deliberately**, from a link on the sign-in screen rather than
 * by the application probing for it. An installation does not announce whether
 * anybody has signed into it yet, and a screen that asked would be the one place
 * a stranger could learn it.
 */
export function BootstrapScreen({
  onExchanged,
  onCancel,
}: {
  onExchanged: () => void;
  onCancel: () => void;
}) {
  const [token, setToken] = useState("");
  const [password, setPassword] = useState("");
  const [again, setAgain] = useState("");
  const [problems, setProblems] = useState<{ token?: string; password?: string }>({});
  const [refusal, setRefusal] = useState<string>();
  const [exchanging, setExchanging] = useState(false);

  const tooShort = password.length > 0 && password.length < PASSWORD_MINIMUM;
  const mismatched = again.length > 0 && again !== password;

  async function exchange(event: FormEvent) {
    event.preventDefault();
    setRefusal(undefined);
    setProblems({});

    if (password.length < PASSWORD_MINIMUM || again !== password) {
      setRefusal(`Give a password of at least ${PASSWORD_MINIMUM} characters, twice.`);
      return;
    }

    setExchanging(true);

    try {
      const { response, error } = await api.POST("/bootstrap", {
        body: { token, password },
      });

      if (response.status === 204) {
        // The exchange signed them in; there is nothing to say and nowhere to
        // point but the installation itself.
        onExchanged();
        return;
      }

      if (response.status === 400) {
        // Every refusal here says which box was wrong. The person on the other
        // end is setting up their own installation with the compose file open
        // in another window, and what this says about the token is only whether
        // the one presented was right.
        setProblems({
          token: problemWith(error, "token"),
          password: problemWith(error, "password"),
        });
        return;
      }

      setRefusal(refusalFor(response.status));
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setExchanging(false);
    }
  }

  return (
    <Gate>
      <div className="grid gap-3">
        <PageTitle>Set the first password</PageTitle>
        <p>
          This installation created its first administrator on its first start, from the
          three <code className="font-mono">Logaffe__Bootstrap__*</code> values in its
          configuration. Exchange the token there for a password, once.
        </p>
      </div>

      <form onSubmit={exchange} className="grid max-w-md gap-3">
        <Field
          label="Bootstrap token"
          said={problems.token}
          hint={
            <>
              The value of <code className="font-mono">Logaffe__Bootstrap__Token</code> in
              the compose file. Whoever installed this has it.
            </>
          }
        >
          <Input
            name="bootstrap-token"
            autoComplete="off"
            value={token}
            onChange={(e) => setToken(e.target.value)}
            aria-invalid={problems.token !== undefined || undefined}
          />
        </Field>

        <Field
          label="Password"
          hint={`At least ${PASSWORD_MINIMUM} characters, and nothing else is asked of it. Length is the property that matters — and until you enrol a second factor afterwards, it is the only credential on the account.`}
          said={
            tooShort ? `A password is at least ${PASSWORD_MINIMUM} characters.` : problems.password
          }
        >
          <Input
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={tooShort || problems.password !== undefined || undefined}
          />
        </Field>

        {/* Typed twice, which is furniture the product avoids everywhere else
            and earns here: the account being given this password is the one an
            installation is recovered through, and a typo at this step costs the
            token a second exchange it will not offer. */}
        <Field
          label="Password again"
          said={mismatched ? "These two are not the same." : undefined}
        >
          <Input
            type="password"
            autoComplete="new-password"
            value={again}
            onChange={(e) => setAgain(e.target.value)}
            aria-invalid={mismatched || undefined}
          />
        </Field>

        {refusal !== undefined && <p className="refusal text-sm">{refusal}</p>}

        <Button type="submit" disabled={exchanging} className="mt-1 w-fit">
          Set the password
        </Button>

        <Button
          type="button"
          variant="link"
          size="sm"
          className="w-fit px-0"
          onClick={onCancel}
        >
          Back to signing in
        </Button>
      </form>
    </Gate>
  );
}

/**
 * The refusals that are not about a box on the form.
 */
function refusalFor(status: number): string {
  switch (status) {
    case 409:
      return "There is nothing to exchange here: this installation's first administrator already has a password. Sign in instead, or recover it from the host.";
    case 429:
      return "Too many attempts from here. Wait a moment and try again.";
    default:
      return "This installation refused the exchange.";
  }
}
