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

/**
 * One project as the list and the switcher both read it, with the contract's
 * numbers and instants already turned into what a screen shows.
 */
export interface HeldProject {
  id: string;
  name: string;
  retentionDays: number;
  createdAt: Date;
  /**
   * One ordinarily, two while the project is being rotated, and none for a
   * project whose door is closed — which is the reading the list is for.
   */
  ingestTokens: number;
  /** When it last received an entry, and `null` when it never has. */
  lastReceivedAt: Date | null;
}

export type ProjectsState =
  | { status: "asking" }
  | { status: "held"; projects: HeldProject[] }
  | { status: "unreachable" };

interface Held {
  state: ProjectsState;
  /** Asked for after an act that changed the list, and never on a timer. */
  reload: () => void;
}

const ProjectsContext = createContext<Held | undefined>(undefined);

/**
 * The projects, fetched once for the whole signed-in application.
 *
 * The list and the switcher are two readings of one answer, and the switcher is
 * present on every screen — so fetching this per screen would be the interface
 * asking for something unasked, which `docs/ui.md` refuses.
 */
export function ProjectsProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<ProjectsState>({ status: "asking" });
  const [asked, setAsked] = useState(0);

  const reload = useCallback(() => setAsked((n) => n + 1), []);

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const { data, response } = await api.GET("/projects");

        if (!current) {
          return;
        }

        if (data !== undefined) {
          setState({ status: "held", projects: data.map(held) });
          return;
        }

        // A `401` is not a failure this screen answers for: the session ended,
        // and the sign-in screen is already on its way in front of everything.
        // Anything else is an installation that did not answer.
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

  return <ProjectsContext.Provider value={value}>{children}</ProjectsContext.Provider>;
}

export function useProjects(): Held {
  const held = useContext(ProjectsContext);

  if (held === undefined) {
    throw new Error("The projects are only readable inside the signed-in application.");
  }

  return held;
}

function held(project: {
  id: string;
  name: string;
  retentionDays: number | string;
  createdAt: string;
  ingestTokens: number | string;
  lastReceivedAt: null | string;
}): HeldProject {
  return {
    id: project.id,
    name: project.name,
    retentionDays: asNumber(project.retentionDays),
    createdAt: asInstant(project.createdAt),
    ingestTokens: asNumber(project.ingestTokens),
    lastReceivedAt: project.lastReceivedAt === null ? null : asInstant(project.lastReceivedAt),
  };
}
