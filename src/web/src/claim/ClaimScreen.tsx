import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import { ShowEnrolment, type Enrolment } from "../session/Enrolment";
import { PASSWORD_MINIMUM } from "../session/password";
import { formatTimestamp } from "../shared/time";

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
          heading="2. A second factor, and ten backup codes"
          enrolment={step.enrolment}
          onKept={() => setStep({ at: "confirming", password: step.password, enrolment: step.enrolment })}
        >
          <p>
            Scan this with an authenticator app. It cannot be turned off later, and it can
            be re-enrolled while signed in — which is what makes replacing a phone an
            ordinary afternoon.
          </p>
        </ShowEnrolment>
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
