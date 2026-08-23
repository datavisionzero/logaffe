import { useState } from "react";
import { Link } from "react-router";
import { api } from "../api/client";
import type { HeldProject } from "../projects/projects";

/**
 * Whether this project's alert conditions are evaluated.
 *
 * It is here rather than in the installation's settings for the reason the
 * group and the host are here: it is a fact about this project and about
 * nothing else. The three switches are the installation's — what it will say
 * something about at all — and this is the one project taken out of them.
 *
 * **One checkbox and not one per condition.** The project a batch job writes
 * into at three in the morning is the project whose silence at four is not an
 * incident either, so the two conditions are muted by the same fact — and a
 * mute per condition is the beginning of the per-project configuration the
 * closed set exists to refuse (`docs/alerts.md`).
 *
 * **It changes what is evaluated and nothing else.** A muted project receives,
 * keeps and answers exactly what it did: the hourly pass simply does not ask
 * about it, so nothing is suppressed and nothing accumulates while it is muted.
 */
export function ProjectMute({
  project,
  onMuted,
}: {
  project: HeldProject;
  onMuted: () => void;
}) {
  const [problem, setProblem] = useState<string>();
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);

  async function put(muted: boolean) {
    setProblem(undefined);
    setSaved(false);
    setSaving(true);

    try {
      const { response } = await api.PUT("/projects/{id}/muted", {
        params: { path: { id: project.id } },
        body: { muted },
      });

      if (response.status === 204) {
        setSaved(true);
        onMuted();
        return;
      }

      setProblem(
        response.status === 404
          ? "This project is gone. It may have been deleted from another browser."
          : "This installation refused the change.",
      );
    } catch {
      setProblem("This installation did not answer.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <section>
      <h2>Alerts</h2>
      <p>
        This installation says something unasked on three conditions and no others: its
        store filling up, a project going quiet, and a project delivering far more than it
        does. Muting takes this project out of the two that are about a project — they are
        not evaluated for it at all, rather than evaluated and kept quiet.
      </p>

      <label className="confirm">
        <input
          type="checkbox"
          checked={project.muted}
          disabled={saving}
          onChange={(e) => void put(e.target.checked)}
        />
        Do not evaluate this project's conditions
      </label>

      <p className="quiet">
        Nothing else changes: what it receives, what it keeps and what it answers are
        exactly what they were. Which conditions this installation runs at all is{" "}
        <Link to="/settings/alerts">in the installation's settings</Link>.
      </p>

      {problem !== undefined && <p className="refusal">{problem}</p>}
      {saved && <p className="quiet">Saved.</p>}
    </section>
  );
}
