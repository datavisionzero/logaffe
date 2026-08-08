import { useState, type FormEvent } from "react";
import { QRCodeSVG } from "qrcode.react";
import { api, problemWith } from "../api/client";
import { formatTimestamp } from "../shared/time";

/** The shortest password the installation will take (`docs/sign-in.md`). */
const PASSWORD_MINIMUM = 12;

interface Enrolment {
  secondFactorSecret: string;
  enrolmentUri: string;
  backupCodes: string[];
  ticket: string;
}

type Step =
  | { at: "password" }
  | { at: "enrolment"; password: string; enrolment: Enrolment }
  | { at: "confirming"; password: string; enrolment: Enrolment };

/**
 * The window lapsed before anyone claimed this installation.
 *
 * Claiming over the network is over and no request will ever be right again, so
 * the screen names the command that re-opens it rather than leaving an operator
 * who is already having a bad minute to search for it (`docs/setup.md`).
 */
export function WindowClosed() {
  return (
    <main>
      <h1>This installation cannot be claimed</h1>
      <p>
        The claim window opened when this installation first ran and has since lapsed.
        Claiming over the network is over, and a restart does not open it again.
      </p>
      <p>Run this on the host the installation runs on, and reload this page:</p>
      <pre>docker compose exec logaffe logaffe recover</pre>
      <p>
        It returns the installation to unclaimed and arms a fresh window. Projects, ingest
        tokens and log entries are untouched.
      </p>
    </main>
  );
}

/**
 * The whole reachable surface of an installation nobody owns.
 *
 * Three steps in order — a password, a second factor, a sheet of backup codes
 * confirmed by typing one back — and <b>only the last one stores anything</b>
 * (ADR 0014). A claim that is started and abandoned holds nothing, so there is
 * no state here to clean up and no way back to explain: the whole flow is one
 * screen and leaving it costs the enrolment, which is drawn again.
 *
 * The secret and the codes survive between the screen that shows them and the
 * request that completes the claim in this component, alongside the sealed
 * ticket only the installation can read (ADR 0035).
 */
export function ClaimScreen({
  closesAt,
  onClaimed,
}: {
  closesAt: string | null;
  onClaimed: () => void;
}) {
  const [step, setStep] = useState<Step>({ at: "password" });

  return (
    <main>
      <h1>Claim this installation</h1>
      <p>
        This installation belongs to nobody yet, and whoever finishes below is its
        operator. There is one account and no second one afterwards.
      </p>
      {closesAt !== null && (
        <p className="notice">
          The claim window closes at{" "}
          <time dateTime={closesAt}>{formatTimestamp(new Date(closesAt))}</time>. After
          that it is opened again only from the host.
        </p>
      )}

      {step.at === "password" ? (
        <ChoosePassword onChosen={(password, enrolment) => setStep({ at: "enrolment", password, enrolment })} />
      ) : step.at === "enrolment" ? (
        <ShowEnrolment
          enrolment={step.enrolment}
          onKept={() => setStep({ at: "confirming", password: step.password, enrolment: step.enrolment })}
        />
      ) : (
        <FinishTheClaim
          password={step.password}
          enrolment={step.enrolment}
          onClaimed={onClaimed}
          onStartAgain={() => setStep({ at: "password" })}
        />
      )}
    </main>
  );
}

/**
 * The password, and the enrolment drawn the moment it is settled.
 *
 * It is confirmed by typing it twice, which is furniture the product avoids
 * everywhere else and earns here: there is no password reset over the network
 * and no email to send one to (ADR 0015), so a typo at this step is answered by
 * Host Recovery and nothing smaller.
 */
function ChoosePassword({
  onChosen,
}: {
  onChosen: (password: string, enrolment: Enrolment) => void;
}) {
  const [password, setPassword] = useState("");
  const [again, setAgain] = useState("");
  const [refusal, setRefusal] = useState<string>();
  const [drawing, setDrawing] = useState(false);

  const tooShort = password.length > 0 && password.length < PASSWORD_MINIMUM;
  const mismatched = again.length > 0 && again !== password;

  async function draw(event: FormEvent) {
    event.preventDefault();
    setRefusal(undefined);

    if (password.length < PASSWORD_MINIMUM || again !== password) {
      setRefusal("Give a password of at least twelve characters, twice.");
      return;
    }

    setDrawing(true);

    try {
      const { data, response } = await api.POST("/claim/enrolment");

      if (data !== undefined) {
        onChosen(password, data);
        return;
      }

      setRefusal(refusalFor(response.status));
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setDrawing(false);
    }
  }

  return (
    <form onSubmit={draw}>
      <h2>1. A password</h2>
      <p>
        At least {PASSWORD_MINIMUM} characters, and nothing else is asked of it. Length is
        the property that matters and the second factor carries the rest.
      </p>

      <label>
        Password
        <input
          type="password"
          autoComplete="new-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          aria-invalid={tooShort || undefined}
        />
      </label>
      {tooShort && <p className="refusal">A password is at least {PASSWORD_MINIMUM} characters.</p>}

      <label>
        Password again
        <input
          type="password"
          autoComplete="new-password"
          value={again}
          onChange={(e) => setAgain(e.target.value)}
          aria-invalid={mismatched || undefined}
        />
      </label>
      {mismatched && <p className="refusal">These two are not the same.</p>}

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      <button type="submit" disabled={drawing}>
        Continue
      </button>
    </form>
  );
}

