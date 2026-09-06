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
