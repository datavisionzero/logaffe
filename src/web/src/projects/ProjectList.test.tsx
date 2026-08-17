import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BrowserRouter } from "react-router";
import { Shell } from "../shell/Shell";
import {
  aGroup,
  aProject,
  anInstallationAnswering,
  noGroups,
  withSecondFactor,
} from "../shared/testing";

function open() {
  window.history.pushState({}, "", "/");

  return render(
    <BrowserRouter>
      <Shell backupCodesRemaining={null} onSignedOut={() => undefined} />
    </BrowserRouter>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("the project list", () => {
  it("says when a project last received an entry, absolute and to the millisecond", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": {
        body: [
          aProject({
            id: "3f0",
            name: "checkout",
            lastReceivedAt: "2026-08-08T11:42:07.318Z",
          }),
        ],
      },
    });

    open();

    const row = (await screen.findByRole("link", { name: "checkout" })).closest("tr")!;
    const shown = within(row).getByRole("time");

    // The rule of docs/ui.md, asserted as a shape rather than as a string: the
    // zone is the reader's and the runner's is not something to depend on. The
    // separator before the milliseconds is whichever `shared/time.ts` produces.
    expect(shown.textContent).toMatch(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}[.,]\d{3}$/);
    expect(shown).toHaveAttribute("datetime", "2026-08-08T11:42:07.318Z");
  });

  it("distinguishes a project nothing has ever delivered to from one that fell quiet", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": {
        body: [aProject({ id: "3f0", name: "checkout", lastReceivedAt: null })],
      },
    });

    open();

    expect(await screen.findByText("Nothing has ever arrived")).toBeInTheDocument();
  });

  it("names the project whose door is closed", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": {
        body: [
          aProject({ id: "3f0", name: "checkout", ingestTokens: 0 }),
          aProject({ id: "9c1", name: "billing", ingestTokens: 1 }),
        ],
      },
    });

    open();

    const closed = (await screen.findByRole("link", { name: "checkout" })).closest("tr")!;
    const open_ = screen.getByRole("link", { name: "billing" }).closest("tr")!;

    expect(within(closed).getByText(/nothing can deliver here/i)).toBeInTheDocument();
    expect(within(open_).queryByText(/nothing can deliver here/i)).toBeNull();
  });

  it("carries no count of entries beside a project", async () => {
    const installation = anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": { body: [aProject({ id: "3f0", name: "checkout" })] },
      "GET /second-factor": withSecondFactor,
    });

    open();

    await screen.findByRole("link", { name: "checkout" });

    // A dashboard is a set of counts nobody asked for over the largest table in
    // the database. The list asks for the projects and the headings they are
    // listed under, and for nothing else — the third is the shell's, asking
    // whether this account has a second factor to say so if it has not.
    expect([...installation.asked].sort()).toEqual([
      "GET /groups",
      "GET /projects",
      "GET /second-factor",
    ]);
  });

  it("lists the projects in no group first and the groups by name after them", async () => {
    anInstallationAnswering({
      "GET /groups": {
        body: [aGroup({ id: "g2", name: "shop" }), aGroup({ id: "g1", name: "blog" })],
      },
      "GET /projects": {
        body: [
          aProject({ id: "3f0", name: "checkout", groupId: "g2" }),
          aProject({ id: "9c1", name: "loose" }),
          aProject({ id: "b47", name: "api", groupId: "g2" }),
        ],
      },
    });

    open();

    await screen.findByRole("link", { name: "loose" });

    // An installation using no groups reads as it always did, so the ungrouped
    // ones carry no heading of their own and come first.
    const headings = screen
      .getAllByRole("heading", { level: 2 })
      .map((heading) => heading.textContent);

    expect(headings).toEqual(["blog", "shop"]);

    const shop = screen.getByRole("heading", { name: "shop" }).closest("section")!;

    expect(within(shop).getByRole("link", { name: "checkout" })).toBeInTheDocument();
    expect(within(shop).queryByRole("link", { name: "loose" })).toBeNull();
  });

  it("says so about a group holding nothing rather than leaving it out", async () => {
    anInstallationAnswering({
      "GET /groups": { body: [aGroup({ id: "g1", name: "shop" })] },
      "GET /projects": { body: [aProject({ id: "9c1", name: "loose" })] },
    });

    open();

    // It is something the operator made and not a side effect of what the
    // projects say, so a list that omitted it would answer where the group they
    // just created went.
    const group = (await screen.findByRole("heading", { name: "shop" })).closest("section")!;

    expect(within(group).getByText(/no projects are in this group/i)).toBeInTheDocument();
  });
});

