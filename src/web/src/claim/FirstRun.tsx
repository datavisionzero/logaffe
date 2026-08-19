import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router";
import { api, problemWith } from "../api/client";
import { RETENTION_MAXIMUM, RETENTION_MINIMUM, RETENTION_OFFERED } from "../projects/retention";
import { copyToClipboard, whyNotCopied, type Copying } from "../shared/clipboard";
import { EnrolSecondFactor } from "../session/EnrolSecondFactor";

type Step =
  | { at: "second-factor" }
  | { at: "project" }
  | { at: "token"; projectId: string; name: string }
  | { at: "snippet"; projectId: string; name: string; snippet: string };

/**
 * What follows the claim: **a guide, not a stage** (`docs/setup.md`).
 *
 * It offers the second factor first, then the first project, and hands over a
 * copy-paste delivery pointed at this installation with the ingest token already
 * in it — because `VISION.md` makes ingestion friction the adoption barrier and
 * the shortest path from a running installation to a log arriving is a snippet
 * the operator does not have to assemble from documentation.
 *
 * **The second factor comes first because it is the one thing on the list that
 * nothing else reminds them of later** except the banner, and because it costs a
 * phone that is already in their hand. Skipping it is a decision (ADR 0041), not
 * an oversight, and the interface keeps saying so.
 *
 * **The backend knows nothing about it.** This is the act that creates a
 * project and the act that issues an ingest token, walked in order by the
 * interface. There is no endpoint that reports how far along it is: a guide
 * that holds no state has no progress to report, and one that reported it would
 * be the stage this is not.
 *
 * **Skipping is final for this claim.** It can be left at any step and nothing
 * is half-configured if it is — the installation was fully claimed the moment
 * the claim completed. It cannot be walked back into, because it cannot know it
 * was skipped, and a route that returned here would start being the stage the
 * document says it is not. An installation with no projects afterwards shows the
 * ordinary empty project list, which already offers the act that creates one.
 */
export function FirstRun({ onDone }: { onDone: () => void }) {
  const navigate = useNavigate();
  const [step, setStep] = useState<Step>({ at: "second-factor" });

  function leave(projectId?: string) {
    if (projectId !== undefined) {
      void navigate(`/project/${projectId}`);
    }

    onDone();
  }

  return (
    <main>
      <h1>This installation is yours</h1>
      <p>
        The account is made and you are signed in. What is left is a second factor,
        somewhere for entries to arrive, and something to deliver them with — three
        steps, and you can leave at any point.
      </p>

      {step.at === "second-factor" ? (
        <TheSecondFactor onDone={() => setStep({ at: "project" })} />
      ) : step.at === "project" ? (
        <FirstProject
          onCreated={(projectId, name) => setStep({ at: "token", projectId, name })}
          onSkipped={() => leave()}
        />
      ) : step.at === "token" ? (
        <FirstToken
          name={step.name}
          projectId={step.projectId}
          onIssued={(snippet) => setStep({ ...step, at: "snippet", snippet })}
          onSkipped={() => leave(step.projectId)}
          onRefused={() => onDone()}
        />
      ) : (
        <TheDelivery
          name={step.name}
          snippet={step.snippet}
          onDone={() => leave(step.projectId)}
        />
      )}
    </main>
  );
}

/**
 * The offer the claim no longer makes.
 *
 * The second factor is optional and is enrolled from the settings by a
 * signed-in operator (ADR 0041) — which this operator is, one screen after
 * claiming, with the account fresh in their hands. That is the moment it costs
 * least, and it is the reason the guide opens with it rather than closing with
 * it.
 *
 * Declining is a plain button and not a dare. What follows an installation with
 * no second factor is a banner that does not go away, which is the honest way to
 * hold somebody to a decision they made on purpose.
 */
function TheSecondFactor({ onDone }: { onDone: () => void }) {
  const [enrolled, setEnrolled] = useState(false);

  return (
    <section>
      <h2>1. A second factor</h2>
      <p>
        A code from an authenticator app, asked for after your password. This account can
        see and do everything in an installation reachable from the internet, and a
        password on its own is all that stands in front of it until you enrol one.
      </p>

      {enrolled ? (
        <>
          <p className="quiet">
            Enrolled. Keep the backup codes somewhere that is not the phone.
          </p>
          <button type="button" onClick={onDone}>
            Continue
          </button>
        </>
      ) : (
        <>
          <EnrolSecondFactor replacing={false} onEnrolled={() => setEnrolled(true)}>
            <p>
              Nothing is stored until the last step. Leaving this screen costs the
              enrolment and nothing else.
            </p>
          </EnrolSecondFactor>

          <button type="button" className="plain" onClick={onDone}>
            Skip this
          </button>
        </>
      )}
    </section>
  );
}

/**
 * The first project. A name and a retention window is the whole of it
 * (`docs/projects.md`), which is the same act the project list offers and is
 * written out here rather than shared, because that one navigates into what it
 * made and this one has a second step to walk first.
 */
