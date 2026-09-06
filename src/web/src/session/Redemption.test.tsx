import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { App } from "../App";
import { anInstallationAnswering, noGroups } from "../shared/testing";

const PASSWORD = "a passphrase nobody guesses";

function openAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("the three links", () => {
  /**
   * The address decides which of them landed, because that is what a link in a
   * message points at (ADR 0053) — and none of the three can be behind the
   * shell, since the person holding one cannot sign in.
   */
  it("sets a first password from an invitation", async () => {
    const installation = anInstallationAnswering({
      "POST /invitation/redemption": { status: 204 },
    });

    openAt("/invitation?secret=the-one-in-the-message");

    const person = userEvent.setup();

    await person.type(await screen.findByLabelText("Password"), PASSWORD);
    await person.type(screen.getByLabelText("Password again"), PASSWORD);
    await person.click(screen.getByRole("button", { name: /^set the password$/i }));

    expect(await screen.findByText(/your password is set/i)).toBeInTheDocument();
    expect(installation.asked).toEqual(["POST /invitation/redemption"]);
  });

  it("confirms an address without asking for a password", async () => {
    anInstallationAnswering({ "POST /address/redemption": { status: 204 } });

    openAt("/address?secret=the-one-in-the-message");

    // Nothing about this changes a credential, so nothing here asks for one.
    expect(await screen.findByRole("button", { name: /confirm the address/i }))
      .toBeInTheDocument();
    expect(screen.queryByLabelText("Password")).not.toBeInTheDocument();

    await userEvent.setup().click(screen.getByRole("button", { name: /confirm the address/i }));

    expect(await screen.findByText(/is now the one that signs in/i)).toBeInTheDocument();
  });

  it("says one thing for every way a link does not open anything", async () => {
    anInstallationAnswering({
      "POST /recovery/redemption": {
        status: 400,
        body: { errors: { secret: ["That link does not open anything."] } },
      },
    });

    openAt("/recovery?secret=spent");

    const person = userEvent.setup();

    await person.type(await screen.findByLabelText("Password"), PASSWORD);
    await person.type(screen.getByLabelText("Password again"), PASSWORD);
    await person.click(screen.getByRole("button", { name: /^set the password$/i }));

    expect(await screen.findByText(/does not open anything/i)).toBeInTheDocument();
  });
});

describe("asking for a password back", () => {
  it("says the same thing whether or not there is an account", async () => {
    anInstallationAnswering({
      "GET /groups": noGroups,
      "GET /projects": { status: 401 },
      "POST /recovery": { status: 204 },
    });

    openAt("/");

    const person = userEvent.setup();

    await person.click(await screen.findByRole("button", { name: /forgotten your password/i }));
    await person.type(screen.getByLabelText(/email address/i), "nobody@example.com");
    await person.click(screen.getByRole("button", { name: /send the link/i }));

    // "If there is an account", never "there is one": this form must not become
    // a way of asking who is here.
    expect(await screen.findByText(/if there is an account/i)).toBeInTheDocument();
  });
});
