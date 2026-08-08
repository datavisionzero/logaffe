import { useState, type ReactNode } from "react";
import { QRCodeSVG } from "qrcode.react";

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
    <section>
      <h2>{heading}</h2>
      {children}

      <QRCodeSVG value={enrolment.enrolmentUri} size={192} marginSize={2} />

      <p>
        Or type the secret in by hand: <code>{enrolment.secondFactorSecret}</code>
      </p>

      <BackupCodeSheet codes={enrolment.backupCodes} replacing={replacing} />

      <label className="confirm">
        <input type="checkbox" checked={kept} onChange={(e) => setKept(e.target.checked)} />I
        have the authenticator enrolled and the codes kept
      </label>

      <button type="button" disabled={!kept} onClick={onKept}>
        Continue
      </button>
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
    <>
      <h3>Backup codes</h3>
      <p className="notice">
        These are shown once and are stored in a form nobody can read back. Each is used
        once, and they are what stands in for the second factor when the phone is gone.
        Keep them somewhere that is not the phone.
        {replacing === true &&
          " They replace the sheet you have now — spent codes and unspent alike."}
      </p>

      <ul className="codes">
        {codes.map((code) => (
          <li key={code}>
            <code>{code}</code>
          </li>
        ))}
      </ul>

      <button type="button" onClick={() => void copy()}>
        {copied ? "Copied" : "Copy the codes"}
      </button>
    </>
  );
}
