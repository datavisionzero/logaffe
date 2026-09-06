import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BrowserRouter } from "react-router";
import { Shell } from "./Shell";
import {
  anInstallationAnswering,
  noGroups,
  withSecondFactor,
  withoutSecondFactor,
  type Answer,
} from "../shared/testing";

function open(routes: Record<string, Answer | Answer[]>) {
  window.history.pushState({}, "", "/");

  anInstallationAnswering({
    "GET /groups": noGroups,
    "GET /projects": { body: [] },
    ...routes,
  });

  return render(
    <BrowserRouter>
      <Shell backupCodesRemaining={null} onSignedOut={() => undefined} />
    </BrowserRouter>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("an account with no second factor", () => {
  /**
   * The second factor is offered rather than required (ADR 0041), and the
   * interface is the only thing that can keep an omission from passing for a
   * setting — so this is said on every screen and cannot be dismissed. It is a
   * statement about the signed-in account and never about the installation.
   */
  it("says so, and points at the act that ends it", async () => {
    open({ "GET /second-factor": withoutSecondFactor });

    expect(await screen.findByText(/has no second factor/i)).toBeInTheDocument();

    const enrol = screen.getByRole("link", { name: /enrol one/i });
    expect(enrol).toHaveAttribute("href", "/settings/credentials");

    // Not dismissible: it is the state of the account rather than a warning
    // about something that went wrong.
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("says nothing at all when one is enrolled", async () => {
    open({ "GET /second-factor": withSecondFactor });

    await screen.findByRole("link", { name: "logaffe" });

    expect(screen.queryByText(/has no second factor/i)).not.toBeInTheDocument();
  });
});

describe("the account menu", () => {
  /**
   * It is the one part of the shell that is not on the screen until it is
   * asked for, so it is the one part a screen rendering cleanly says nothing
   * about — which is how it once shipped throwing on the first click.
   */
  it("opens on what belongs to the session, and says nothing about a person", async () => {
    open({ "GET /second-factor": withSecondFactor });

    const operator = userEvent.setup();

    await operator.click(await screen.findByRole("button", { name: "Account" }));

    expect(
      await screen.findByRole("menuitem", { name: /installation settings/i }),
    ).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /sign out/i })).toBeInTheDocument();

    // There is one operator and no user model, so nothing here names anybody.
    expect(screen.getByText(/this installation/i)).toBeInTheDocument();
  });
});
