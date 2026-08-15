import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { ProjectsProvider } from "../projects/projects";
import { ProjectSettings } from "./ProjectSettings";
import { aProject, anInstallationAnswering, type Answer } from "../shared/testing";

/** One of a project's tokens, carrying no secret — a list decrypts nothing. */
function anIngestToken(token: {
  id: string;
  identifier: string;
  issuedAt?: string;
  lastUsedAt?: string | null;
}) {
  return {
    id: token.id,
    identifier: token.identifier,
    issuedAt: token.issuedAt ?? "2026-08-01T09:00:00.000Z",
    lastUsedAt: token.lastUsedAt ?? null,
  };
}

const ONE_TOKEN: Answer = {
  body: [anIngestToken({ id: "t1", identifier: "3kf9q2" })],
};

/** The screen at one of its areas, which is an address like any other. */
function open(routes: Record<string, Answer | Answer[]>, at = "/project/p1/settings") {
  const installation = anInstallationAnswering({
    "GET /projects": { body: [aProject({ id: "p1", name: "checkout", retentionDays: 30 })] },
    "GET /projects/p1/ingest-tokens": ONE_TOKEN,
    ...routes,
  });

  render(
    <MemoryRouter initialEntries={[at]}>
      <ProjectsProvider>
        <Routes>
          <Route path="/project/:id/settings" element={<ProjectSettings />} />
          <Route path="/project/:id/settings/:section" element={<ProjectSettings />} />
          <Route path="/" element={<h1>Projects</h1>} />
        </Routes>
      </ProjectsProvider>
    </MemoryRouter>,
  );

  return installation;
}

/** The area holding the tokens a project is delivered to on. */
function openTokens(routes: Record<string, Answer | Answer[]> = {}) {
  return open(routes, "/project/p1/settings/tokens");
}

afterEach(() => vi.unstubAllGlobals());

describe("the areas", () => {
  it("keeps the act that cannot be undone off the area the screen opens at", async () => {
    open({});

    await screen.findByLabelText("Name");

    // It is arrived at rather than scrolled past: the rail names it, and
    // nothing on the way in offers it.
    expect(screen.queryByRole("button", { name: "Delete checkout" })).toBeNull();
    expect(
      within(screen.getByRole("navigation", { name: "Settings" })).getByRole("link", {
        name: "Delete this project",
      }),
    ).toBeInTheDocument();
  });

  it("asks the installation for nothing until an area needs something", async () => {
    const installation = open({});

    await screen.findByLabelText("Name");

    // The project is read off the list the shell already fetched, and the
    // tokens belong to an area nobody has opened.
    expect(installation.asked).toEqual(["GET /projects"]);
  });
});

describe("the retention window", () => {
  it("says how many entries lowering it removes, before it is applied", async () => {
    const installation = open({
      "GET /projects/p1/retention/outside": { body: { retentionDays: 7, entries: 4210 } },
      "PUT /projects/p1/retention": {},
    });

    const operator = userEvent.setup();
    const field = await screen.findByLabelText(/kept for/i);

    await operator.clear(field);
    await operator.type(field, "7");
    await operator.click(screen.getByRole("button", { name: /change the window/i }));

    expect(await screen.findByText(/4210 entries/)).toBeInTheDocument();

    // A settings field that silently destroys data is a bad settings field: the
    // count stands in front of the change, and nothing has been written yet.
    expect(installation.asked).not.toContain("PUT /projects/p1/retention");

    await operator.click(screen.getByRole("button", { name: /lower it and remove them/i }));

    await waitFor(() =>
      expect(installation.asked).toContain("PUT /projects/p1/retention"),
    );
  });

  it("counts nothing when the window is raised, because nothing leaves", async () => {
    const installation = open({ "PUT /projects/p1/retention": {} });

    const operator = userEvent.setup();
    const field = await screen.findByLabelText(/kept for/i);

    await operator.clear(field);
    await operator.type(field, "60");
    await operator.click(screen.getByRole("button", { name: /change the window/i }));

    expect(await screen.findByText(/kept for 60 days now/i)).toBeInTheDocument();
    expect(installation.asked).not.toContain("GET /projects/p1/retention/outside");
  });

  it("drops a count about a window the operator has since moved on from", async () => {
    open({
      // The window that was asked about, echoed back — and this is not the one
      // in the field any more.
      "GET /projects/p1/retention/outside": { body: { retentionDays: 7, entries: 4210 } },
    });

    const operator = userEvent.setup();
    const field = await screen.findByLabelText(/kept for/i);

    await operator.clear(field);
    await operator.type(field, "14");
    await operator.click(screen.getByRole("button", { name: /change the window/i }));

    await waitFor(() =>
      expect(screen.queryByText(/lower it and remove them/i)).toBeNull(),
    );
    expect(screen.queryByText(/4210/)).toBeNull();
  });
});

