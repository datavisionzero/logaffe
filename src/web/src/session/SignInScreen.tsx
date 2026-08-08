import { useState, type FormEvent } from "react";
import { api, asNumber } from "../api/client";

/**
 * How the operator gets back in, from whatever machine they happen to be at.
 *
 * There is nothing to select and nothing naming which account is meant: an
 * installation has exactly one operator, with no username and no email address
 * (ADR 0015), so a password and one second factor is the whole of it. Nothing
 * is bound to this browser, and nothing has to be prepared on a machine being
 * used for the first time.
 */
export function SignInScreen({
  onSignedIn,
}: {
  /** How many backup codes are left, when one was spent getting in. */
  onSignedIn: (backupCodesRemaining: number | null) => void;
}) {
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

      // One refusal for every way of not getting in — a wrong password, a
      // wrong code, a code already spent — because which of them it was is not
      // something this surface hands over.
      setRefusal(
        response.status === 429
          ? "Too many attempts from here. Wait a moment and try again; the account is never locked."
          : "That did not sign you in.",
      );
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setSigningIn(false);
    }
  }

  return (
    <main>
      <h1>logaffe</h1>

      <form onSubmit={signIn}>
        <label>
          Password
          <input
            type="password"
            name="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </label>

        {usingBackupCode ? (
          <label>
            Backup code
            <input
              name="backup-code"
              autoComplete="off"
              value={backupCode}
              onChange={(e) => setBackupCode(e.target.value)}
            />
          </label>
        ) : (
          <label>
            The six digits from the app
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
            <input
              id="totp"
              name="totp"
              inputMode="numeric"
              maxLength={6}
              autoComplete="one-time-code"
              value={secondFactorCode}
              onChange={(e) => setSecondFactorCode(e.target.value)}
            />
          </label>
        )}

        {refusal !== undefined && <p className="refusal">{refusal}</p>}

        <button type="submit" disabled={signingIn}>
          Sign in
        </button>

        {/* A backup code stands in for the second factor and is consumed when
            used. It is the ordinary way in when the phone is not to hand, so it
            is one click away rather than something to be found. */}
        <button
          type="button"
          className="plain"
          onClick={() => {
            setUsingBackupCode(!usingBackupCode);
            setRefusal(undefined);
          }}
        >
          {usingBackupCode ? "Use the authenticator app" : "Use a backup code instead"}
        </button>
      </form>
    </main>
  );
}
