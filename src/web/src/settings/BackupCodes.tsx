import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import { BackupCodeSheet } from "../session/Enrolment";

/**
 * A fresh sheet, asked for on its own.
 *
 * It requires the password, because ten of these are ten ways past the second
 * factor. It **ends no session**: replacing the way back in says nothing about
 * the browsers already signed in (`docs/sign-in.md`), which is the one thing
 * that distinguishes it from the two acts around it.
 *
 * There is nothing to issue on an account with no second factor, since a code
 * that stands in for one that is not there stands in for nothing (ADR 0041).
 */
export function BackupCodes() {
  const [password, setPassword] = useState("");
  const [codes, setCodes] = useState<string[]>();
  const [problem, setProblem] = useState<string>();
  const [issuing, setIssuing] = useState(false);

  async function issue(event: FormEvent) {
    event.preventDefault();
    setProblem(undefined);
    setIssuing(true);

    try {
      const { data, response, error } = await api.POST("/backup-codes", {
        body: { password },
      });

      if (data !== undefined) {
        setCodes(data.codes);
        setPassword("");
        return;
      }

      setProblem(
        response.status === 400
          ? problemWith(error, "password")
          : response.status === 409
            ? "There is no second factor on this account for these to stand in for. "
              + "Enrol one above, and a sheet comes with it."
            : "This installation refused to issue a sheet.",
      );
    } catch {
      setProblem("This installation did not answer.");
    } finally {
      setIssuing(false);
    }
  }

  return (
    <section>
      <h2>Backup codes</h2>
      <p>
        Ten codes that stand in for the second factor, each used once. A fresh set
        replaces the previous one entirely — spent codes and unspent alike — and no
        session ends: replacing the way back in says nothing about the browsers already
        signed in.
      </p>
      <p className="quiet">
        How many are left is said whenever one is spent signing in, because a set that
        quietly runs out ends at Host Recovery.
      </p>

      {codes === undefined ? (
        <form onSubmit={issue}>
          <label>
            Password
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              aria-invalid={problem !== undefined || undefined}
            />
          </label>
          {problem !== undefined && <p className="refusal">{problem}</p>}

          <button type="submit" disabled={issuing}>
            Issue a fresh sheet
          </button>
        </form>
      ) : (
        <>
          <BackupCodeSheet codes={codes} replacing />
          <button type="button" className="plain" onClick={() => setCodes(undefined)}>
            Done — hide them
          </button>
        </>
      )}
    </section>
  );
}
