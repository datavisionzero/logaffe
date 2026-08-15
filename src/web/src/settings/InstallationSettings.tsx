import { AgentTokens } from "./AgentTokens";
import { BackupCodes } from "./BackupCodes";
import { ChangePassword } from "./ChangePassword";
import { SecondFactor } from "./SecondFactor";
import { Sessions } from "./Sessions";

/**
 * What is changed rarely about the installation itself.
 *
 * There is **no user management** here and there is nothing to add: one
 * operator, no invitations, no roles (`docs/ui.md`). There are also no
 * installation-wide defaults for a project's retention — a window is set per
 * project, up to a ceiling no installation can raise (ADR 0020) — and no host
 * recovery, no export and no backup button, because those are verbs on the
 * binary and are never reachable over the network (ADR 0013).
 *
 * What is left is the three things that are the installation's rather than a
 * project's: the browsers signed in, the tokens agents read with, and the
 * operator's own credentials.
 */
export function InstallationSettings() {
  return (
    <section className="narrow settings">
      <h1>Installation settings</h1>

      <Sessions />
      <AgentTokens />

      {/* The operator's own three credentials. Each of the acts below asks for
          the password again, which is what makes them the operator's rather
          than those of whoever is sitting at an unlocked browser. */}
      <p className="quiet">
        Each of the three below asks for your password again. There is no reset over the
        network and no email to send one to: what stands behind all of them is Host
        Recovery, on the machine this installation runs on.
      </p>

      <ChangePassword />
      <SecondFactor />
      <BackupCodes />
    </section>
  );
}