describe("an empty installation", () => {
  it("offers the act that creates a project", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": [
        { body: [] },
        { body: [aProject({ id: "3f0", name: "checkout" })] },
      ],
      "POST /projects": {
        status: 201,
        body: {
          id: "3f0",
          name: "checkout",
          retentionDays: 30,
          createdAt: "2026-08-08T09:00:00.000Z",
        },
      },
    });

    open();

    const operator = userEvent.setup();

    expect(await screen.findByRole("heading", { name: /no projects yet/i })).toBeInTheDocument();

    await operator.type(screen.getByLabelText("Name"), "checkout");
    await operator.click(screen.getByRole("button", { name: /create the project/i }));

    // Straight into the project that was just made, which is where an operator
    // creating one was going.
    expect(await screen.findByRole("heading", { name: "checkout" })).toBeInTheDocument();
  });

  it("puts the project into a group without a second trip through its settings", async () => {
    const installation = anInstallationAnswering({
      "GET /groups": { body: [aGroup({ id: "g1", name: "shop" })] },
      "GET /projects": [{ body: [] }, { body: [aProject({ id: "3f0", name: "checkout" })] }],
      "POST /projects": {
        status: 201,
        body: {
          id: "3f0",
          name: "checkout",
          groupId: "g1",
          retentionDays: 30,
          createdAt: "2026-08-08T09:00:00.000Z",
        },
      },
    });

    open();

    const operator = userEvent.setup();

    await operator.type(await screen.findByLabelText("Name"), "checkout");
    await operator.selectOptions(screen.getByLabelText("Group"), "g1");
    await operator.click(screen.getByRole("button", { name: /create the project/i }));

    // Creating a project and putting it where it belongs is one errand.
    expect(installation.sentTo("POST /projects")).toEqual([
      { name: "checkout", retentionDays: 30, groupId: "g1" },
    ]);
  });

  it("offers no group to choose while the installation holds none", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": { body: [] },
    });

    open();

    // A select whose only option is "no group" asks the operator to decide
    // something with one possible answer.
    await screen.findByLabelText("Name");
    expect(screen.queryByLabelText("Group")).toBeNull();
  });

  it("says which name is already taken", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": { body: [] },
      "POST /projects": { status: 409 },
    });

    open();

    const operator = userEvent.setup();

    await operator.type(await screen.findByLabelText("Name"), "checkout");
    await operator.click(screen.getByRole("button", { name: /create the project/i }));

    expect(await screen.findByText(/already holds a project by that name/i)).toBeInTheDocument();
  });
});

describe("the project switcher", () => {
  it("reaches another project without a trip back to the list", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": {
        body: [
          aProject({ id: "3f0", name: "checkout" }),
          aProject({ id: "9c1", name: "billing" }),
        ],
      },
    });

    open();

    const operator = userEvent.setup();

    const shell = within(screen.getByRole("banner"));

    await operator.click(await screen.findByRole("button", { name: /^project/i }));
    await operator.click(shell.getByRole("link", { name: "billing" }));

    expect(await screen.findByRole("heading", { name: "billing" })).toBeInTheDocument();
    expect(window.location.pathname).toBe("/project/9c1");
  });

  it("names the group beside the project being read", async () => {
    anInstallationAnswering({
      "GET /groups": { body: [aGroup({ id: "g1", name: "shop" })] },
      "GET /projects": { body: [aProject({ id: "3f0", name: "api", groupId: "g1" })] },
    });

    window.history.pushState({}, "", "/project/3f0");

    render(
      <BrowserRouter>
        <Shell backupCodesRemaining={null} onSignedOut={() => undefined} />
      </BrowserRouter>,
    );

    // A name is unique only within its group, and this is the one place a
    // project is named while the list it stands in is nowhere on the screen.
    expect(await screen.findByRole("button", { name: /shop \/ api/ })).toBeInTheDocument();
  });
});
