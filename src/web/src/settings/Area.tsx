import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * One thing that can be changed on a settings screen, with the sentence saying
 * what changing it does.
 *
 * The areas of a screen are a column of these, ruled off from each other rather
 * than boxed: they are one screen's worth of one subject, and a card each would
 * make five settings look like five places.
 *
 * `grave` is the exception, and it is the one act that destroys data. It is
 * boxed and outlined in the refusal colour, because the eye should meet it
 * before the mouse does.
 */
export function Area({
  title,
  grave = false,
  children,
}: {
  title: string;
  grave?: boolean;
  children: ReactNode;
}) {
  return (
    <section
      className={cn(
        "grid gap-3",
        grave
          ? "rounded-lg border border-destructive/50 p-4"
          : "border-t pt-6 first:border-t-0 first:pt-0",
      )}
    >
      <h2 className={cn("text-base font-semibold", grave && "text-destructive")}>{title}</h2>
      {children}
    </section>
  );
}

/** What an area says about itself before anything has been asked of it. */
export function About({ children }: { children: ReactNode }) {
  return <p className="max-w-prose text-muted-foreground">{children}</p>;
}

/**
 * The two lines every area can say afterwards: what the installation refused
 * with, and the one word confirming it did not.
 */
export function Said({ problem, note }: { problem?: string; note?: string }) {
  return (
    <>
      {problem !== undefined && <p className="refusal text-sm">{problem}</p>}
      {note !== undefined && <p className="quiet text-sm">{note}</p>}
    </>
  );
}

/**
 * A question the screen asks before it does something it cannot take back, put
 * where the button was rather than over the top of the screen: what it is about
 * is on the screen behind it, and a dialog would cover the thing being decided.
 */
export function Confirming({ children }: { children: ReactNode }) {
  return (
    <div className="grid gap-3 rounded-lg border border-brand/30 bg-brand-soft p-3 text-sm">
      {children}
    </div>
  );
}

/**
 * A short table of things the installation holds — tokens, sessions, groups,
 * hosts. It is ruled by rows and has no box around it: it is one part of an
 * area rather than a thing in its own right.
 */
export function Listing({ children }: { children: ReactNode }) {
  return <table className="w-full border-collapse text-sm">{children}</table>;
}

/** A column's name, which is quieter than what is under it. */
export function Head({ children }: { children: ReactNode }) {
  return (
    <th
      scope="col"
      className="border-b py-1.5 pr-3 text-left align-baseline text-xs font-medium text-muted-foreground"
    >
      {children}
    </th>
  );
}

/** The cell that says which row this is. */
export function RowName({ children }: { children: ReactNode }) {
  return (
    <th scope="row" className="border-b py-1.5 pr-3 text-left align-baseline font-medium">
      {children}
    </th>
  );
}

/** Every other cell. */
export function Cell({ className, children }: { className?: string; children: ReactNode }) {
  return (
    <td className={cn("border-b py-1.5 pr-3 align-baseline whitespace-nowrap", className)}>
      {children}
    </td>
  );
}