describe("the name", () => {
  it("says that renaming moves nothing", async () => {
    open({});

    expect(await screen.findByText(/no sender notices/i)).toBeInTheDocument();
  });

  it("says which name is already taken", async () => {
    open({ "PATCH /projects/p1": { status: 409 } });

    const operator = userEvent.setup();

    await operator.clear(await screen.findByLabelText("Name"));
    await operator.type(screen.getByLabelText("Name"), "billing");
    await operator.click(screen.getByRole("button", { name: /rename the project/i }));

    expect(
      await screen.findByText(/already holds a project by that name/i),
    ).toBeInTheDocument();
  });
});

describe("the ingest tokens", () => {
  it("shows a last use to the minute and never finer", async () => {
    openTokens({
      "GET /projects/p1/ingest-tokens": {
        body: [
          anIngestToken({
            id: "t1",
            identifier: "3kf9q2",
            lastUsedAt: "2026-08-08T11:42:07.318Z",
          }),
        ],
      },
    });

    const row = (await screen.findByText("3kf9q2")).closest("tr")!;
    const [, lastUsed] = within(row).getAllByRole("time");

    // It is written only when the stored value is absent or more than five
    // minutes old (ADR 0033), so the seconds this interface shows everywhere
    // else would be two digits of invention.
    expect(lastUsed!.textContent).toMatch(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/);
  });

  it("tells a token never deployed from one that has gone quiet", async () => {
    openTokens();

    expect(await screen.findByText("Never used")).toBeInTheDocument();
  });

  it("hands over the delivery that arrives with the token", async () => {
    openTokens({
      "GET /ingest-tokens/t1/token": {
        body: {
          token: "logaffe_ingest_3kf9q2_secret",
          deliverySnippet: "curl -H 'Authorization: Bearer logaffe_ingest_3kf9q2_secret'",
        },
      },
    });

    const operator = userEvent.setup();

    await operator.click(await screen.findByRole("button", { name: /show the delivery/i }));

    expect(await screen.findByText(/curl -H/)).toBeInTheDocument();
  });

  it("refuses a third token in the project's own terms", async () => {
    openTokens({
      "GET /projects/p1/ingest-tokens": {
        body: [
          anIngestToken({ id: "t1", identifier: "3kf9q2" }),
          anIngestToken({ id: "t2", identifier: "7hb1zz" }),
        ],
      },
    });

    // Two is what moving deployments over one at a time needs, and the act that
    // would make a third is not offered at all.
    await screen.findByText("7hb1zz");
    expect(screen.queryByRole("button", { name: /issue a/i })).toBeNull();
  });

  it("names the closed door of a project holding none", async () => {
    openTokens({ "GET /projects/p1/ingest-tokens": { body: [] } });

    expect(await screen.findByText(/nothing can deliver to it/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /issue an ingest token/i })).toBeInTheDocument();
  });
});

describe("deleting a project", () => {
  it("is guarded by typing the name, and the route is told none of it", async () => {
    const installation = open({ "DELETE /projects/p1": {} }, "/project/p1/settings/delete");

    const operator = userEvent.setup();
    const act = await screen.findByRole("button", { name: "Delete checkout" });

    expect(act).toBeDisabled();

    await operator.type(screen.getByLabelText(/to confirm/i), "checkou");
    expect(act).toBeDisabled();

    await operator.type(screen.getByLabelText(/to confirm/i), "t");
    expect(act).toBeEnabled();

    await operator.click(act);

    // Where an operator who has just deleted the project they were in lands.
    expect(await screen.findByRole("heading", { name: "Projects" })).toBeInTheDocument();

    const deletion = installation.asked.filter((route) => route.startsWith("DELETE"));

    expect(deletion).toEqual(["DELETE /projects/p1"]);
  });
});
