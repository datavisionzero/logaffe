import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { Field } from "../components/Field";
import { Callout, Gate, PageTitle, Snippet } from "../components/Page";
import { PASSWORD_MINIMUM } from "../session/password";
import { formatTimestamp } from "../shared/time";

/**
 * This installation cannot be claimed over the network, and no request will
 * ever be right again until the host opens the way in.
 *
 * Two states arrive here: a window that lapsed, and an installation in secret
 * mode holding no secret to present to. Both name the command that opens it
 * again rather than leaving an operator who is already having a bad minute to
 * search for it (`docs/setup.md`).
 */
export function CannotBeClaimed({ needsSecret }: { needsSecret: boolean }) {
  return (
    <Gate>
      <PageTitle>This installation cannot be claimed</PageTitle>
      {needsSecret ? (
        <p>
          It is guarded by a claim secret and holds none, so there is nothing to present
          to it.
        </p>
      ) : (
        <p>
          The claim window opened when this installation first ran and has since lapsed.
          Claiming over the network is over, and a restart does not open it again.
        </p>
      )}
      <div className="grid gap-3">
        <p>Run this on the host the installation runs on, and reload this page:</p>
        <Snippet>docker compose exec logaffe logaffe recover</Snippet>
      </div>
      <p className="quiet">
        It returns the installation to unclaimed and opens the way in again — a fresh
        claim secret, or a fresh window. Projects, ingest tokens and log entries are
        untouched.
      </p>
    </Gate>
  );
}

/**
 * The whole reachable surface of an installation nobody owns.
 *
 * **It is one act and one request** (ADR 0014): a password, and the claim secret
 * on an installation guarded by one. The second factor is not here — it is the
 * operator's to enrol afterwards (ADR 0041) — so there is nothing to carry
 * between two steps, nothing stored until the request succeeds, and nothing to
 * clean up if the screen is abandoned.
 */
export function ClaimScreen({
  needsSecret,
  closesAt,
  onClaimed,
}: {
  needsSecret: boolean;
  closesAt: string | null;
  onClaimed: () => void;
}) {
  const [secret, setSecret] = useState("");
  const [password, setPassword] = useState("");
  const [again, setAgain] = useState("");
  const [problems, setProblems] = useState<{ secret?: string; password?: string }>({});
  const [refusal, setRefusal] = useState<string>();
  const [claiming, setClaiming] = useState(false);

  const tooShort = password.length > 0 && password.length < PASSWORD_MINIMUM;
  const mismatched = again.length > 0 && again !== password;

  async function claim(event: FormEvent) {
    event.preventDefault();
    setRefusal(undefined);
    setProblems({});

    if (password.length < PASSWORD_MINIMUM || again !== password) {
      setRefusal(`Give a password of at least ${PASSWORD_MINIMUM} characters, twice.`);
      return;
    }

    setClaiming(true);

    try {
      const { response, error } = await api.POST("/claim", {
        body: { password, secret: needsSecret ? secret : null },
      });

      if (response.status === 204) {
        // The claim signed them in; there is nothing to say and nowhere to
        // point but the installation itself.
        onClaimed();
        return;
      }

      if (response.status === 400) {
        // Every refusal here says which box was wrong. The person on the other
        // end is setting up their own installation, and what this says about
        // the secret is only whether the one presented was right.
        setProblems({
          secret: problemWith(error, "secret"),
          password: problemWith(error, "password"),
        });
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
    <Gate>
      <div className="grid gap-3">
        <PageTitle>Claim this installation</PageTitle>
        <p>
          This installation belongs to nobody yet, and whoever finishes below is its
          operator. There is one account and no second one afterwards.
        </p>
        {closesAt !== null && (
          <Callout>
            The claim window closes at{" "}
            <time dateTime={closesAt} className="font-mono">
              {formatTimestamp(new Date(closesAt))}
            </time>
            . After that it is opened again only from the host.
          </Callout>
        )}
      </div>

      <form onSubmit={claim} className="grid max-w-md gap-3">
        {needsSecret && (
          <Field
            label="Claim secret"
            said={problems.secret}
            hint={
              <>
                It is in <code className="font-mono">claim-secret.txt</code> on the
                installation's volume, or it is the one its configuration names. Whoever
                installed this has it.
              </>
            }
          >
            <Input
              name="claim-secret"
              autoComplete="off"
              value={secret}
              onChange={(e) => setSecret(e.target.value)}
              aria-invalid={problems.secret !== undefined || undefined}
            />
          </Field>
        )}

        <Field
          label="Password"
          hint={`At least ${PASSWORD_MINIMUM} characters, and nothing else is asked of it. Length is the property that matters — and until you enrol a second factor afterwards, it is the only credential on the account.`}
          said={
            tooShort
              ? `A password is at least ${PASSWORD_MINIMUM} characters.`
              : problems.password
          }
        >
          <Input
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={tooShort || problems.password !== undefined || undefined}
          />
        </Field>

        {/* Typed twice, which is furniture the product avoids everywhere else
            and earns here: there is no password reset over the network and no
            email to send one to (ADR 0015), so a typo at this step is answered
            by Host Recovery and nothing smaller. */}
        <Field
          label="Password again"
          said={mismatched ? "These two are not the same." : undefined}
        >
          <Input
            type="password"
            autoComplete="new-password"
            value={again}
            onChange={(e) => setAgain(e.target.value)}
            aria-invalid={mismatched || undefined}
          />
        </Field>

        {refusal !== undefined && <p className="refusal text-sm">{refusal}</p>}

        <Button type="submit" disabled={claiming} className="mt-1 w-fit">
          Claim this installation
        </Button>
      </form>
    </Gate>
  );
}

/**
 * The refusals that are not about a box on the form. Two people can reach this
 * screen at once, and the one who sends second meets the conflict.
 */
function refusalFor(status: number): string {
  switch (status) {
    case 403:
      return "This installation cannot be claimed over the network until the host opens the way in.";
    case 409:
      return "This installation now has an operator. Somebody else finished first.";
    case 429:
      return "Too many attempts from here. Wait a moment and try again.";
    default:
      return "This installation refused the claim.";
  }
}
