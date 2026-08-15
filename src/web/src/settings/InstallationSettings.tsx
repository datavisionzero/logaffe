import { useParams } from "react-router";
import { AgentTokens } from "./AgentTokens";
import { BackupCodes } from "./BackupCodes";
import { ChangePassword } from "./ChangePassword";
import { SecondFactor } from "./SecondFactor";
import { Sessions } from "./Sessions";
import { SettingsScreen } from "./SettingsScreen";

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
 * project's, and they are three areas because that is what they are: the
 * browsers signed in, the tokens agents read with, and the operator's own
 * credentials. The first two are lists of what exists and the third is three
 * acts on one account, which is why the three credentials stay together on one
 * area rather than becoming three.
 */
export function InstallationSettings() {
  const { section } = useParams();

  return (
    <SettingsScreen
      heading="Installation settings"
      at="/settings"
      section={section}
      groups={[
        { at: null, name: "Signed-in browsers", panel: <Sessions /> },
        { at: "agents", name: "Agent tokens", panel: <AgentTokens /> },
        {
          at: "credentials",
          name: "Your credentials",
          panel: (
            <>
              {/* Each of the acts below asks for the password again, which is
                  what makes them the operator's rather than those of whoever is
                  sitting at an unlocked browser. */}
              <p className="quiet">
                Each of the three below asks for your password again. There is no reset over
                the network and no email to send one to: what stands behind all of them is
                Host Recovery, on the machine this installation runs on.
              </p>

              <ChangePassword />
              <SecondFactor />
              <BackupCodes />
            </>
          ),
        },
      ]}
    />
  );
}
