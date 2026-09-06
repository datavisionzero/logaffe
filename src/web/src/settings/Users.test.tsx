import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { InstallationSettings } from "./InstallationSettings";
import { anInstallationAnswering, withSecondFactor } from "../shared/testing";

const ADMINISTRATOR = {
  body: {
    id: "u1",
    name: "The Administrator",
    email: "admin@example.com",
    administrator: true,
  },
};

const SOMEBODY = {
  body: { id: "u2", name: "Somebody", email: "somebody@example.com", administrator: false },
};

const TWO_PEOPLE = {
  body: [
    {
      id: "u1",
      name: "The Administrator",
      email: "admin@example.com",
      state: "active",
      administrator: true,
      hasSecondFactor: true,
      projects: 2,
      createdAt: "2026-09-01T09:00:00Z",
    },
    {
      id: "u2",
      name: "Somebody",
      email: "somebody@example.com",
      state: "invited",
      administrator: false,
      hasSecondFactor: false,
      projects: 0,
      createdAt: "2026-09-02T09:00:00Z",
    },
  ],
};

function open(answers: Record<string, unknown> = {}) {
  window.history.pushState({}, "", "/settings/people");

  const installation = anInstallationAnswering({
    "GET /me": ADMINISTRATOR,
    "GET /second-factor": withSecondFactor,
    "GET /users": TWO_PEOPLE,
    ...answers,
  } as Parameters<typeof anInstallationAnswering>[0]);

  render(
    <MemoryRouter initialEntries={["/settings/people"]}>
      <Routes>
        <Route path="/settings/:section" element={<InstallationSettings />} />
      </Routes>
    </MemoryRouter>,
  );

  return installation;
}

afterEach(() => vi.unstubAllGlobals());

describe("the people on an installation", () => {
  it("says what state each account is in and whether it has a second factor", async () => {
    open();

    expect(await screen.findByText("somebody@example.com")).toBeInTheDocument();
    expect(screen.getByText("Invited")).toBeInTheDocument();

    // An administrator sees who has none and cannot require it
    // (`docs/sign-in.md`).
    expect(screen.getByText("None")).toBeInTheDocument();
    expect(screen.getByText("Enrolled")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /require/i })).not.toBeInTheDocument();
  });

  it("deactivates rather than deleting, and says what that costs", async () => {
    const installation = open({
      "POST /users/u2/deactivation": { status: 204 },
      "GET /users": [TWO_PEOPLE, TWO_PEOPLE],
    });

    await screen.findByText("somebody@example.com");

    // Nothing in this product deletes an identity, and no button says it does.
    expect(screen.queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();

    await userEvent
      .setup()
      .click(screen.getAllByRole("button", { name: "Deactivate" })[1]!);

    expect(await screen.findByText(/their sessions are over/i)).toBeInTheDocument();
    expect(installation.asked).toContain("POST /users/u2/deactivation");
  });

  it("says why the last administrator cannot lose the role", async () => {
    open({ "PUT /users/u1/role": { status: 409 } });

    await screen.findByText("admin@example.com");

    await userEvent.setup().click(screen.getByRole("button", { name: "Take the role" }));

    expect(await screen.findByText(/always at least one active administrator/i))
      .toBeInTheDocument();
  });

  it("is not offered to somebody who does not administer", async () => {
    window.history.pushState({}, "", "/settings");

    anInstallationAnswering({
      "GET /me": SOMEBODY,
      "GET /second-factor": withSecondFactor,
      "GET /sessions": { body: [] },
    } as Parameters<typeof anInstallationAnswering>[0]);

    render(
      <MemoryRouter initialEntries={["/settings"]}>
        <Routes>
          <Route path="/settings" element={<InstallationSettings />} />
        </Routes>
      </MemoryRouter>,
    );

    // A courtesy rather than a boundary: the installation refuses the acts
    // either way, and a tab that does nothing but refuse is one nobody should be
    // shown (ADR 0055).
    await screen.findByRole("link", { name: /agent tokens/i });
    expect(screen.queryByRole("link", { name: /^people$/i })).not.toBeInTheDocument();
  });
});
