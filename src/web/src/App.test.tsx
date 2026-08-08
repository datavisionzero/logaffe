import { afterEach, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BrowserRouter } from "react-router";
import { vi } from "vitest";
import { App } from "./App";
import {
  aProject,
  anInstallationAnswering,
  claimed,
  lapsed,
  unclaimed,
} from "./shared/testing";

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
  it("is the claim when the installation belongs to nobody", async () => {
    anInstallationAnswering({ "GET /claim": unclaimed() });

    open();

    expect(await screen.findByRole("heading", { name: /claim this installation/i }))
      .toBeInTheDocument();
  });

  it("names the host command when the window has lapsed", async () => {
    anInstallationAnswering({ "GET /claim": lapsed });

    open();

    expect(await screen.findByText(/logaffe recover/)).toBeInTheDocument();
  });

  it("is the project list on a claimed installation", async () => {
    anInstallationAnswering({
      "GET /claim": claimed,
      "GET /projects": { body: [aProject({ id: "3f0", name: "checkout" })] },
    });

    open();

    expect(await screen.findByRole("link", { name: "checkout" })).toBeInTheDocument();
  });

  it("is the sign-in when the session is refused", async () => {
    anInstallationAnswering({
      "GET /claim": claimed,
      "GET /projects": { status: 401 },
    });

    open();

    expect(await screen.findByRole("button", { name: "Sign in" })).toBeInTheDocument();
  });
});

describe("finishing the claim", () => {
  /**
   * What follows the claim is the guide, not the shell (`docs/setup.md`), and
   * the only thing that ever reaches it is a claim finished in this browser.
   */
  it("reaches the first-run guide rather than the project list", async () => {
    anInstallationAnswering({
      "GET /claim": unclaimed(),
      "POST /claim/enrolment": {
        body: {
          secondFactorSecret: "JBSWY3DPEHPK3PXP",
          enrolmentUri: "otpauth://totp/logaffe:operator?secret=JBSWY3DPEHPK3PXP",
          backupCodes: ["4RTY-8HQ2"],
          ticket: "sealed",
        },
      },
      "POST /claim": { status: 204 },
    });

    open();

    const operator = userEvent.setup();
    const password = "a passphrase nobody guesses";

    await operator.type(await screen.findByLabelText("Password"), password);
    await operator.type(screen.getByLabelText("Password again"), password);
    await operator.click(screen.getByRole("button", { name: "Continue" }));

    await operator.click(await screen.findByRole("checkbox"));
    await operator.click(screen.getByRole("button", { name: "Continue" }));

    await operator.type(screen.getByLabelText(/six digits/i), "123456");
    await operator.type(screen.getByLabelText(/backup code/i), "4RTY-8HQ2");
    await operator.click(screen.getByRole("button", { name: /claim this installation/i }));

    expect(await screen.findByRole("heading", { name: /this installation is yours/i }))
      .toBeInTheDocument();
  });
});

describe("signing in", () => {
  it("reaches the project list, and says what a spent backup code left", async () => {
    anInstallationAnswering({
      "GET /claim": claimed,
      "GET /projects": [{ status: 401 }, { body: [aProject({ id: "3f0", name: "checkout" })] }],
      "POST /sign-in": { body: { backupCodesRemaining: 7 } },
    });

    open();

    const operator = userEvent.setup();

    await operator.type(
      await screen.findByLabelText(/password/i),
      "a passphrase nobody guesses",
    );
    await operator.click(screen.getByRole("button", { name: /use a backup code/i }));
    await operator.type(screen.getByLabelText(/backup code/i), "4RTY-8HQ2");
    await operator.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("link", { name: "checkout" })).toBeInTheDocument();
    expect(screen.getByText(/7 codes are left/)).toBeInTheDocument();
  });

  it("says one thing for every way of not getting in", async () => {
    anInstallationAnswering({
      "GET /claim": claimed,
      "GET /projects": { status: 401 },
      "POST /sign-in": { status: 401 },
    });

    open();

    const operator = userEvent.setup();

    await operator.type(await screen.findByLabelText(/password/i), "not the password");
    await operator.type(screen.getByLabelText(/six digits/i), "000000");
    await operator.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("That did not sign you in.")).toBeInTheDocument();
  });
});
