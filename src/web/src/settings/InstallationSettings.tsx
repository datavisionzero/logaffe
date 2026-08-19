import { useParams } from "react-router";
import { AgentTokens } from "./AgentTokens";
import { BackupCodes } from "./BackupCodes";
import { ChangePassword } from "./ChangePassword";
import { Groups } from "./Groups";
import { Hosts } from "./Hosts";
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
 * What is left is the five things that are the installation's rather than a
 * project's, and they are five areas because that is what they are: the browsers
 * signed in, the tokens agents read with, the operator's own credentials, the
 * groups the projects are listed under, and the machines they run on. Four of
 * them are lists of what exists and the credentials are three acts on one
 * account, which is why those stay together on one area rather than becoming
 * three.
 *
 * The groups and the hosts are here for the same reason the agent tokens are: a
 * group is a fact about the projects taken together and so is a host, and no
 * single project's screen can hold one (ADR 0039). A project's own settings say
 * which group it is in and which machine it runs on, which is all a project
 * knows about either.
 *
 * **A host is the one area with an address inside it.** It carries more than a
 * group does — a token, a collector command and a history of what the machine
 * was doing — so opening one is a screen rather than a row that unfolds, and it
 * is an address for the reason every area is one.
 */
export function InstallationSettings() {
  const { section, hostId } = useParams();

  return (
    <SettingsScreen
      heading="Installation settings"
      at="/settings"
      // A host's address matches a route of its own, which carries the host and
      // not the area — so the area it is inside is named here rather than read
      // off a segment that route never bound.
      section={hostId === undefined ? section : "hosts"}
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
        { at: "groups", name: "Groups", panel: <Groups /> },
        { at: "hosts", name: "Hosts", panel: <Hosts hostId={hostId} /> },
      ]}
    />
  );
}
