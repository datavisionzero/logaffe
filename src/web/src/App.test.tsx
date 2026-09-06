import { afterEach, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BrowserRouter } from "react-router";
import { vi } from "vitest";
import { App } from "./App";
import { aProject, anInstallationAnswering, noGroups } from "./shared/testing";

const PASSWORD = "a passphrase nobody guesses";

function open() {
  window.history.pushState({}, "", "/");

  return render(
    <BrowserRouter>
      <App />
    </BrowserRouter>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("the first screen", () => {
  /**
   * The application shows the application, and the first request it makes is
   * what answers whether the session is one. Nothing probes for it, and nothing
   * announces whether anybody has ever signed in here (ADR 0054).
   */
  it("is the project list when the session admits", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": { body: [aProject({ id: "3f0", name: "checkout" })] },
    });

    open();

    expect(await screen.findByRole("link", { name: "checkout" })).toBeInTheDocument();
  });

  it("is the sign-in when the session is refused", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": { status: 401 },
    });

    open();

    expect(await screen.findByRole("button", { name: "Sign in" })).toBeInTheDocument();
  });
});

describe("the bootstrap exchange", () => {
  /**
   * Reached deliberately, from a link on the sign-in screen rather than because
   * the application worked out that nobody has a password yet.
   */
  it("is one link away from the sign-in and reaches the first-run guide", async () => {
    const installation = anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": { status: 401 },
      "POST /bootstrap": { status: 204 },
    });

    open();

    const person = userEvent.setup();

    await person.click(
      await screen.findByRole("button", { name: /setting this installation up/i }),
    );

    await person.type(screen.getByLabelText("Bootstrap token"), "the-one-it-names");
    await person.type(screen.getByLabelText("Password"), PASSWORD);
    await person.type(screen.getByLabelText("Password again"), PASSWORD);
    await person.click(screen.getByRole("button", { name: /^set the password$/i }));

    expect(await screen.findByRole("heading", { name: /this installation is yours/i }))
      .toBeInTheDocument();

    // The guide opens with the offer the exchange does not make (ADR 0041).
    expect(screen.getByRole("heading", { name: /a second factor/i })).toBeInTheDocument();

    expect(installation.asked).toContain("POST /bootstrap");
  });

  it("says so when there is nothing left to exchange", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": { status: 401 },
      "POST /bootstrap": { status: 409 },
    });

    open();

    const person = userEvent.setup();

    await person.click(
      await screen.findByRole("button", { name: /setting this installation up/i }),
    );

    await person.type(screen.getByLabelText("Bootstrap token"), "the-one-it-names");
    await person.type(screen.getByLabelText("Password"), PASSWORD);
    await person.type(screen.getByLabelText("Password again"), PASSWORD);
    await person.click(screen.getByRole("button", { name: /^set the password$/i }));

    expect(await screen.findByText(/already has a password/i)).toBeInTheDocument();
  });
});

describe("signing in", () => {
  it("reaches the project list, and says what a spent backup code left", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": [{ status: 401 }, { body: [aProject({ id: "3f0", name: "checkout" })] }],
      "POST /sign-in": { body: { backupCodesRemaining: 7 } },
    });

    open();

    const person = userEvent.setup();

    await person.type(await screen.findByLabelText(/email address/i), "somebody@example.com");
    await person.type(screen.getByLabelText(/^password$/i), PASSWORD);
    await person.click(screen.getByRole("button", { name: /use a backup code/i }));
    await person.type(screen.getByLabelText(/backup code/i), "4RTY-8HQ2");
    await person.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("link", { name: "checkout" })).toBeInTheDocument();
    expect(screen.getByText(/7 codes are left/)).toBeInTheDocument();
  });

  it("says one thing for every way of not getting in", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": { status: 401 },
      "POST /sign-in": { status: 401 },
    });

    open();

    const person = userEvent.setup();

    await person.type(await screen.findByLabelText(/email address/i), "nobody@example.com");
    await person.type(screen.getByLabelText(/^password$/i), "not the password");
    await person.type(screen.getByLabelText(/six digits/i), "000000");
    await person.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("That did not sign you in.")).toBeInTheDocument();
  });
});
