import { useEffect, useState } from "react";
import { api, whenSignedOut } from "./api/client";
import { ClaimScreen, WindowClosed } from "./claim/ClaimScreen";
import { FirstRun } from "./claim/FirstRun";
import { SignInScreen } from "./session/SignInScreen";
import { Shell } from "./shell/Shell";

/**
 * What this installation is, which is what decides the first screen.
 *
 * There are only two: an installation nobody owns shows the claim and nothing
 * else, and a claimed one shows the operator's application. Whether the
 * operator is signed in is not asked here — the first request the application
 * makes answers it, and a screen that probed for it beforehand would be the
 * interface asking for something unasked.
 *
 * `guiding` is not a third: it is the claim that just completed, still on
 * screen. Nothing reaches it except finishing a claim in this browser, which is
 * what keeps the first-run guide a guide rather than a stage (`docs/setup.md`) —
 * it holds no state, so it cannot know it was skipped, and an installation
 * reloaded from here is simply a claimed one.
 */
type Reached =
  | { at: "asking" }
  | { at: "unreachable" }
  | { at: "unclaimed"; windowIsOpen: boolean; closesAt: string | null }
  | { at: "guiding" }
  | { at: "claimed" };

export function App() {
  const [reached, setReached] = useState<Reached>({ at: "asking" });

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const { data } = await api.GET("/claim");

        if (!current) {
          return;
        }

        setReached(
          data === undefined
            ? { at: "unreachable" }
            : data.isClaimed
              ? { at: "claimed" }
              : {
                  at: "unclaimed",
                  windowIsOpen: data.windowIsOpen,
                  closesAt: data.closesAt,
                },
        );
      } catch {
        if (current) {
          setReached({ at: "unreachable" });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, []);

  switch (reached.at) {
    case "asking":
      return null;

    case "unreachable":
      return (
        <main>
          <h1>logaffe</h1>
          <p className="refusal">This installation did not answer. Reload to try again.</p>
        </main>
      );

    case "unclaimed":
      return reached.windowIsOpen ? (
        <ClaimScreen
          closesAt={reached.closesAt}
          onClaimed={() => setReached({ at: "guiding" })}
        />
      ) : (
        <WindowClosed />
      );

    case "guiding":
      return <FirstRun onDone={() => setReached({ at: "claimed" })} />;

    case "claimed":
      return <Installation />;
  }
}

/**
 * A claimed installation, in front of which the sign-in stands whenever the
 * session is not one.
 *
 * The session ends in five ways that are not a sign-out — it expires, it is
 * revoked from another browser, the password changes, the second factor is
 * re-enrolled, or Host Recovery removes the account — and every one of them
 * shows up as the next request being refused. That is the one signal this
 * listens for.
 */
function Installation() {
  const [signedOut, setSignedOut] = useState(false);
  const [backupCodesRemaining, setBackupCodesRemaining] = useState<number | null>(null);

  // A fresh shell for each session, so that signing back in re-reads what the
  // installation holds rather than showing what the previous one saw.
  const [session, setSession] = useState(0);

  useEffect(() => whenSignedOut(() => setSignedOut(true)), []);

  if (signedOut) {
    return (
      <SignInScreen
        onSignedIn={(remaining) => {
          setBackupCodesRemaining(remaining);
          setSession((n) => n + 1);
          setSignedOut(false);
        }}
      />
    );
  }

  return (
    <Shell
      key={session}
      backupCodesRemaining={backupCodesRemaining}
      onSignedOut={() => setSignedOut(true)}
    />
  );
}
