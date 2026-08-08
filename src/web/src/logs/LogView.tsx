import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router";
import type { HeldProject } from "../projects/projects";
import { CountPanel, ReadExpired } from "./CountPanel";
import { EmptyProject } from "./EmptyProject";
import { EntryDetail } from "./EntryDetail";
import { EntryList } from "./EntryList";
import { FilterBar } from "./FilterBar";
import { useEntries } from "./entries";
import {
  addressOf,
  filtersIn,
  keepsGrowing,
  namesOfSetFilters,
  NO_FILTERS,
  type Filters,
} from "./filters";

/**
 * The single screen on which the operator reads one project.
 *
 * The filters across the top, the entries below them, the detail of one beside
 * them. There is no separate search page, no "advanced" mode, and nothing that
 * opens in a new place — every narrowing happens in front of the list it
 * narrows.
 */
export function LogView({ project }: { project: HeldProject }) {
  const [params, setParams] = useSearchParams();
  const filters = filtersIn(params);

  const [selected, setSelected] = useState<number | null>(null);
  const [showingDetail, setShowingDetail] = useState(false);
  const [counting, setCounting] = useState(false);
  const [atTop, setAtTop] = useState(true);

  const scroller = useRef<HTMLDivElement | null>(null);
  const searchBox = useRef<HTMLInputElement | null>(null);

  const entries = useEntries(project.id, filters, atTop);

  const narrow = useCallback(
    (next: Filters) => {
      // Pushed rather than replaced, so that the back button walks the
      // narrowings just made — which is what makes the address the place a
      // filter set is kept.
      setParams(new URLSearchParams(addressOf(next).replace(/^\?/, "")));
    },
    [setParams],
  );

  // Up and down move through the entries, `Enter` opens the detail and
  // `Escape` closes it, and `/` puts the cursor in the search box. Scanning a
  // list is a keyboard task, and reaching for the mouse for every next line is
  // what makes a log viewer tiring to use.
  useEffect(() => {
    function press(event: KeyboardEvent) {
      const inABox =
        event.target instanceof HTMLElement &&
        ["INPUT", "SELECT", "TEXTAREA"].includes(event.target.tagName);

      if (event.key === "Escape") {
        setShowingDetail(false);
        if (inABox) {
          (event.target as HTMLElement).blur();
        }
        return;
      }

      if (inABox) {
        return;
      }

      if (event.key === "/") {
        event.preventDefault();
        searchBox.current?.focus();
        return;
      }

      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        setSelected((held) => step(entries.entries.map((entry) => entry.id), held, event.key));
        return;
      }

      if (event.key === "Enter" && selected !== null) {
        event.preventDefault();
        setShowingDetail(true);
      }
    }

    document.addEventListener("keydown", press);
    return () => document.removeEventListener("keydown", press);
  }, [entries.entries, selected]);

  // The selected row is kept in view as the keyboard walks it.
  useEffect(() => {
    if (selected === null) {
      return;
    }

    scroller.current
      ?.querySelector(`[data-id="${selected}"]`)
      ?.scrollIntoView({ block: "nearest" });
  }, [selected]);

  function returnToTheTop() {
    entries.showWaiting();
    scroller.current?.scrollTo({ top: 0 });
  }

  // A project nothing has ever delivered to is a different screen from a filter
  // set that matched nothing, and showing one where the other belongs is how an
  // operator concludes their integration is broken while the truth is that the
  // range is set to yesterday.
  if (project.lastReceivedAt === null) {
    return (
      <section className="logview">
        <h1>{project.name}</h1>
        <ToSettings projectId={project.id} />
        <EmptyProject projectId={project.id} />
      </section>
    );
  }

  return (
    <section className="logview">
      <FilterBar
        filters={filters}
        onChange={narrow}
        counting={counting}
        onCount={() => setCounting(!counting)}
        searchRef={searchBox}
      />

      {counting && (
        <CountPanel
          projectId={project.id}
          filters={filters}
          onNarrow={(next) => {
            narrow(next);
            setCounting(false);
          }}
          onClose={() => setCounting(false)}
        />
      )}

      <div className="logview-status">
        <span className="quiet">
          {keepsGrowing(filters)
            ? entries.following
              ? "Following"
              : "Paused — this tab is in the background"
            : "Not following — the range has an end in the past"}
        </span>

        {entries.behind && (
          <span className="notice">
            Entries are arriving faster than this view is asking for them. Nothing is lost;
            the next poll resumes where this one stopped.
          </span>
        )}

        {entries.waiting > 0 && (
          <button type="button" onClick={returnToTheTop}>
            {entries.waiting} new {entries.waiting === 1 ? "entry" : "entries"} — back to the
            top
          </button>
        )}

        <ToSettings projectId={project.id} />
      </div>

      <div className="logview-body">
        <div
          className="logview-entries"
          ref={scroller}
          onScroll={(event) => setAtTop(event.currentTarget.scrollTop < 8)}
        >
          {entries.reading.status === "asking" && <p className="quiet">Reading…</p>}

          {entries.reading.status === "unreachable" && (
            <p className="refusal">This installation did not answer.</p>
          )}

          {entries.reading.status === "expired" && (
            <ReadExpired narrow={entries.reading.narrow} />
          )}

          {entries.reading.status === "answered" && entries.entries.length === 0 && (
            <NothingMatched filters={filters} onClear={() => narrow(NO_FILTERS)} />
          )}

          {entries.entries.length > 0 && (
            <>
              <EntryList
                entries={entries.entries}
                selected={selected}
                justArrived={entries.justArrived}
                onSelect={(id) => {
                  setSelected(id);
                  setShowingDetail(true);
                }}
                onNarrowToLogger={(loggerName) => narrow({ ...filters, loggerName })}
              />

              {/* Not infinite scroll. The tail inserts entries at the top of
                  this same list, and a list that grows at both ends while a
                  person reads is where scroll position stops being trustworthy
                  — and there is no total to scroll against anyway. */}
              {entries.older !== null && (
                <button type="button" disabled={entries.loadingOlder} onClick={entries.loadOlder}>
                  {entries.loadingOlder ? "Loading…" : "Load older entries"}
                </button>
              )}
            </>
          )}
        </div>

        {showingDetail && selected !== null && (
          <EntryDetail
            projectId={project.id}
            entryId={selected}
            filters={filters}
            onNarrow={narrow}
            onClose={() => setShowingDetail(false)}
          />
        )}
      </div>
    </section>
  );
}