/**
 * The second factor and the sheet, shown once.
 *
 * Nothing here is stored yet — the installation drew both and kept neither —
 * so this is the only moment the backup codes exist anywhere but in the
 * operator's hands.
 */
function ShowEnrolment({
  enrolment,
  onKept,
}: {
  enrolment: Enrolment;
  onKept: () => void;
}) {
  const [kept, setKept] = useState(false);
  const [copied, setCopied] = useState(false);

  async function copy() {
    await navigator.clipboard.writeText(enrolment.backupCodes.join("\n"));
    setCopied(true);
  }

  return (
    <section>
      <h2>2. A second factor, and ten backup codes</h2>
      <p>
        Scan this with an authenticator app. It cannot be turned off later, and it can be
        re-enrolled while signed in — which is what makes replacing a phone an ordinary
        afternoon.
      </p>

      <QRCodeSVG value={enrolment.enrolmentUri} size={192} marginSize={2} />

      <p>
        Or type the secret in by hand: <code>{enrolment.secondFactorSecret}</code>
      </p>

      <h3>Backup codes</h3>
      <p className="notice">
        These are shown once and are stored in a form nobody can read back. Each is used
        once, and they are what stands in for the second factor when the phone is gone.
        Keep them somewhere that is not the phone.
      </p>

      <ul className="codes">
        {enrolment.backupCodes.map((code) => (
          <li key={code}>
            <code>{code}</code>
          </li>
        ))}
      </ul>

      <button type="button" onClick={() => void copy()}>
        {copied ? "Copied" : "Copy the codes"}
      </button>

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
 * The last step, which is the only one that stores anything.
 *
 * Two proofs: a code from the app just enrolled, which is what proves the
 * enrolment took, and one code off the sheet, which is what proves it was kept.
 */
function FinishTheClaim({
  password,
  enrolment,
  onClaimed,
  onStartAgain,
}: {
  password: string;
  enrolment: Enrolment;
  onClaimed: () => void;
  onStartAgain: () => void;
}) {
  const [secondFactorCode, setSecondFactorCode] = useState("");
  const [backupCode, setBackupCode] = useState("");
  const [refusal, setRefusal] = useState<string>();
  const [problems, setProblems] = useState<{ secondFactorCode?: string; backupCode?: string }>({});
  const [claiming, setClaiming] = useState(false);

  async function claim(event: FormEvent) {
    event.preventDefault();
    setRefusal(undefined);
    setProblems({});
    setClaiming(true);

    try {
      const { response, error } = await api.POST("/claim", {
        body: {
          password,
          ticket: enrolment.ticket,
          secondFactorCode,
          backupCode,
        },
      });

      if (response.status === 204) {
        // The claim signed them in; there is nothing to say and nowhere to
        // point but the installation itself.
        onClaimed();
        return;
      }

      if (response.status === 400) {
        // Every refusal here says which step failed, because the door is open
        // on purpose and the person on the other end is setting up their own
        // installation.
        setProblems({
          secondFactorCode: problemWith(error, "secondFactorCode"),
          backupCode: problemWith(error, "backupCode"),
        });

        const ticket = problemWith(error, "ticket");
        const chosen = problemWith(error, "password");

        setRefusal(ticket ?? chosen);
        return;
      }

      setRefusal(refusalFor(response.status));
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setClaiming(false);
    }
  }

  return (
    <form onSubmit={claim}>
      <h2>3. Confirm both</h2>

      <label>
        The six digits from the app
        <input
          inputMode="numeric"
          autoComplete="one-time-code"
          value={secondFactorCode}
          onChange={(e) => setSecondFactorCode(e.target.value)}
          aria-invalid={problems.secondFactorCode !== undefined || undefined}
        />
      </label>
      {problems.secondFactorCode !== undefined && (
        <p className="refusal">{problems.secondFactorCode}</p>
      )}

      <label>
        One backup code off the sheet
        <input
          value={backupCode}
          onChange={(e) => setBackupCode(e.target.value)}
          aria-invalid={problems.backupCode !== undefined || undefined}
        />
      </label>
      {problems.backupCode !== undefined && <p className="refusal">{problems.backupCode}</p>}

      {refusal !== undefined && (
        <p className="refusal">
          {refusal}{" "}
          <button type="button" className="plain" onClick={onStartAgain}>
            Start again
          </button>
        </p>
      )}

      <button type="submit" disabled={claiming}>
        Claim this installation
      </button>
    </form>
  );
}

/**
 * The refusals that are not about a box on the form. Two people can walk this
 * flow at once, and the one who confirms second meets the conflict.
 */
function refusalFor(status: number): string {
  switch (status) {
    case 403:
      return "The claim window has lapsed. Claiming over the network is over until the host arms a fresh one.";
    case 409:
      return "This installation now has an operator. Somebody else finished first.";
    case 429:
      return "Too many attempts from here. Wait a moment and try again.";
    default:
      return "This installation refused the claim.";
  }
}
