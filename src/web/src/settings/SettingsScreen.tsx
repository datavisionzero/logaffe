import type { ReactNode } from "react";
import { Navigate, NavLink } from "react-router";

/**
 * One area of a settings screen: what it is called, where it is, and what is on
 * it.
 *
 * The first group of a screen is the one it opens at, and its address is the
 * screen's own — `/settings` rather than `/settings/browsers` — so that the
 * ordinary way in stays one address rather than a redirect to another.
 */
export interface SettingsGroup {
  /** The segment after the screen's address, and `null` for the first. */
  at: string | null;
  name: string;
  panel: ReactNode;
}

/**
 * A settings screen as its areas, one of them on the screen at a time.
 *
 * Both screens were a single column with every area under the last: five of them
 * on the installation's, and the answer to *where is the retention window* was
 * to read the page. An area is now a place — it has a name in the rail, it is
 * marked while it is being read, and it has an address, so a reload comes back
 * to it and the back button walks the areas just visited, which is what the rest
 * of this interface already does with a view.
 *
 * **Only the area being read is mounted**, which is the part that is not
 * decoration: every one of them asks the installation for something on the way
 * in — the sessions, the agent tokens, a project's ingest tokens — and the
 * stacked version asked for all of it whenever the screen was opened, most of it
 * for something nobody had looked at. `docs/ui.md` says the interface asks for
 * nothing unasked, and this is that rule applied to a screen that was quietly
 * breaking it.
 */
export function SettingsScreen({
  heading,
  at,
  section,
  groups,
}: {
  heading: string;
  /** The screen's own address, which the first group also answers to. */
  at: string;
  /** The segment the address carries, or `undefined` for the first group. */
  section: string | undefined;
  groups: SettingsGroup[];
}) {
  const first = groups[0];
  const open = section === undefined ? first : groups.find((group) => group.at === section);

  // An address naming an area that does not exist is answered by the screen's
  // own rather than by a screen saying so: there is nothing an operator could
  // have meant by it, and nothing to do about it but arrive.
  if (open === undefined || first === undefined) {
    return <Navigate to={at} replace />;
  }

  return (
    <section className="narrow settings">
      <h1>{heading}</h1>

      <div className="settings-body">
        <nav className="settings-rail" aria-label="Settings">
          {groups.map((group) => (
            <NavLink
              key={group.name}
              end={group.at === null}
              to={group.at === null ? at : `${at}/${group.at}`}
            >
              {group.name}
            </NavLink>
          ))}
        </nav>

        <div className="settings-panel">{open.panel}</div>
      </div>
    </section>
  );
}
