import { useState, type FormEvent } from "react";
import { api, asNumber } from "../api/client";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { Field } from "../components/Field";
import { Gate, PageTitle } from "../components/Page";

/**
 * How somebody gets back in, from whatever machine they happen to be at.
 *
 * An address, a password, and the second factor if they enrolled one — all in
 * one request. Nothing is bound to this browser, and nothing has to be prepared
 * on a machine being used for the first time.
 *
 * **The code is left empty by an account that has none** (ADR 0041), and the
 * form says so rather than asking in two stages. Asking for the address and
 * password first and the code afterwards would read better and would tell an
 * attacker when they had found the password, which is the one thing this screen
 * refuses to say: every way of not getting in is one refusal, in one wording and
 * one time class (ADR 0056).
 */
export function SignInScreen({
  onSignedIn,
  onSettingUp,
  onRecovering,
}: {
  /** How many backup codes are left, when one was spent getting in. */
  onSignedIn: (backupCodesRemaining: number | null) => void;

  /** Somebody with the bootstrap token in hand, setting this installation up. */
  onSettingUp: () => void;

  /** Somebody who cannot remember their password. */
  onRecovering: () => void;
}) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [secondFactorCode, setSecondFactorCode] = useState("");
  const [backupCode, setBackupCode] = useState("");
  const [usingBackupCode, setUsingBackupCode] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [signingIn, setSigningIn] = useState(false);

  async function signIn(event: FormEvent) {
    event.preventDefault();
    setRefusal(undefined);
    setSigningIn(true);

    try {
      const { data, response } = await api.POST("/sign-in", {
        body: {
          email,
          password,
          secondFactorCode: usingBackupCode ? null : secondFactorCode,
          backupCode: usingBackupCode ? backupCode : null,
        },
      });

      if (data !== undefined) {
        onSignedIn(
          data.backupCodesRemaining === null || data.backupCodesRemaining === undefined
            ? null
            : asNumber(data.backupCodesRemaining),
        );
        return;
      }

      // One refusal for every way of not getting in — an address nobody
      // holds, a wrong password, a wrong code, a code already spent, an account
      // that has been deactivated — because which of them it was is not
      // something this surface hands over.
      setRefusal(
        response.status === 429
          ? "Too many attempts. Wait a few minutes and try again; nothing is locked, and the count clears on its own."
          : "That did not sign you in.",
      );
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setSigningIn(false);
    }
  }

  return (
    <Gate>
      <div className="flex items-center gap-2">
        <span aria-hidden className="size-5 rounded-sm bg-brand" />
        <PageTitle>logaffe</PageTitle>
      </div>

      <form onSubmit={signIn} className="grid max-w-md gap-3">
        <Field label="Email address">
          <Input
            type="email"
            name="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        </Field>

        <Field label="Password">
          <Input
            type="password"
            name="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </Field>

        {usingBackupCode ? (
          <Field label="Backup code">
            <Input
              name="backup-code"
              autoComplete="off"
              value={backupCode}
              onChange={(e) => setBackupCode(e.target.value)}
            />
          </Field>
        ) : (
          <Field label="The six digits from the app, if you enrolled one">
            {/* `autocomplete` is the standard's answer and browsers honour it,
                but password managers find this field by their own heuristics,
                and those read `name` and `id` before anything else.

                The name is `totp` and not the standard's `one-time-code`,
                which reads like the tidier choice and was measured to be the
                broken one: a field named `one-time-code` is not offered a code,
                with or without a password field beside it, while `totp` is.
                `totp` is also what ADR 0016 and `Rfc6238SecondFactor` already
                call the mechanism, and unlike `otp` and `2fa` it is not a term
                `CONTEXT.md` tells us to avoid. Tidying this back to match the
                attribute below it would silently cost the operator the fill. */}
            <Input
              id="totp"
              name="totp"
              inputMode="numeric"
              maxLength={6}
              autoComplete="one-time-code"
              value={secondFactorCode}
              onChange={(e) => setSecondFactorCode(e.target.value)}
            />
          </Field>
        )}

        {refusal !== undefined && <p className="refusal text-sm">{refusal}</p>}

        <Button type="submit" disabled={signingIn} className="mt-1 w-fit">
          Sign in
        </Button>

        {/* A link to an address rather than an answer here. What this screen
            must not become is a way of asking who has an account on this
            installation (ADR 0053). */}
        <Button
          type="button"
          variant="link"
          size="sm"
          className="w-fit px-0"
          onClick={onRecovering}
        >
          Forgotten your password?
        </Button>

        {/* A backup code stands in for the second factor and is consumed when
            used. It is the ordinary way in when the phone is not to hand, so it
            is one click away rather than something to be found. */}
        <Button
          type="button"
          variant="link"
          size="sm"
          className="w-fit px-0"
          onClick={() => {
            setUsingBackupCode(!usingBackupCode);
            setRefusal(undefined);
          }}
        >
          {usingBackupCode ? "Use the authenticator app" : "Use a backup code instead"}
        </Button>

        {/* The way in for whoever is holding the bootstrap token, and the only
            thing on this screen that is not a sign-in. It is a link rather than
            something the application decided to show, because an installation
            does not announce whether anybody has signed into it yet
            (ADR 0054). */}
        <Button
          type="button"
          variant="link"
          size="sm"
          className="w-fit px-0"
          onClick={onSettingUp}
        >
          Setting this installation up for the first time?
        </Button>
      </form>
    </Gate>
  );
}
