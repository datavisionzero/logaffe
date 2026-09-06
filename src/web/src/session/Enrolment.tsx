import { useState, type ReactNode } from "react";
import { QRCodeSVG } from "qrcode.react";
import { Button } from "../components/ui/button";
import { Check } from "../components/Field";
import { Callout } from "../components/Page";

/**
 * A second factor and a sheet of backup codes the installation drew and stored
 * neither of, with the sealed ticket that carries both back to it
 * ([ADR 0035], [ADR 0036]).
 */
export interface Enrolment {
  secondFactorSecret: string;
  enrolmentUri: string;
  backupCodes: string[];
  ticket: string;
}

/**
 * The one moment either of them exists anywhere but in the operator's hands.
 *
 * It is the same screen in the claim and in a re-enrolment, because it is the
 * same act: the installation has drawn a secret and ten codes and stored
 * neither, and what happens here decides whether the operator can still get in
 * afterwards. The two callers differ in what they say around it and in what the
 * confirming request is, and in nothing else.
 *
 * The prose above the code is the caller's, since the claim is enrolling a phone
 * for the first time while a re-enrolment is replacing one that still works.
 */
export function ShowEnrolment({
  heading,
  enrolment,
  replacing,
  children,
  onKept,
}: {
  heading: string;
  enrolment: Enrolment;
  /** Whether a sheet already exists, which this one replaces wholesale. */
  replacing?: boolean;
  children: ReactNode;
  onKept: () => void;
}) {
  const [kept, setKept] = useState(false);

  return (
    <section className="grid gap-3">
      <h2 className="text-base font-semibold">{heading}</h2>
      {children}

      {/* The code paints its own ground — dark on light, with a quiet zone —
          whatever the operating system's colour scheme is, because a camera
          reads it that way round and an inverted one fails for a reason nobody
          can see. The frame is ours; the two colours inside it are the
          component's defaults and are left alone. */}
      <div className="w-fit rounded-lg border p-2">
        <QRCodeSVG value={enrolment.enrolmentUri} size={192} marginSize={2} />
      </div>

      <p>
        Or type the secret in by hand:{" "}
        <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs break-all">
          {enrolment.secondFactorSecret}
        </code>
      </p>

      <BackupCodeSheet codes={enrolment.backupCodes} replacing={replacing} />

      <Check>
        <input
          type="checkbox"
          className="mt-0.5 size-4 accent-brand"
          checked={kept}
          onChange={(e) => setKept(e.target.checked)}
        />
        I have the authenticator enrolled and the codes kept
      </Check>

      <Button type="button" disabled={!kept} onClick={onKept} className="w-fit">
        Continue
      </Button>
    </section>
  );
}

/**
 * Ten codes, shown once and stored in a form nobody can read back (ADR 0032).
 *
 * It is the same sheet whether it arrives with a second factor or on its own,
 * and it is the same warning: this is the only moment these exist anywhere but
 * in the operator's hands, and a set that quietly runs out ends at Host
 * Recovery.
 */
export function BackupCodeSheet({
  codes,
  replacing,
}: {
  codes: string[];
  /** Whether a sheet already exists, which this one replaces wholesale. */
  replacing?: boolean;
}) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    await navigator.clipboard.writeText(codes.join("\n"));
    setCopied(true);
  }

  return (
    <div className="grid gap-2">
      <h3 className="text-sm font-semibold">Backup codes</h3>
      <Callout>
        These are shown once and are stored in a form nobody can read back. Each is used
        once, and they are what stands in for the second factor when the phone is gone.
        Keep them somewhere that is not the phone.
        {replacing === true &&
          " They replace the sheet you have now — spent codes and unspent alike."}
      </Callout>

      <ul className="grid max-w-md grid-cols-[repeat(auto-fill,minmax(9rem,1fr))] gap-x-4 gap-y-1 rounded-lg border bg-muted p-3">
        {codes.map((code) => (
          <li key={code}>
            <code className="font-mono text-sm">{code}</code>
          </li>
        ))}
      </ul>

      <Button type="button" variant="outline" onClick={() => void copy()} className="w-fit">
        {copied ? "Copied" : "Copy the codes"}
      </Button>
    </div>
  );
}
