import { CircleUserRoundIcon, LogOutIcon, ServerIcon } from "lucide-react";
import { useNavigate } from "react-router";
import { Button } from "../components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "../components/ui/dropdown-menu";
import { browserTimeZone } from "../shared/time";
import type { Me } from "../session/me";

/**
 * Top right, where every reader of a web application looks for it.
 *
 * **It says who is signed in**, because there is more than one person now and
 * the question "as whom am I looking at this" has an answer worth reading — on
 * an installation somebody administers for other people, it is the difference
 * between two browser windows. Still no avatar and no initials: a name and an
 * address say it, and a picture would be a thing to upload.
 *
 * The time zone is repeated here because the header only has room to say it on
 * a wide window, and the sentence it carries — every timestamp in this
 * application is absolute, in this zone, to the millisecond, and there is no
 * toggle to another — is one somebody should be able to find on a phone too.
 */
export function AccountMenu({
  me,
  onSignOut,
}: {
  me: Me | undefined | null;
  onSignOut: () => void;
}) {
  const navigate = useNavigate();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={<Button variant="ghost" size="icon-sm" aria-label="Account" />}
      >
        <CircleUserRoundIcon />
      </DropdownMenuTrigger>

      <DropdownMenuContent align="end" className="min-w-56">
        {/* The label is a group's label to Base UI, and a group part outside a
            group throws rather than degrading — so the heading of this menu is
            wrapped even though it is the only thing in its group. */}
        <DropdownMenuGroup>
          <DropdownMenuLabel className="font-normal">
            <div className="font-medium">{me?.name ?? "This installation"}</div>
            {me != null && (
              <div className="text-xs text-muted-foreground">{me.email}</div>
            )}
            <div className="text-xs text-muted-foreground">Times in {browserTimeZone()}</div>
          </DropdownMenuLabel>
        </DropdownMenuGroup>

        <DropdownMenuSeparator />

        <DropdownMenuItem onClick={() => void navigate("/settings")}>
          <ServerIcon />
          Installation settings
        </DropdownMenuItem>

        <DropdownMenuItem onClick={onSignOut}>
          <LogOutIcon />
          Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
