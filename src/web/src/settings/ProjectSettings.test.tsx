import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { GroupsProvider } from "../projects/groups";
import { ProjectsProvider } from "../projects/projects";
import { ProjectSettings } from "./ProjectSettings";
import {
  aFootprint,
  aGroup,
  aHost,
  aProject,
  anInstallationAnswering,
  noGroups,
  noHosts,
  type Answer,
} from "../shared/testing";

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
    "GET /groups": noGroups,
    "GET /hosts": noHosts,
    "GET /projects": { body: [aProject({ id: "p1", name: "checkout", retentionDays: 30 })] },
    "GET /projects/p1/ingest-tokens": ONE_TOKEN,

    // The window in the box costs something, and the field says what while it
    // is being chosen. Every opening of this area asks it.
    "GET /projects/p1/retention/footprint": aFootprint({ retentionDays: 30 }),
    ...routes,
  });

  render(
    <MemoryRouter initialEntries={[at]}>
      <ProjectsProvider>
        <GroupsProvider>
          <Routes>
            <Route path="/project/:id/settings" element={<ProjectSettings />} />
            <Route path="/project/:id/settings/:section" element={<ProjectSettings />} />
            <Route path="/" element={<h1>Projects</h1>} />
          </Routes>
        </GroupsProvider>
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

    // The project is read off the list the shell already fetched, the groups
    // off the one beside it, and the tokens belong to an area nobody opened.
    //
    // The hosts are the one thing this area does ask for, and they are the
    // reason this rule is stated as *what an area needs* rather than *nothing*:
    // a field offering the machines to run on cannot offer them without them.
    // They are not a provider beside the groups because their answer carries
    // when each host last reported, read across the sample table — a cost every
    // sign-in should not pay for a field on one area.
    expect([...installation.asked].sort()).toEqual([
      "GET /groups",
      "GET /hosts",
      "GET /projects",
    ]);
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

  it("says what the window in the box will cost, without refusing it", async () => {
    open({
      "GET /projects/p1/retention/footprint": aFootprint({
        retentionDays: 30,
        heldBytes: 12_000_000_000,
        impliedBytes: 43_000_000_000,
        diskFreeBytes: 220_000_000_000,
        diskTotalBytes: 500_000_000_000,
      }),
    });

    // What the ceiling used to do: three numbers and the operator's own
    // decision (ADR 0048). Nothing here is a threshold and nothing is refused —
    // the window implies four times what the installation holds and the button
    // still works.
    expect(await screen.findByText("12.0 GB")).toBeInTheDocument();
    expect(screen.getByText("43.0 GB")).toBeInTheDocument();
    expect(screen.getByText(/220 GB of 500 GB/)).toBeInTheDocument();

    // Three numbers and no fourth thing: no threshold, no warning and nothing
    // that says the window is too large — the arithmetic is advisory.
    expect(screen.queryByText(/too (large|much)/i)).toBeNull();
  });

  it("says nothing rather than guessing for a project without a fortnight behind it", async () => {
    open({
      "GET /projects/p1/retention/footprint": aFootprint({
        retentionDays: 30,
        impliedBytes: null,
      }),
    });

    // Two days multiplied up by a year is a guess with a number on it, and the
    // first fortnight is exactly when somebody is choosing the window.
    expect(await screen.findByText(/less than a fortnight of history/i)).toBeInTheDocument();
  });

  it("shows the first two numbers when the installation names no host", async () => {
    open({
      "GET /projects/p1/retention/footprint": aFootprint({
        retentionDays: 30,
        impliedBytes: 43_000_000_000,
      }),
    });

    expect(await screen.findByText("43.0 GB")).toBeInTheDocument();

    // Absent rather than refusing to render: an installation on no host is the
    // ordinary one, not a degraded one.
    expect(screen.queryByText(/free on the disk/i)).toBeNull();
  });

  it("costs the window in the field rather than the one in force", async () => {
    open({
      "GET /projects/p1/retention/footprint": aFootprint({
        retentionDays: 365,
        impliedBytes: 48_000_000_000,
      }),
    });

    const operator = userEvent.setup();
    const field = await screen.findByLabelText(/kept for/i);

    await operator.clear(field);
    await operator.type(field, "365");

    // It follows the field and not the change: a year is what the operator is
    // considering, and what it costs is the thing they are deciding on.
    expect(await screen.findByText("48.0 GB")).toBeInTheDocument();
  });

  it("drops a cost about a window the operator has since moved on from", async () => {
    open({
      // The window that was asked about, echoed back — and it is not the one in
      // the field any more.
      "GET /projects/p1/retention/footprint": aFootprint({
        retentionDays: 30,
        impliedBytes: 4_000_000_000,
      }),
    });

    const operator = userEvent.setup();
    const field = await screen.findByLabelText(/kept for/i);

    await operator.clear(field);
    await operator.type(field, "365");

    await waitFor(() => expect(screen.queryByText("4.00 GB")).toBeNull());
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

describe("the group", () => {
  it("moves the project under another heading and nothing else", async () => {
    const installation = open({
      "GET /groups": { body: [aGroup({ id: "g1", name: "shop" })] },
      "PUT /projects/p1/group": {},
    });

    const operator = userEvent.setup();

    await operator.selectOptions(await screen.findByLabelText("Group"), "g1");

    await waitFor(() => expect(installation.asked).toContain("PUT /projects/p1/group"));
    expect(await screen.findByText("Moved.")).toBeInTheDocument();
  });

  it("refuses a move into a group that already holds the name", async () => {
    open({
      "GET /groups": { body: [aGroup({ id: "g1", name: "shop" })] },
      "PUT /projects/p1/group": { status: 409 },
    });

    const operator = userEvent.setup();

    await operator.selectOptions(await screen.findByLabelText("Group"), "g1");

    // Renaming a project nobody asked to rename is not this screen's to do.
    expect(
      await screen.findByText(/already holds a project by this name/i),
    ).toBeInTheDocument();
  });

  it("points at where a group is made when there are none", async () => {
    open({});

    // A screen about one project is the wrong place to bring into existence a
    // thing that outlives it (`docs/ui.md`).
    expect(
      await screen.findByRole("link", { name: /make one in the installation's settings/i }),
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

describe("the machine a project runs on", () => {
  it("offers the hosts and puts the project on one", async () => {
    const installation = open({
      "GET /hosts": { body: [aHost({ id: "h1", name: "web-01" })] },
      "PUT /projects/p1/host": { status: 204 },
      "GET /projects": [
        { body: [aProject({ id: "p1", name: "checkout" })] },
        { body: [aProject({ id: "p1", name: "checkout", hostId: "h1" })] },
      ],
    });

    // The field waits for the hosts, which this area asks for on the way in.
    await screen.findByRole("option", { name: "web-01" });

    await userEvent.selectOptions(screen.getByLabelText("Host"), "h1");

    await waitFor(() =>
      expect(installation.sentTo("PUT /projects/p1/host")).toEqual([{ hostId: "h1" }]),
    );
  });

  it("takes a project off every machine, which is where every project starts", async () => {
    const installation = open({
      "GET /hosts": { body: [aHost({ id: "h1", name: "web-01" })] },
      "PUT /projects/p1/host": { status: 204 },
      "GET /projects": { body: [aProject({ id: "p1", name: "checkout", hostId: "h1" })] },
    });

    await screen.findByRole("option", { name: "web-01" });

    await userEvent.selectOptions(screen.getByLabelText("Host"), "");

    await waitFor(() =>
      expect(installation.sentTo("PUT /projects/p1/host")).toEqual([{ hostId: null }]),
    );
  });

  // A host is made in the installation's settings, for the reason a group is:
  // a screen about one project is the wrong place to bring into existence a
  // thing that outlives it.
  it("sends the operator elsewhere to make one, and offers no field until there is", async () => {
    open({});

    expect(await screen.findByRole("link", { name: /Make a host in the/ })).toHaveAttribute(
      "href",
      "/settings/hosts",
    );
    expect(screen.queryByLabelText("Host")).toBeNull();
  });
});

describe("the mute", () => {
  it("takes this project out of the conditions, and puts it back in", async () => {
    const installation = open({
      "PUT /projects/p1/muted": { status: 204 },
      "GET /projects": [
        { body: [aProject({ id: "p1", name: "checkout" })] },
        { body: [aProject({ id: "p1", name: "checkout", muted: true })] },
      ],
    });

    const box = await screen.findByLabelText("Do not evaluate this project's conditions");

    // Every project is evaluated until the operator says otherwise.
    expect(box).not.toBeChecked();

    await userEvent.click(box);

    await waitFor(() =>
      expect(installation.sentTo("PUT /projects/p1/muted")).toEqual([{ muted: true }]),
    );

    await waitFor(() => expect(box).toBeChecked());
  });

  // One flag rather than a mute per condition: the switch and this checkbox are
  // the whole of what is adjustable about alerting (ADR 0050).
  it("offers one checkbox and no per-condition anything", async () => {
    open({});

    await screen.findByLabelText("Do not evaluate this project's conditions");

    expect(screen.queryByLabelText(/gone quiet/i)).toBeNull();
    expect(screen.queryByLabelText(/flooding/i)).toBeNull();
  });
});
