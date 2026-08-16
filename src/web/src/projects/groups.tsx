import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { api, asInstant, asNumber } from "../api/client";
import type { HeldProject } from "./projects";

/**
 * One group as the list, the switcher and the settings area all read it.
 */
export interface HeldGroup {
  id: string;
  name: string;
  createdAt: Date;
  /**
   * How many projects it holds. Zero is an ordinary answer — a group made
   * before its first project, or left behind by its last — and it is what the
   * settings area says before removing one.
   */
  projects: number;
}

export type GroupsState =
  | { status: "asking" }
  | { status: "held"; groups: HeldGroup[] }
  | { status: "unreachable" };

interface Held {
  state: GroupsState;
  /** Asked for after an act that changed the list, and never on a timer. */
  reload: () => void;
}

const GroupsContext = createContext<Held | undefined>(undefined);

/**
 * The groups, fetched once for the whole signed-in application.
 *
 * The list, the switcher and the settings area are three readings of one
 * answer, exactly as the projects are — so this sits beside `ProjectsProvider`
 * rather than being asked for per screen.
 *
 * **It is a second request and not a field on the project rows.** A project
 * carries the identity of its group and nothing more, which is what lets a group
 * holding no projects be shown at all: it is something the operator made, and a
 * list assembled from what the projects say would quietly omit it
 * (`docs/ui.md`).
 */
export function GroupsProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<GroupsState>({ status: "asking" });
  const [asked, setAsked] = useState(0);

  const reload = useCallback(() => setAsked((n) => n + 1), []);

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const { data, response } = await api.GET("/groups");

        if (!current) {
          return;
        }

        if (data !== undefined) {
          setState({ status: "held", groups: data.map(held) });
          return;
        }

        // A `401` is not a failure this answers for: the session ended, and the
        // sign-in screen is already on its way in front of everything.
        if (response.status !== 401) {
          setState({ status: "unreachable" });
        }
      } catch {
        if (current) {
          setState({ status: "unreachable" });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, [asked]);

  const value = useMemo(() => ({ state, reload }), [state, reload]);

  return <GroupsContext.Provider value={value}>{children}</GroupsContext.Provider>;
}

export function useGroups(): Held {
  const held = useContext(GroupsContext);

  if (held === undefined) {
    throw new Error("The groups are only readable inside the signed-in application.");
  }

  return held;
}

/**
 * The projects arranged the way both the list and the switcher show them:
 * everything in no group first and with no heading over it, then the groups in
 * the order of their names.
 *
 * A group with nothing in it keeps its place in this — the interface says so in
 * a sentence rather than leaving it out — which is why this takes the groups
 * rather than deriving them from what the projects point at.
 */
export interface UnderAGroup {
  group: HeldGroup;
  projects: HeldProject[];
}

export function arrangedByGroup(
  projects: HeldProject[],
  groups: HeldGroup[],
): { ungrouped: HeldProject[]; grouped: UnderAGroup[] } {
  return {
    ungrouped: projects.filter((project) => project.groupId === null),
    grouped: [...groups]
      .sort((one, other) => one.name.localeCompare(other.name))
      .map((group) => ({
        group,
        projects: projects.filter((project) => project.groupId === group.id),
      })),
  };
}

/** The name of the group a project is in, or `null` for one in none. */
export function groupOf(project: HeldProject, groups: GroupsState): string | null {
  if (project.groupId === null || groups.status !== "held") {
    return null;
  }

  return groups.groups.find((group) => group.id === project.groupId)?.name ?? null;
}

function held(group: {
  id: string;
  name: string;
  createdAt: string;
  projects: number | string;
}): HeldGroup {
  return {
    id: group.id,
    name: group.name,
    createdAt: asInstant(group.createdAt),
    projects: asNumber(group.projects),
  };
}
