import { useEffect, useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import { EnrolSecondFactor } from "../session/EnrolSecondFactor";

/**
 * The second factor: enrolled, replaced or turned off, and this is the only
 * place any of the three happens (ADR 0041).
 *
 * It is **offered, not required**. An installation whose operator declined runs
 * behind a password alone, which is weaker and is said so here rather than
 * implied — and the banner above the whole application keeps saying it, so that
 * having none stays a decision somebody made rather than a thing that happened.
 *
 * Removing it costs exactly what enrolling it costs: the password, and the
 * second factor in use. A session that has been taken is not a session that can
 * strip the account down to one credential.
 */
export function SecondFactor() {
  const [enrolled, setEnrolled] = useState<boolean>();
  const [settled, setSettled] = useState<"enrolled" | "removed">();
  const [turningOff, setTurningOff] = useState(false);
  const [refusal, setRefusal] = useState<string>();

  async function read() {
    try {
      const { data } = await api.GET("/second-factor");

      if (data !== undefined) {
        setEnrolled(data.isEnrolled);
      }
    } catch {
      setRefusal("This installation did not answer.");
    }
  }

  useEffect(() => {
    void read();
  }, []);

  return (
    <section>
      <h2>Second factor</h2>
      <p>
        A time-based code from an authenticator app, asked for after the password. It is
        yours to enrol and yours to remove, and nothing is stored until the last step
        below — so an app you have now keeps working until the moment a new one takes
        over.
      </p>
      <p>
        Enrolling issues a fresh sheet of backup codes, and every change here{" "}
        <b>ends every other session</b>.
      </p>

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      {settled === "enrolled" && (
        <p className="quiet">
          Enrolled. Any previous authenticator and sheet are gone, and every other session
          has ended.
        </p>
      )}
      {settled === "removed" && (
        <p className="quiet">
          Turned off. The backup codes went with it, and this account is now behind its
          password alone.
        </p>
      )}

      {enrolled === false && (
        <p className="notice">
          There is no second factor on this account. A password is the only thing between
          the internet and everything this installation holds.
        </p>
      )}

      {enrolled !== undefined && !turningOff && (
        <EnrolSecondFactor
          replacing={enrolled}
          onEnrolled={() => {
            setSettled("enrolled");
            setEnrolled(true);
          }}
        />
      )}

      {enrolled === true && !turningOff && (
        <button type="button" className="plain" onClick={() => setTurningOff(true)}>
          Turn the second factor off
        </button>
      )}

      {turningOff && (
        <TurnOff
          onRemoved={() => {
            setTurningOff(false);
            setSettled("removed");
            setEnrolled(false);
          }}
          onKept={() => setTurningOff(false)}
        />
      )}
    </section>
  );
}

/**
 * Removing it, which asks for the same credentials enrolling asks for.
 *
 * It says plainly what the account is left with. This is the one act in the
 * product that makes an installation weaker, and it is the operator's to make
 * with their eyes open.
 */
function TurnOff({ onRemoved, onKept }: { onRemoved: () => void; onKept: () => void }) {
  const [password, setPassword] = useState("");
  const [secondFactorCode, setSecondFactorCode] = useState("");
  const [backupCode, setBackupCode] = useState("");
  const [usingBackupCode, setUsingBackupCode] = useState(false);
  const [problems, setProblems] = useState<{ password?: string; secondFactorCode?: string }>(
    {},
  );
  const [refusal, setRefusal] = useState<string>();
  const [removing, setRemoving] = useState(false);

  async function remove(event: FormEvent) {
    event.preventDefault();
    setProblems({});
    setRefusal(undefined);
    setRemoving(true);

    try {
      const { response, error } = await api.POST("/second-factor/removal", {
        body: {
          password,
          secondFactorCode: usingBackupCode ? null : secondFactorCode,
          backupCode: usingBackupCode ? backupCode : null,
        },
      });

      if (response.status === 204) {
        onRemoved();
        return;
      }

      if (response.status === 400) {
        setProblems({
          password: problemWith(error, "password"),
          secondFactorCode: problemWith(error, "secondFactorCode"),
        });
        return;
      }

      setRefusal(
        response.status === 409
          ? "There is no second factor on this account."
          : "This installation refused to turn it off.",
      );
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setRemoving(false);
    }
  }

  return (
    <form onSubmit={remove}>
      <h3>Turn the second factor off</h3>
      <p className="notice">
        The authenticator and the backup codes go, and the password becomes the only
        credential on this account. Signing in will ask for nothing else.
      </p>

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
          A backup code off the sheet
          <input
            name="backup-code"
            autoComplete="off"
            value={backupCode}
            onChange={(e) => setBackupCode(e.target.value)}
          />
        </label>
      ) : (
        <label>
          The six digits from the authenticator
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
      )}
      {problems.secondFactorCode !== undefined && (
        <p className="refusal">{problems.secondFactorCode}</p>
      )}

      <button
        type="button"
        className="plain"
        onClick={() => setUsingBackupCode(!usingBackupCode)}
      >
        {usingBackupCode ? "Use the authenticator" : "Use a backup code instead"}
      </button>

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      <button type="submit" disabled={removing}>
        Turn it off
      </button>
      <button type="button" className="plain" onClick={onKept}>
        Keep it
      </button>
    </form>
  );
}
