import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
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

/** One row, with everything the contract requires on it. */
function aChange(change: {
  id: number;
  actorName?: string;
  actorKind?: "user" | "agent";
  subject?: string;
  subjectName?: string;
  act?: string;
  field?: string | null;
  from?: string | null;
  to?: string | null;
}) {
  return {
    id: change.id,
    actorId: "u1",
    actorKind: change.actorKind ?? "user",
    actorName: change.actorName ?? "The Administrator",
    at: "2026-09-06T09:00:00Z",
    subject: change.subject ?? "project",
    subjectId: "p1",
    subjectName: change.subjectName ?? "orders-api",
    act: change.act ?? "created",
    field: change.field ?? null,
    from: change.from ?? null,
    to: change.to ?? null,
  };
}

function open(answers: Record<string, unknown> = {}, at = "/settings/history") {
  window.history.pushState({}, "", at);

  const installation = anInstallationAnswering({
    "GET /me": ADMINISTRATOR,
    "GET /second-factor": withSecondFactor,
    "GET /history": { body: [aChange({ id: 3 })] },
    ...answers,
  } as Parameters<typeof anInstallationAnswering>[0]);

  render(
    <MemoryRouter initialEntries={[at]}>
      <Routes>
        <Route path="/settings/:section" element={<InstallationSettings />} />
      </Routes>
    </MemoryRouter>,
  );

  return installation;
}

afterEach(() => vi.unstubAllGlobals());

describe("what has been changed on an installation", () => {
  it("reads each row as a sentence rather than as its parts", async () => {
    open({
      "GET /history": {
        body: [
          aChange({ id: 5, act: "removed", subjectName: "orders-api" }),
          aChange({ id: 4, act: "renamed", from: "orders", to: "orders-api" }),
          aChange({
            id: 3,
            act: "changed",
            field: "retention",
            from: "7 days",
            to: "30 days",
          }),
          aChange({ id: 2, act: "issued", subject: "ingestToken", subjectName: "orders" }),
        ],
      },
    });

    expect(await screen.findByText("Deleted project orders-api")).toBeInTheDocument();
    expect(screen.getByText("Renamed project orders to orders-api")).toBeInTheDocument();
    expect(
      screen.getByText("Changed retention on project orders-api from 7 days to 30 days"),
    ).toBeInTheDocument();
    expect(screen.getByText("Issued an ingest token for orders")).toBeInTheDocument();
  });

  it("says when an agent held the keyboard and not only whose authority it was", async () => {
    open({
      "GET /history": {
        body: [aChange({ id: 3, actorKind: "agent", actorName: "the terminal agent" })],
      },
    });

    // An agent acts with its owner's authority, so a row naming a person and
    // saying nothing else would be true and misleading at once (ADR 0052).
    expect(await screen.findByText("the terminal agent")).toBeInTheDocument();
    expect(screen.getByText("Agent")).toBeInTheDocument();
  });

  it("walks back from the row the page ended at", async () => {
    const full = Array.from({ length: 100 }, (_, index) => aChange({ id: 200 - index }));

    const installation = open({
      "GET /history": [
        { body: full },
        { body: [aChange({ id: 100, subjectName: "the oldest thing" })] },
      ],
    });

    await userEvent.click(await screen.findByRole("button", { name: "Show more" }));

    expect(await screen.findByText("Created project the oldest thing")).toBeInTheDocument();

    // The cursor is the id of the last row shown, so nothing is fetched twice
    // and nothing between two pages is skipped.
    expect(
      installation.asked.filter((route) => route === "GET /history"),
    ).toHaveLength(2);

    // And the page that came back short is the end of it.
    expect(screen.queryByRole("button", { name: "Show more" })).not.toBeInTheDocument();
  });

  it("is not offered to somebody who does not administer this installation", async () => {
    open({ "GET /me": SOMEBODY, "GET /agent-tokens": { body: [] } }, "/settings/agents");

    const rail = within(await screen.findByRole("navigation", { name: "Settings" }));

    // The installation refuses the read either way; an area that does nothing
    // but refuse is one nobody should be offered (ADR 0055).
    expect(rail.getByRole("link", { name: "Agent tokens" })).toBeInTheDocument();
    expect(rail.queryByRole("link", { name: "History" })).not.toBeInTheDocument();
  });
});
