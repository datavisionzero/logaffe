import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import { ShowEnrolment, type Enrolment } from "../session/Enrolment";

type Step =
  | { at: "settled" }
  | { at: "drawing" }
  | { at: "enrolment"; enrolment: Enrolment }
  | { at: "confirming"; enrolment: Enrolment };

/**
 * Replacing the second factor, which is what makes replacing a phone an
 * ordinary afternoon instead of an incident.
 *
 * It is the claim's enrolment for an account that already exists: the
 * installation draws a new secret and a fresh sheet, shows both, and hands back
 * a sealed ticket carrying them ([ADR 0036]) — **and stores nothing until the
 * confirming request**, so the authenticator in the operator's pocket keeps
 * working right up to the moment it is replaced.
 *
 * That request asks for all three: the password, the second factor in use — the
 * current code, or a backup code, which is the case of the phone that is already
 * gone — and a code from the app just enrolled, which is what proves the
 * enrolment took. What cannot happen here is turning it off. There is no route
 * that removes one, and a god-mode account on the public internet does not get
 * to become single-factor later (ADR 0016).
 */
export function SecondFactor() {
  const [step, setStep] = useState<Step>({ at: "settled" });
  const [replaced, setReplaced] = useState(false);
  const [refusal, setRefusal] = useState<string>();

  async function draw() {
    setRefusal(undefined);
    setReplaced(false);
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
    <section>
      <h2>Second factor</h2>
      <p>
        A time-based code from an authenticator app. It cannot be turned off, only
        replaced — and replacing it is the ordinary answer to a new phone. Nothing is
        stored until the last step below, so the app you have now keeps working until the
        moment the new one takes over.
      </p>
      <p>
        Re-enrolling issues a fresh sheet of backup codes and <b>ends every other
        session</b>.
      </p>

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      {replaced && (
        <p className="quiet">
          Replaced. The previous authenticator and the previous sheet are gone, and every
          other session has ended.
        </p>
      )}

      {step.at === "settled" && (
        <button type="button" onClick={() => void draw()}>
          Replace the second factor
        </button>
      )}

      {step.at === "drawing" && <p className="quiet">Drawing an enrolment…</p>}

      {step.at === "enrolment" && (
        <ShowEnrolment
          heading="Scan this with the new authenticator"
          enrolment={step.enrolment}
          replacing
          onKept={() => setStep({ at: "confirming", enrolment: step.enrolment })}
        >
          <p>
            Nothing here is stored yet. Leaving this screen costs the enrolment and
            nothing else — the authenticator you have now still works.
          </p>
        </ShowEnrolment>
      )}

      {step.at === "confirming" && (
        <Confirm
          enrolment={step.enrolment}
          onReplaced={() => {
            setStep({ at: "settled" });
            setReplaced(true);
          }}
          onStartAgain={() => void draw()}
        />
      )}
    </section>
  );
}

/**
 * The only request that stores anything, and the only one that can refuse.
 *
 * Every refusal says which of the three credentials did not take. There is
 * nobody here but the operator — they proved it with the session — and somebody
 * replacing their second factor with a phone in one hand has to know which step
 * failed rather than be told that something did.
 */
function Confirm({
  enrolment,
  onReplaced,
  onStartAgain,
}: {
  enrolment: Enrolment;
  onReplaced: () => void;
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
  const [replacing, setReplacing] = useState(false);

  async function replace(event: FormEvent) {
    event.preventDefault();
    setProblems({});
    setRefusal(undefined);
    setReplacing(true);

    try {
      const { response, error } = await api.PUT("/second-factor", {
        body: {
          password,
          secondFactorCode: usingBackupCode ? null : secondFactorCode,
          backupCode: usingBackupCode ? backupCode : null,
          newSecondFactorCode,
          ticket: enrolment.ticket,
        },
      });

      if (response.status === 204) {
        onReplaced();
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

      setRefusal("This installation refused the re-enrolment.");
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setReplacing(false);
    }
  }

  return (
    <form onSubmit={replace}>
      <h3>Confirm the replacement</h3>

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

      {usingBackupCode ? (
        <label>
          A backup code off the sheet you have now
          <input value={backupCode} onChange={(e) => setBackupCode(e.target.value)} />
        </label>
      ) : (
        <label>
          The six digits from the authenticator you have now
          <input
            inputMode="numeric"
            autoComplete="one-time-code"
            value={secondFactorCode}
            onChange={(e) => setSecondFactorCode(e.target.value)}
            aria-invalid={problems.secondFactorCode !== undefined || undefined}
          />
        </label>
      )}
      {problems.secondFactorCode !== undefined && (
        <p className="refusal">{problems.secondFactorCode}</p>
      )}

      {/* The phone that is already gone is the case this whole screen exists
          for, so the code that stands in for it is one click away rather than
          something to be found. */}
      <button
        type="button"
        className="plain"
        onClick={() => setUsingBackupCode(!usingBackupCode)}
      >
        {usingBackupCode
          ? "Use the authenticator I have now"
          : "The old phone is gone — use a backup code"}
      </button>

      <label>
        The six digits from the authenticator you just enrolled
        <input
          inputMode="numeric"
          autoComplete="one-time-code"
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

      <button type="submit" disabled={replacing}>
        Replace it
      </button>
    </form>
  );
}
