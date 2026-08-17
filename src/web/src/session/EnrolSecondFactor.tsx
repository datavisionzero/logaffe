import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import { ShowEnrolment, type Enrolment } from "./Enrolment";

type Step =
  | { at: "settled" }
  | { at: "drawing" }
  | { at: "enrolment"; enrolment: Enrolment }
  | { at: "confirming"; enrolment: Enrolment };

/**
 * Enrolling a second factor, and replacing one — which are one act with one
 * optional half (ADR 0041).
 *
 * The installation draws a secret and a fresh sheet, shows both, and hands back
 * a sealed ticket carrying them (ADR 0036) — **and stores nothing until the
 * confirming request**, so an authenticator already in the operator's pocket
 * keeps working right up to the moment it is replaced, and an account that has
 * none is unchanged by abandoning this.
 *
 * It is written once and used from two places — the guide that follows a claim,
 * and the settings — because it is the same act in both. What differs is the
 * prose around it and whether there is a second factor in use to prove, and the
 * second of those is `replacing`.
 */
export function EnrolSecondFactor({
  replacing,
  onEnrolled,
  children,
}: {
  /** Whether the account already has one, which this enrolment replaces. */
  replacing: boolean;
  onEnrolled: () => void;
  children?: React.ReactNode;
}) {
  const [step, setStep] = useState<Step>({ at: "settled" });
  const [refusal, setRefusal] = useState<string>();

  async function draw() {
    setRefusal(undefined);
    setStep({ at: "drawing" });

    try {
      const { data } = await api.POST("/second-factor/enrolment");

      if (data === undefined) {
        // A 401 is the account gone from under a live session, which is Host
        // Recovery a moment ago — the sign-in is already on its way in front of
        // everything and there is nothing to say here.
        setStep({ at: "settled" });
        setRefusal("This installation did not draw an enrolment.");
        return;
      }

      setStep({ at: "enrolment", enrolment: data });
    } catch {
      setStep({ at: "settled" });
      setRefusal("This installation did not answer.");
    }
  }

  return (
    <>
      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      {step.at === "settled" && (
        <button type="button" onClick={() => void draw()}>
          {replacing ? "Replace the second factor" : "Enrol a second factor"}
        </button>
      )}

      {step.at === "drawing" && <p className="quiet">Drawing an enrolment…</p>}

      {step.at === "enrolment" && (
        <ShowEnrolment
          heading={
            replacing
              ? "Scan this with the new authenticator"
              : "Scan this with an authenticator app"
          }
          enrolment={step.enrolment}
          replacing={replacing}
          onKept={() => setStep({ at: "confirming", enrolment: step.enrolment })}
        >
          {children ?? (
            <p>
              Nothing here is stored yet. Leaving this screen costs the enrolment and
              nothing else.
            </p>
          )}
        </ShowEnrolment>
      )}

      {step.at === "confirming" && (
        <Confirm
          enrolment={step.enrolment}
          replacing={replacing}
          onEnrolled={() => {
            setStep({ at: "settled" });
            onEnrolled();
          }}
          onStartAgain={() => void draw()}
        />
      )}
    </>
  );
}

/**
 * The only request that stores anything, and the only one that can refuse.
 *
 * Every refusal says which credential did not take. There is nobody here but the
 * operator — they proved it with the session — and somebody enrolling with a
 * phone in one hand has to know which step failed rather than be told that
 * something did.
 */