/**
 * The way into what is changed rarely about this project: its name, its
 * retention window, its ingest tokens and its end.
 *
 * It sits on the screen the operator is already on rather than beside the
 * project on the list, because settings are reached from the thing they are
 * about — and because the switcher is what moves between projects.
 */
function ToSettings({ projectId }: { projectId: string }) {
  return (
    <Link className="to-settings" to={`/project/${projectId}/settings`}>
      Project settings
    </Link>
  );
}

/**
 * A filter set that matches nothing says that, names the filters responsible,
 * and offers to clear them.
 */
function NothingMatched({ filters, onClear }: { filters: Filters; onClear: () => void }) {
  return (
    <div className="nothing-matched">
      <p>No entries match these filters:</p>
      <ul>
        {namesOfSetFilters(filters).map((name) => (
          <li key={name}>{name}</li>
        ))}
      </ul>
      <p className="quiet">
        This project has received entries — these narrowings are what is leaving none.
      </p>
      <button type="button" onClick={onClear}>
        Clear the filters
      </button>
    </div>
  );
}

function step(ids: number[], selected: number | null, key: string): number | null {
  if (ids.length === 0) {
    return null;
  }

  const at = selected === null ? -1 : ids.indexOf(selected);
  const next = key === "ArrowDown" ? at + 1 : at - 1;

  return ids[Math.min(Math.max(next, 0), ids.length - 1)] ?? null;
}
