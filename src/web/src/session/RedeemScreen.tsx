import { useState, type FormEvent } from "react";
import { useSearchParams } from "react-router";
import { api, problemWith } from "../api/client";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { Field } from "../components/Field";
import { Gate, PageTitle } from "../components/Page";
import { PASSWORD_MINIMUM } from "./password";

/** Which of the three links landed here. */
export type Redemption = "invitation" | "recovery" | "address";

const WHAT: Record<
  Redemption,
  { title: string; opening: string; action: string; path: string; done: string }
> = {
  invitation: {
    title: "Set your password",
    opening:
      "Somebody invited you to this installation. Choose a password and you are in.",
    action: "Set the password",
    path: "/invitation/redemption",
    done: "Your password is set. Sign in with it.",
  },
  recovery: {
    title: "Set a new password",
    opening:
      "Choose a new password. It ends every session this account has, everywhere.",
    action: "Set the password",
    path: "/recovery/redemption",
    done: "Your password is set. Sign in with it.",
  },
  address: {
    title: "Confirm this address",
    opening:
      "Confirming moves the account to this address, and it becomes the one that signs in.",
    action: "Confirm the address",
    path: "/address/redemption",
    done: "This address is now the one that signs in.",
  },
};

/**
 * Where the three links land (ADR 0053).
 *
 * **The secret is in the address bar and nowhere else.** It is not stored, not
 * put into a cookie and not remembered: it exists for as long as this screen is
 * open, and the request that spends it is the only thing that ever reads it.
 *
 * **A link that opens nothing says one thing.** Used already, expired, replaced
 * by a newer one, or never a link at all — the installation does not tell those
 * apart and neither does this screen, because the person who is holding the
 * value it does not recognize has no business learning which.
 */
export function RedeemScreen({
  what,
  onDone,
}: {
  what: Redemption;
  /** Back to the sign-in, which is where all three of these end. */
  onDone: () => void;
}) {
  const [parameters] = useSearchParams();
  const secret = parameters.get("secret");

  const [password, setPassword] = useState("");
  const [again, setAgain] = useState("");
  const [problems, setProblems] = useState<{ secret?: string; password?: string }>({});
  const [refusal, setRefusal] = useState<string>();
  const [redeeming, setRedeeming] = useState(false);
  const [redeemed, setRedeemed] = useState(false);

  const wants = WHAT[what];
  const needsPassword = what !== "address";
  const tooShort = password.length > 0 && password.length < PASSWORD_MINIMUM;
  const mismatched = again.length > 0 && again !== password;

  async function redeem(event: FormEvent) {
    event.preventDefault();
    setRefusal(undefined);
    setProblems({});

    if (needsPassword && (password.length < PASSWORD_MINIMUM || again !== password)) {
      setRefusal(`Give a password of at least ${PASSWORD_MINIMUM} characters, twice.`);
      return;
    }

    setRedeeming(true);

    try {
      const { response, error } = await api.POST(wants.path as "/invitation/redemption", {
        body: { secret, password: needsPassword ? password : null },
      });

      if (response.status === 204) {
        setRedeemed(true);
        return;
      }

      if (response.status === 409) {
        setRefusal("Somebody took that address while this link was outstanding.");
        return;
      }

      if (response.status === 400) {
        setProblems({
          secret: problemWith(error, "secret"),
          password: problemWith(error, "password"),
        });
        return;
      }

      setRefusal(
        response.status === 429
          ? "Too many attempts. Wait a few minutes and try again."
          : "This installation refused the link.",
      );
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setRedeeming(false);
    }
  }

  if (redeemed) {
    return (
      <Gate>
        <PageTitle>{wants.title}</PageTitle>
        <p>{wants.done}</p>
        <Button type="button" className="w-fit" onClick={onDone}>
          Sign in
        </Button>
      </Gate>
    );
  }

  return (
    <Gate>
      <div className="grid gap-3">
        <PageTitle>{wants.title}</PageTitle>
        <p>{wants.opening}</p>
      </div>

      <form onSubmit={redeem} className="grid max-w-md gap-3">
        {needsPassword && (
          <>
            <Field
              label="Password"
              hint={`At least ${PASSWORD_MINIMUM} characters, and nothing else is asked of it.`}
              said={
                tooShort
                  ? `A password is at least ${PASSWORD_MINIMUM} characters.`
                  : problems.password
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
          </>
        )}

        {problems.secret !== undefined && (
          <p className="refusal text-sm">{problems.secret}</p>
        )}
        {refusal !== undefined && <p className="refusal text-sm">{refusal}</p>}

        <Button type="submit" disabled={redeeming} className="mt-1 w-fit">
          {wants.action}
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
