import { useParams } from "react-router";
import { AgentTokens } from "./AgentTokens";
import { Alerts } from "./Alerts";
import { BackupCodes } from "./BackupCodes";
import { ChangePassword } from "./ChangePassword";
import { Groups } from "./Groups";
import { History } from "./History";
import { Hosts } from "./Hosts";
import { ChangeAddress } from "./ChangeAddress";
import { SecondFactor } from "./SecondFactor";
import { Users } from "./Users";
import { useMe } from "../session/me";
import { Sessions } from "./Sessions";
import { SettingsScreen } from "./SettingsScreen";

/**
 * What is changed rarely about the installation itself.
 *
 * There are **no installation-wide defaults for a project's retention** — a
 * window is set per project, up to a ceiling no installation can raise
 * (ADR 0020) — and no host recovery, no export and no backup button, because
 * those are verbs on the binary and are never reachable over the network
 * (ADR 0013).
 *
 * What is left is the things that are the installation's rather than a
 * project's, and they are areas because that is what they are: the browsers
 * signed in, the tokens agents connect with, this account's own credentials, the
 * people with accounts here, what any of them has changed, the groups the
 * projects are listed under, the machines they run on, and what this
 * installation says unasked. Most are lists of what exists and the credentials
 * are three acts on one account, which is why those stay together on one area
 * rather than becoming three.
 *
 * **The alerts area is the last of them and the smallest**: a notifier, three
 * switches and what each of them currently works out to. It is not a
 * notification bell and it is not an alert list — an alert leaves the
 * installation and reaches a phone, so there is nothing here to mark as read
 * (`docs/ui.md`).
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
const EVERYBODY = ["agents", "credentials", "groups", "hosts", "alerts"];

/**
 * The settings of the installation itself, as areas.
 */
export function InstallationSettings() {
  const { section, hostId } = useParams();
  const me = useMe();

  // One area is only an administrator's, and who is reading takes a request to
  // find out. The screen therefore waits for the answer **only when the address
  // names an area it does not otherwise know** — which is the case that would
  // otherwise bounce somebody who opened /settings/people directly straight
  // back to the first area. Every other address draws on the first frame, the
  // way it always did.
  if (me === undefined && section !== undefined && !EVERYBODY.includes(section)) {
    return null;
  }

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
                  what makes them this person's rather than those of whoever is
                  sitting at an unlocked browser. */}
              <p className="quiet">
                Each of the three below asks for your password again. They are yours alone:
                nobody else on this installation can enrol your second factor, print your
                backup codes or change your password, and no administrator can either.
              </p>

              <ChangePassword />
              <ChangeAddress />
              <SecondFactor />
              <BackupCodes />
            </>
          ),
        },
        // Offered only to an administrator, which is a courtesy rather than a
        // boundary: the installation refuses the acts either way, and a tab that
        // does nothing but refuse is a tab nobody should be shown (ADR 0055).
        // Offered only to an administrator for the same reason People is, and
        // the two sit together: what somebody may do and what they have done
        // are one question asked twice.
        ...(me?.administrator === true
          ? [
              { at: "people", name: "People", panel: <Users me={me.id} /> },
              { at: "history", name: "History", panel: <History /> },
            ]
          : []),
        { at: "groups", name: "Groups", panel: <Groups /> },
        { at: "hosts", name: "Hosts", panel: <Hosts hostId={hostId} /> },
        { at: "alerts", name: "Alerts", panel: <Alerts /> },
      ]}
    />
  );
}
