import { useEffect, useState } from "react";
import { api } from "../api/client";

type Handover =
  | { status: "asking" }
  | { status: "closed" }
  | { status: "snippet"; snippet: string }
  | { status: "unreachable" };

/**
 * A project with no entries at all.
 *
 * An operator looking at an empty project is asking whether delivery works, and
 * the answer is the configuration they are about to check anyway — so this is
 * the delivery snippet with the token already in it, the same one the first-run
 * guide hands over (`docs/setup.md`).
 *
 * The snippet arrives with the token rather than being assembled here: issuing
 * one returns it and reading one back returns it again, because reading a token
 * back and being able to use it are one errand.
 *
 * **A project holding no token at all is shown the act that issues one
 * instead**, since there is nothing to deliver with yet — that is the same
 * closed door the token count on the project list names.
 */
export function EmptyProject({ projectId }: { projectId: string }) {
  const [handover, setHandover] = useState<Handover>({ status: "asking" });
  const [issuing, setIssuing] = useState(false);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    let current = true;

    setHandover({ status: "asking" });

    void (async () => {
      try {
        const { data } = await api.GET("/projects/{projectId}/ingest-tokens", {
          params: { path: { projectId } },
        });

        if (!current) {
          return;
        }

        if (data === undefined) {
          setHandover({ status: "unreachable" });
          return;
        }

        const token = data[0];

        if (token === undefined) {
          setHandover({ status: "closed" });
          return;
        }

        const read = await api.GET("/ingest-tokens/{id}/token", {
          params: { path: { id: token.id } },
        });

        if (current) {
          setHandover(
            read.data === undefined
              ? { status: "unreachable" }
              : { status: "snippet", snippet: read.data.deliverySnippet },
          );
        }
      } catch {
        if (current) {
          setHandover({ status: "unreachable" });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, [projectId]);

  async function issue() {
    setIssuing(true);

    try {
      const { data } = await api.POST("/projects/{projectId}/ingest-tokens", {
        params: { path: { projectId } },
      });

      setHandover(
        data === undefined
          ? { status: "unreachable" }
          : { status: "snippet", snippet: data.deliverySnippet },
      );
    } finally {
      setIssuing(false);
    }
  }

  return (
    <section className="empty">
      <h2>Nothing has ever arrived here</h2>

      {handover.status === "asking" && <p className="quiet">Reading the project's token…</p>}

      {handover.status === "unreachable" && (
        <p className="refusal">This installation did not answer.</p>
      )}

      {handover.status === "closed" && (
        <>
          <p>
            This project holds no ingest token, so there is nothing to deliver with yet.
            Issuing one hands back the delivery to paste.
          </p>
          <button type="button" disabled={issuing} onClick={() => void issue()}>
            Issue an ingest token
          </button>
        </>
      )}

      {handover.status === "snippet" && (
        <>
          <p>
            Send this from the application, and the entry appears above. The address, the
            header and the token are already in it.
          </p>
          <pre>{handover.snippet}</pre>
          <button
            type="button"
            onClick={() => {
              void navigator.clipboard.writeText(handover.snippet);
              setCopied(true);
            }}
          >
            {copied ? "Copied" : "Copy the delivery"}
          </button>
        </>
      )}
    </section>
  );
}
