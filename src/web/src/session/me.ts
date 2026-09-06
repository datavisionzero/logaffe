import { useEffect, useState } from "react";
import { api } from "../api/client";

/** Who this browser is signed in as. */
export interface Me {
  id: string;
  name: string;
  email: string;
  /** Whether they administer the installation, which decides what is offered. */
  administrator: boolean;
}

/**
 * Reads it once per session.
 *
 * **The role decides what the interface offers and never what it may read.**
 * Every read narrows to the reach the installation resolves per request
 * (ADR 0055), so hiding a screen here is a courtesy rather than a boundary —
 * and a screen shown to somebody without the role would still be refused by the
 * installation.
 *
 * `undefined` while it is being asked, and `null` when the installation did not
 * answer — which behind the shell is the session having ended, and the sign-in
 * is already on its way in front of everything.
 */
export function useMe(): Me | undefined | null {
  const [me, setMe] = useState<Me | undefined | null>(undefined);

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const { data } = await api.GET("/me");

        if (current) {
          setMe(data ?? null);
        }
      } catch {
        if (current) {
          setMe(null);
        }
      }
    })();

    return () => {
      current = false;
    };
  }, []);

  return me;
}
