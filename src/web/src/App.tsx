import { useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router";
import { whenSignedOut } from "./api/client";
import { RecoveryScreen } from "./session/RecoveryScreen";
import { RedeemScreen, type Redemption } from "./session/RedeemScreen";
import { SignInScreen } from "./session/SignInScreen";
import { BootstrapScreen } from "./setup/BootstrapScreen";
import { FirstRun } from "./setup/FirstRun";
import { Shell } from "./shell/Shell";

/**
 * Where this browser is, which is what decides the screen.
 *
 * There is no state to ask the installation about. An installation with nobody
 * signed into it does not say so — the claim that used to announce it is gone
 * (ADR 0054) — so the application starts by showing the application, and the
 * first request it makes is what answers whether the session is one. A screen
 * that probed for it beforehand would be the interface asking for something
 * unasked, and on this product it would also be the one place a stranger could
 * learn whether anybody has ever signed in here.
 *
 * `bootstrapping` and `guiding` are reached deliberately and never by a probe:
 * the first from a link on the sign-in screen, held by whoever has the bootstrap
 * token; the second from finishing that exchange. Neither holds anything, which
 * is what keeps the first-run guide a guide rather than a stage
 * (`docs/setup.md`) — it cannot know it was skipped, and an installation
 * reloaded from there is simply one somebody is signed into.
 *
 * The three redemptions are decided by the address, because that is what a link
 * in a message points at (ADR 0053). They are checked before anything else: a
 * person redeeming an invitation has no account yet, and one recovering a
 * password cannot sign in, so neither of them can be behind the shell.
 */
type Where = "in" | "signed-out" | "bootstrapping" | "guiding" | "recovering";

/** The address each of the three links lands on. */
const REDEMPTIONS: Record<string, Redemption> = {
  "/invitation": "invitation",
  "/recovery": "recovery",
  "/address": "address",
};

export function App() {
  const location = useLocation();
  const navigate = useNavigate();
  const [where, setWhere] = useState<Where>("in");
  const [backupCodesRemaining, setBackupCodesRemaining] = useState<number | null>(null);

  // A fresh shell for each session, so that signing back in re-reads what the
  // installation holds rather than showing what the previous one saw.
  const [session, setSession] = useState(0);

  // The session ends in six ways that are not a sign-out — it expires, it hits
  // its thirty days, it is revoked from another browser, the password changes,
  // the second factor is re-enrolled, or the account is deactivated — and every
  // one of them shows up as the next request being refused. That is the one
  // signal this listens for.
  useEffect(() => whenSignedOut(() => setWhere("signed-out")), []);

  function begin() {
    setSession((n) => n + 1);
    setWhere("in");
  }

  /** Back to the sign-in, with the secret out of the address bar. */
  function leaveTheLink() {
    void navigate("/", { replace: true });
    setWhere("signed-out");
  }

  const redemption = REDEMPTIONS[location.pathname];

  if (redemption !== undefined) {
    return <RedeemScreen what={redemption} onDone={leaveTheLink} />;
  }

  switch (where) {
    case "signed-out":
      return (
        <SignInScreen
          onSignedIn={(remaining) => {
            setBackupCodesRemaining(remaining);
            begin();
          }}
          onSettingUp={() => setWhere("bootstrapping")}
          onRecovering={() => setWhere("recovering")}
        />
      );

    case "recovering":
      return <RecoveryScreen onDone={() => setWhere("signed-out")} />;

    case "bootstrapping":
      return (
        <BootstrapScreen
          onExchanged={() => setWhere("guiding")}
          onCancel={() => setWhere("signed-out")}
        />
      );

    case "guiding":
      return <FirstRun onDone={begin} />;

    case "in":
      return (
        <Shell
          key={session}
          backupCodesRemaining={backupCodesRemaining}
          onSignedOut={() => setWhere("signed-out")}
        />
      );
  }
}