function Confirm({
  enrolment,
  replacing,
  onEnrolled,
  onStartAgain,
}: {
  enrolment: Enrolment;
  replacing: boolean;
  onEnrolled: () => void;
  onStartAgain: () => void;
}) {
  const [password, setPassword] = useState("");
  const [secondFactorCode, setSecondFactorCode] = useState("");
  const [backupCode, setBackupCode] = useState("");
  const [usingBackupCode, setUsingBackupCode] = useState(false);
  const [newSecondFactorCode, setNewSecondFactorCode] = useState("");
  const [problems, setProblems] = useState<{
    password?: string;
    secondFactorCode?: string;
    newSecondFactorCode?: string;
  }>({});
  const [refusal, setRefusal] = useState<string>();
  const [enrolling, setEnrolling] = useState(false);

  async function enrol(event: FormEvent) {
    event.preventDefault();
    setProblems({});
    setRefusal(undefined);
    setEnrolling(true);

    try {
      const { response, error } = await api.PUT("/second-factor", {
        body: {
          password,
          // Nothing in use to prove on an account that has none, and the form
          // above does not ask for it.
          secondFactorCode: !replacing || usingBackupCode ? null : secondFactorCode,
          backupCode: replacing && usingBackupCode ? backupCode : null,
          newSecondFactorCode,
          ticket: enrolment.ticket,
        },
      });

      if (response.status === 204) {
        onEnrolled();
        return;
      }

      if (response.status === 400) {
        setProblems({
          password: problemWith(error, "password"),
          secondFactorCode: problemWith(error, "secondFactorCode"),
          newSecondFactorCode: problemWith(error, "newSecondFactorCode"),
        });

        // An enrolment this installation does not recognize, or one drawn too
        // long ago. Nothing on the form can fix it and a fresh one is a click.
        setRefusal(problemWith(error, "ticket"));
        return;
      }

      setRefusal("This installation refused the enrolment.");
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setEnrolling(false);
    }
  }

  return (
    <form onSubmit={enrol}>
      <h3>{replacing ? "Confirm the replacement" : "Confirm the enrolment"}</h3>

      <label>
        Password
        <input
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          aria-invalid={problems.password !== undefined || undefined}
        />
      </label>
      {problems.password !== undefined && <p className="refusal">{problems.password}</p>}

      {replacing &&
        (usingBackupCode ? (
          <label>
            A backup code off the sheet you have now
            <input
              name="backup-code"
              autoComplete="off"
              value={backupCode}
              onChange={(e) => setBackupCode(e.target.value)}
            />
          </label>
        ) : (
          <label>
            The six digits from the authenticator you have now
            {/* Neither code field here is offered to a password manager, and that
                is deliberate -- it is the one screen that opts out.

                `SignInScreen` explains why the sign-in field is named `totp`. The
                same naming here made things worse rather than better: two code
                fields side by side, same stem and same length, read to a manager
                as one segmented input -- the box-per-digit pattern -- and it
                spread a single code one digit per field. Breaking that grouping
                would be enough to stop it, but it would not make the fill right:
                these two codes come from two different phones, and a manager
                holds at most one of them. Re-enrolling replaces the secret it
                holds, so which field it would be right about depends on whether
                the operator scanned the new code before clicking this one.

                A manager that is right half the time is worse than none on the
                screen where six wrong digits cost the operator their way in, and
                they have both authenticators in front of them anyway. */}
            <input
              id="current-code"
              name="current-code"
              inputMode="numeric"
              maxLength={6}
              autoComplete="off"
              value={secondFactorCode}
              onChange={(e) => setSecondFactorCode(e.target.value)}
              aria-invalid={problems.secondFactorCode !== undefined || undefined}
            />
          </label>
        ))}
      {problems.secondFactorCode !== undefined && (
        <p className="refusal">{problems.secondFactorCode}</p>
      )}

      {/* The phone that is already gone is the case a replacement exists for, so
          the code that stands in for it is one click away rather than something
          to be found. */}
      {replacing && (
        <button
          type="button"
          className="plain"
          onClick={() => setUsingBackupCode(!usingBackupCode)}
        >
          {usingBackupCode
            ? "Use the authenticator I have now"
            : "The old phone is gone — use a backup code"}
        </button>
      )}

      <label>
        The six digits from the authenticator you just enrolled
        <input
          id="enrolled-code"
          name="enrolled-code"
          inputMode="numeric"
          maxLength={6}
          autoComplete="off"
          value={newSecondFactorCode}
          onChange={(e) => setNewSecondFactorCode(e.target.value)}
          aria-invalid={problems.newSecondFactorCode !== undefined || undefined}
        />
      </label>
      {problems.newSecondFactorCode !== undefined && (
        <p className="refusal">{problems.newSecondFactorCode}</p>
      )}

      {refusal !== undefined && (
        <p className="refusal">
          {refusal}{" "}
          <button type="button" className="plain" onClick={onStartAgain}>
            Draw a fresh enrolment
          </button>
        </p>
      )}

      <button type="submit" disabled={enrolling}>
        {replacing ? "Replace it" : "Enrol it"}
      </button>
    </form>
  );
}