function FirstProject({
  onCreated,
  onSkipped,
}: {
  onCreated: (projectId: string, name: string) => void;
  onSkipped: () => void;
}) {
  const [name, setName] = useState("");
  const [retentionDays, setRetentionDays] = useState(String(RETENTION_OFFERED));
  const [refusal, setRefusal] = useState<string>();
  const [problems, setProblems] = useState<{ name?: string; retentionDays?: string }>({});
  const [creating, setCreating] = useState(false);

  async function create(event: FormEvent) {
    event.preventDefault();
    setRefusal(undefined);
    setProblems({});
    setCreating(true);

    try {
      const { data, response, error } = await api.POST("/projects", {
        // In no group, and the guide offers no choice about it: an installation
        // being claimed holds none, and the first project is the one thing
        // standing between the operator and their first delivery.
        body: { name, retentionDays: Number(retentionDays), groupId: null },
      });

      if (data !== undefined) {
        onCreated(data.id, data.name);
        return;
      }

      if (response.status === 409) {
        setProblems({ name: "This installation already holds a project by that name." });
        return;
      }

      if (response.status === 400) {
        setProblems({
          name: problemWith(error, "name"),
          retentionDays: problemWith(error, "retentionDays"),
        });
        return;
      }

      setRefusal("This installation refused to create the project.");
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setCreating(false);
    }
  }

  return (
    <form onSubmit={create}>
      <h2>2. A project for the entries to arrive in</h2>
      <p>
        One per application is the usual shape. Nothing creates one implicitly, and there
        can be as many as you like afterwards.
      </p>

      <label>
        Name
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          aria-invalid={problems.name !== undefined || undefined}
        />
      </label>
      {problems.name !== undefined && <p className="refusal">{problems.name}</p>}

      <label>
        Kept for
        <input
          type="number"
          min={RETENTION_MINIMUM}
          max={RETENTION_MAXIMUM}
          value={retentionDays}
          onChange={(e) => setRetentionDays(e.target.value)}
          aria-invalid={problems.retentionDays !== undefined || undefined}
        />
        days
      </label>
      {problems.retentionDays !== undefined && <p className="refusal">{problems.retentionDays}</p>}

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      <button type="submit" disabled={creating}>
        Create the project
      </button>
      <button type="button" className="plain" onClick={onSkipped}>
        Skip this
      </button>
    </form>
  );
}

/**
 * The second act, and the one that produces the handover: issuing a token hands
 * back the delivery to paste, because reading a token back and being able to use
 * it are one errand.
 */
function FirstToken({
  name,
  projectId,
  onIssued,
  onSkipped,
  onRefused,
}: {
  name: string;
  projectId: string;
  onIssued: (snippet: string) => void;
  onSkipped: () => void;
  onRefused: () => void;
}) {
  const [refusal, setRefusal] = useState<string>();
  const [issuing, setIssuing] = useState(false);

  async function issue() {
    setRefusal(undefined);
    setIssuing(true);

    try {
      const { data, response } = await api.POST("/projects/{projectId}/ingest-tokens", {
        params: { path: { projectId } },
      });

      if (data !== undefined) {
        onIssued(data.deliverySnippet);
        return;
      }

      if (response.status === 401) {
        // The session went while the guide was open. The shell behind this
        // stands the sign-in up, which is the one place that is said.
        onRefused();
        return;
      }

      setRefusal("This installation refused to issue the token.");
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setIssuing(false);
    }
  }

  return (
    <section>
      <h2>3. A token to deliver with</h2>
      <p>
        <strong>{name}</strong> exists. An ingest token is what an application presents to
        write into it — it permits writing and grants no read access of any kind, and the
        token is the project, so a delivery never names one.
      </p>

      {refusal !== undefined && <p className="refusal">{refusal}</p>}

      <button type="button" disabled={issuing} onClick={() => void issue()}>
        Issue an ingest token
      </button>
      <button type="button" className="plain" onClick={onSkipped}>
        Skip this
      </button>
    </section>
  );
}

/**
 * The handover, which is **the plain path**: an address, a header and one CLEF
 * line, needing nothing installed and working from any language.
 *
 * The timestamp is generated when the line is sent rather than when the token
 * was issued — the UI orders by `@t`, and a snippet carrying a fixed one would
 * deliver an entry dated whenever this page happened to be open. The cost is
 * that it is a POSIX shell line. The Serilog form of the same handover waits on
 * the package being published (`docs/setup.md`).
 */
function TheDelivery({
  name,
  snippet,
  onDone,
}: {
  name: string;
  snippet: string;
  onDone: () => void;
}) {
  const [copying, setCopying] = useState<Copying>();

  return (
    <section>
      <h2>Send something to it</h2>
      <p>
        This is pointed at this installation with the token already in it. Run it, and the
        entry is in <strong>{name}</strong>.
      </p>
      <pre>{snippet}</pre>
      <button
        type="button"
        onClick={() => void copyToClipboard(snippet).then(setCopying)}
      >
        {copying === "copied" ? "Copied" : "Copy the delivery"}
      </button>
      {whyNotCopied(copying) !== undefined && (
        <p className="refusal">{whyNotCopied(copying)}</p>
      )}
      <p className="quiet">
        The token can be read back at any time from the project's settings, so there is
        nothing here to write down.
      </p>
      <button type="button" onClick={onDone}>
        Take me to {name}
      </button>
    </section>
  );
}
