import * as React from "react"

import { cn } from "@/lib/utils"

/**
 * planaffe has no Label of its own — its forms are few and inline. logaffe's
 * settings screens are almost nothing but labelled fields, so the one class
 * string they all share lives here rather than in thirty places.
 */
function Label({ className, ...props }: React.ComponentProps<"label">) {
  return (
    <label
      data-slot="label"
      className={cn(
        "flex select-none items-center gap-2 text-sm leading-none font-medium text-foreground has-disabled:pointer-events-none has-disabled:opacity-50",
        className
      )}
      {...props}
    />
  )
}

export { Label }
