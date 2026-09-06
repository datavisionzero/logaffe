import { cn } from "@/lib/utils";
import type { ListedEntry } from "./entries";

/**
 * The level as a word with a colour behind it, and never as a colour alone
 * (`docs/ui.md`).
 *
 * One token per level, and the chip mixes its own ground out of it — which is
 * why there are six values here and not twelve. Fatal is the exception and the
 * only one: it is filled rather than tinted, because the step from Error to
 * Fatal has to be larger than one of saturation.
 *
 * The width is fixed so that the logger column does not move from row to row.
 * A list that is scanned is scanned down a column, and a column that shifts by
 * a character on every fifth line is one the eye has to keep finding again.
 */
export function Level({ level, className }: { level: ListedEntry["level"]; className?: string }) {
  return <span className={cn(chip(level), className)}>{level}</span>;
}

/** The chip as classes, for the places where it is something to press. */
export function chip(level: string): string {
  return cn(
    "inline-block min-w-22 rounded-md px-1.5 text-center text-[0.78rem]",
    grounds[level] ?? "bg-muted text-muted-foreground",
  );
}

/**
 * Written out rather than composed, because Tailwind reads the class names in
 * this file as text: a `bg-level-${level}/15` assembled at runtime is a class
 * that never reaches the stylesheet.
 */
const grounds: Record<string, string> = {
  Verbose: "bg-level-verbose/15 text-level-verbose",
  Debug: "bg-level-debug/15 text-level-debug",
  Information: "bg-level-information/15 text-level-information",
  Warning: "bg-level-warning/15 text-level-warning",
  Error: "bg-level-error/15 text-level-error",
  Fatal: "bg-level-fatal text-level-fatal-foreground",
};
