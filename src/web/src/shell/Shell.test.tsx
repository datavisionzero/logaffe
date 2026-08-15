import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { Shell } from "./Shell";
import { aProject, anInstallationAnswering, type Answer } from "../shared/testing";

/**
 * The shell around a signed-in installation holding two projects, opened at an
 * address rather than navigated to one — which is what a reload is, and what
 * every one of these has to be true after.
 */
function open(at = "/", routes: Record<string, Answer | Answer[]> = {}) {
  anInstallationAnswering({
    "GET /projects": {
      body: [
        aProject({ id: "3f0", name: "checkout" }),
        aProject({ id: "9c1", name: "billing" }),
      ],
    },
    // A project nothing has ever delivered to, which is the log view that asks
    // the installation for the least.
    "GET /projects/9c1/ingest-tokens": { body: [] },
    ...routes,
  });

  render(
    <MemoryRouter initialEntries={[at]}>
      <Shell backupCodesRemaining={null} onSignedOut={() => undefined} />
    </MemoryRouter>,
  );

  return within(screen.getByRole("banner"));
}

afterEach(() => vi.unstubAllGlobals());

describe("the project switcher", () => {
  /**
   * The name of the project being read appears nowhere else on the log view,
   * so a switcher that does not say which one is open leaves the screen without
   * one.
   */
  it("names the project being read", async () => {
    const shell = open("/project/9c1");

    expect(await shell.findByRole("button", { name: /project billing/i })).toBeInTheDocument();
  });

  it("carries the time range and the level to the other project, and nothing else", async () => {
    const shell = open("/project/9c1?range=15m&minimumLevel=Warning&search=timeout");

    const operator = userEvent.setup();

    await operator.click(await shell.findByRole("button", { name: /project billing/i }));

    const address = shell.getByRole("link", { name: "checkout" }).getAttribute("href")!;

    expect(address).toContain("range=15m");
    expect(address).toContain("minimumLevel=Warning");

    // A search text belongs to the project it was found in: carried over, it
    // would produce an empty list that looks like an outage.
    expect(address).not.toContain("search");
  });

  it("goes back to the list, which is otherwise only the wordmark", async () => {
    const shell = open("/project/9c1");

    const operator = userEvent.setup();

    await operator.click(await shell.findByRole("button", { name: /project billing/i }));
    await operator.click(shell.getByRole("link", { name: "All projects" }));

    expect(await screen.findByRole("heading", { name: "Projects" })).toBeInTheDocument();
  });
});

describe("the two levels", () => {
  it("shows a project's surfaces only while one is open", async () => {
    const shell = open("/");

    // The list has arrived, so the second level is absent because there is no
    // project open and not because nothing has been read yet.
    await screen.findByRole("link", { name: "checkout" });

    expect(shell.queryByRole("navigation", { name: "Project" })).toBeNull();
    expect(shell.getByRole("link", { name: "Installation settings" })).toBeInTheDocument();
  });

  it("marks which of a project's surfaces is being read", async () => {
    const shell = open("/project/9c1");

    const tabs = within(await shell.findByRole("navigation", { name: "Project" }));

    expect(tabs.getByRole("link", { name: "Log" })).toHaveAttribute("aria-current", "page");
    expect(tabs.getByRole("link", { name: "Project settings" })).not.toHaveAttribute(
      "aria-current",
    );
  });

  it("offers no project surfaces for a project the installation does not hold", async () => {
    const shell = open("/project/gone");

    // The list has arrived and does not hold it, which is what a project
    // deleted from another browser looks like from here.
    expect(await screen.findByRole("heading", { name: /no such project/i })).toBeInTheDocument();
    expect(shell.queryByRole("navigation", { name: "Project" })).toBeNull();
  });

  /**
   * Settings are navigated to, rather than reached from a link inside the view
   * they are about.
   */
  it("reaches a project's settings from the shell", async () => {
    const shell = open("/project/9c1");

    const operator = userEvent.setup();

    await operator.click(await shell.findByRole("link", { name: "Project settings" }));

    expect(await screen.findByRole("heading", { name: "billing", level: 1 })).toBeInTheDocument();
    expect(shell.getByRole("link", { name: "Project settings" })).toHaveAttribute(
      "aria-current",
      "page",
    );
  });
});
