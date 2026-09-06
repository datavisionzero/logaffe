import { useEffect } from "react";

const COLOR_SCHEME_QUERY = "(prefers-color-scheme: dark)";

/**
 * Puts the class the token layer is written against on `<html>`, and takes it
 * off again — nothing else.
 *
 * planaffe's provider of the same name carries a three-way setting and
 * remembers it. Only the mechanism is copied here: `docs/ui.md` says there is
 * no theme setting, so that the interface follows the colour scheme the
 * operating system asks for and no screenshot carries the question of which
 * mode it was taken in (ADR 0051). There is nothing to set, so there is no
 * context and no `useTheme`; the class simply follows the media query, at load
 * and whenever it changes.
 */
export function ThemeProvider({ children }: { children: React.ReactNode }) {
  useEffect(() => {
    const media = window.matchMedia(COLOR_SCHEME_QUERY);

    function apply() {
      const root = document.documentElement;
      root.classList.toggle("dark", media.matches);
      root.classList.toggle("light", !media.matches);
    }

    apply();
    media.addEventListener("change", apply);
    return () => media.removeEventListener("change", apply);
  }, []);

  return children;
}
