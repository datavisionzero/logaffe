import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BrowserRouter } from "react-router";
import { Shell } from "../shell/Shell";
import { aProject, anInstallationAnswering } from "../shared/testing";

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
      "GET /projects": {
        body: [aProject({ id: "3f0", name: "checkout", lastReceivedAt: null })],
      },
    });

    open();

    expect(await screen.findByText("Nothing has ever arrived")).toBeInTheDocument();
  });

  it("names the project whose door is closed", async () => {
    anInstallationAnswering({
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
      "GET /projects": { body: [aProject({ id: "3f0", name: "checkout" })] },
    });

    open();

    await screen.findByRole("link", { name: "checkout" });

    // A dashboard is a set of counts nobody asked for over the largest table in
    // the database. The list asks for the projects and for nothing else.
    expect(installation.asked).toEqual(["GET /projects"]);
  });
});

describe("an empty installation", () => {
  it("offers the act that creates a project", async () => {
    anInstallationAnswering({
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

  it("says which name is already taken", async () => {
    anInstallationAnswering({
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
});
