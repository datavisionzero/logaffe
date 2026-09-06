import * as React from "react"
import { ChevronDownIcon } from "lucide-react"

import { cn } from "@/lib/utils"

/**
 * The browser's own select, wearing the Input's border and focus ring.
 *
 * Base UI has a Select primitive and planaffe uses the Picker for the same
 * job, but every select in this application is a short list of fixed choices
 * inside a form — a level, a span, a bucket, a group. The native control
 * already carries the keyboard behaviour and the mobile presentation for that,
 * and swapping it for a listbox would change what the tests find without
 * changing what the operator can do.
 */
function Select({ className, children, ...props }: React.ComponentProps<"select">) {
  return (
    <div data-slot="select" className="relative inline-flex w-full items-center">
      <select
        className={cn(
          "h-8 w-full min-w-0 appearance-none rounded-lg border border-input bg-transparent py-1 pr-7 pl-2.5 text-base transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50 md:text-sm dark:bg-input/30",
          className
        )}
        {...props}
      >
        {children}
      </select>
      <ChevronDownIcon
        aria-hidden
        className="pointer-events-none absolute right-2 size-3.5 text-muted-foreground"
      />
    </div>
  )
}

export { Select }
