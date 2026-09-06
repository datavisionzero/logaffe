import type { ReactNode } from "react";
import { Label } from "@/components/ui/label";

/**
 * One labelled control, and whatever the installation said about it.
 *
 * The label wraps the control rather than pointing at it by identity, because
 * nothing on these screens has to invent an id for that to work — and the label
 * text is exactly the label, with anything the operator has to read first put
 * above the field and not inside it.
 */
export function Field({
  label,
  after,
  said,
  children,
}: {
  label: ReactNode;
  /** What follows the control on the same line — a unit, usually. */
  after?: ReactNode;
  /** What the installation refused with, said under the field it is about. */
  said?: string;
  children: ReactNode;
}) {
  return (
    <div className="grid gap-1.5">
      <Label className="grid gap-1.5">
        <span>{label}</span>
        {after === undefined ? (
          children
        ) : (
          <span className="flex items-center gap-2">
            {children}
            <span className="shrink-0 text-muted-foreground">{after}</span>
          </span>
        )}
      </Label>
      {said !== undefined && <p className="refusal text-sm">{said}</p>}
    </div>
  );
}

/** A tick and the sentence it is about, which reads as one line. */
export function Check({ children }: { children: ReactNode }) {
  return (
    <Label className="items-start gap-2 font-normal">
      {children}
    </Label>
  );
}
