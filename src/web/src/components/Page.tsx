import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * The column a screen that is read rather than scanned stands in.
 *
 * Everything except the log view is prose, forms and short tables, and all of
 * it wants the same measure: wide enough for a table of four columns, narrow
 * enough that a sentence does not run the width of a monitor. The settings
 * screens are given more because they carry their areas down the left inside
 * the column.
 */
export function Page({
  wide = false,
  className,
  children,
}: {
  wide?: boolean;
  className?: string;
  children: ReactNode;
}) {
  return (
    <section
      className={cn(
        "mx-auto w-full px-4 py-6",
        wide ? "max-w-[76rem]" : "max-w-[62rem]",
        className,
      )}
    >
      {children}
    </section>
  );
}

/** The one heading a screen has, and the only `h1` below the shell. */
export function PageTitle({ children }: { children: ReactNode }) {
  return <h1 className="text-xl font-semibold tracking-tight">{children}</h1>;
}

/** A part of a screen, under a heading of its own. */
export function Section({
  title,
  className,
  children,
}: {
  title: ReactNode;
  className?: string;
  children: ReactNode;
}) {
  return (
    <section className={cn("grid gap-3", className)}>
      <h2 className="text-base font-semibold">{title}</h2>
      {children}
    </section>
  );
}

/**
 * The column the screens before the shell stand in: the claim, the guide that
 * follows it, and the sign-in.
 *
 * They have no frame around them — there is nothing to navigate to yet — so
 * they carry their own, and they are narrower than a screen inside the
 * application because all three are read rather than scanned.
 */
export function Gate({ children }: { children: ReactNode }) {
  return (
    <main className="mx-auto grid w-full max-w-2xl gap-6 px-4 py-10 sm:py-16">{children}</main>
  );
}

/** A boxed sentence the operator has to read before going on. */
export function Callout({ children }: { children: ReactNode }) {
  return (
    <p className="rounded-lg border border-brand/30 bg-brand-soft px-3 py-2 text-sm">
      {children}
    </p>
  );
}

/** Something to copy out of the screen, whole and unwrapped. */
export function Snippet({ children }: { children: ReactNode }) {
  return (
    <pre className="overflow-x-auto rounded-lg border bg-muted px-3 py-2.5 font-mono text-xs">
      {children}
    </pre>
  );
}
