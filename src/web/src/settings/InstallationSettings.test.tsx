import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { GroupsProvider } from "../projects/groups";
import { ProjectsProvider } from "../projects/projects";
import { InstallationSettings } from "./InstallationSettings";
import { aGroup, aProject, anInstallationAnswering, type Answer } from "../shared/testing";

function aSession(session: {
  id: string;
  lastSeenFrom?: string;
  startedAt?: string;
  lastUsedAt?: string;
  expiresAt?: string;
  isCurrent?: boolean;
}) {
  return {
    id: session.id,
    lastSeenFrom: session.lastSeenFrom ?? "203.0.113.7",
    startedAt: session.startedAt ?? "2026-08-01T09:00:00.000Z",
    lastUsedAt: session.lastUsedAt ?? "2026-08-08T11:42:07.318Z",
    expiresAt: session.expiresAt ?? "2026-09-07T09:00:00.000Z",
    isCurrent: session.isCurrent ?? false,
  };
}

function anAgentToken(token: {
  id: string;
  name: string;
  issuedAt?: string;
  lastUsedAt?: string | null;
}) {
  return {
    id: token.id,
    name: token.name,
    issuedAt: token.issuedAt ?? "2026-08-01T09:00:00.000Z",
    lastUsedAt: token.lastUsedAt ?? null,
  };
}

/** The enrolment a re-enrolment is drawn from, which stores nothing yet. */
const ENROLMENT: Answer = {
  body: {
    secondFactorSecret: "JBSWY3DPEHPK3PXP",
    enrolmentUri: "otpauth://totp/logaffe?secret=JBSWY3DPEHPK3PXP",
    backupCodes: ["4RTY-8HQ2", "9KDP-2LMN"],
    ticket: "sealed",
  },
};

/** The screen at one of its areas, which is an address like any other. */
function open(routes: Record<string, Answer | Answer[]> = {}, at = "/settings") {
  const installation = anInstallationAnswering({
    "GET /sessions": { body: [aSession({ id: "s1", isCurrent: true })] },
    "GET /agent-tokens": { body: [] },
    ...routes,
  });

  render(
    <MemoryRouter initialEntries={[at]}>
      <Routes>
        <Route path="/settings" element={<InstallationSettings />} />
        <Route path="/settings/:section" element={<InstallationSettings />} />
      </Routes>
    </MemoryRouter>,
  );

  return installation;
}

/** The area holding the tokens agents read with. */
function openAgents(routes: Record<string, Answer | Answer[]> = {}) {
  return open(routes, "/settings/agents");
}

/** The area holding the operator\'s own three credentials. */
function openCredentials(routes: Record<string, Answer | Answer[]> = {}) {
  return open(routes, "/settings/credentials");
}

/** One section of the screen, since three of them ask for a password. */
function section(name: string) {
  return within(screen.getByRole("heading", { name, level: 2 }).closest("section")!);
}

afterEach(() => vi.unstubAllGlobals());

describe("the areas", () => {
  it("asks only for what the area being read needs", async () => {
    const installation = open();

    await screen.findByText("203.0.113.7");

    // The stacked screen asked for the sessions and the agent tokens both,
    // whichever of them the operator had come for. The interface asks for
    // nothing unasked (`docs/ui.md`), and an area nobody opened is unasked.
    expect(installation.asked).toEqual(["GET /sessions"]);
  });

  it("walks from one area to another, and marks the one being read", async () => {
    open();

    const operator = userEvent.setup();
    const rail = within(screen.getByRole("navigation", { name: "Settings" }));

    await operator.click(rail.getByRole("link", { name: "Agent tokens" }));

    expect(
      await screen.findByRole("heading", { name: "Agent tokens", level: 2 }),
    ).toBeInTheDocument();
    expect(rail.getByRole("link", { name: "Agent tokens" })).toHaveAttribute(
      "aria-current",
      "page",
    );
  });

  it("answers an address naming no area with the screen's own", async () => {
    open({}, "/settings/nothing-by-that-name");

    expect(await screen.findByText("203.0.113.7")).toBeInTheDocument();
  });
});

describe("the sessions", () => {
  it("marks the browser being read from, because nothing else could", async () => {
    open({
      "GET /sessions": {
        body: [
          aSession({ id: "s1", lastSeenFrom: "203.0.113.7", isCurrent: true }),
          aSession({ id: "s2", lastSeenFrom: "198.51.100.4" }),
        ],
      },
    });

    const here = (await screen.findByText("203.0.113.7")).closest("tr")!;
    const elsewhere = screen.getByText("198.51.100.4").closest("tr")!;

    expect(within(here).getByText("This browser")).toBeInTheDocument();
    expect(within(elsewhere).queryByText("This browser")).toBeNull();
  });

  it("shows the last use to the minute, which is how accurately it is recorded", async () => {
    open();

    const row = (await screen.findByText("203.0.113.7")).closest("tr")!;
    const [, lastUsed] = within(row).getAllByRole("time");

    expect(lastUsed!.textContent).toMatch(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/);
  });

  it("offers ending every other session only when there is another", async () => {
    const installation = open({
      "GET /sessions": {
        body: [aSession({ id: "s1", isCurrent: true }), aSession({ id: "s2" })],
      },
      "DELETE /sessions/others": {},
    });

    const operator = userEvent.setup();

    await operator.click(
      await screen.findByRole("button", { name: /end every other session/i }),
    );

    // Every other, never every one: the browser doing this stays signed in, or
    // securing the installation would sign the operator out of the screen they
    // secured it from.
    await waitFor(() => expect(installation.asked).toContain("DELETE /sessions/others"));
  });

  it("says what ending this browser's own row does before it is done", async () => {
    open();

    const operator = userEvent.setup();
    const row = (await screen.findByText("203.0.113.7")).closest("tr")!;

    await operator.click(within(row).getByRole("button", { name: "End" }));

    expect(within(row).getByText(/this browser signs out/i)).toBeInTheDocument();
  });
});

describe("the agent tokens", () => {
  it("hands over the finished configuration rather than the bare token", async () => {
    openAgents({
      "POST /agent-tokens": {
        status: 201,
        body: {
          id: "a1",
          name: "terminal",
          token: "logaffe_agent_7hb1zz_secret",
          clientConfiguration: '{ "mcpServers": { "logaffe": { "type": "http" } } }',
          issuedAt: "2026-08-08T09:00:00.000Z",
        },
      },
      "GET /agent-tokens": [
        { body: [] },
        { body: [anAgentToken({ id: "a1", name: "terminal" })] },
      ],
    });

    const operator = userEvent.setup();

    await operator.type(await screen.findByLabelText(/name for a new token/i), "terminal");
    await operator.click(screen.getByRole("button", { name: /issue an agent token/i }));

    expect(await screen.findByText(/mcpServers/)).toBeInTheDocument();
  });

  it("reads a token back, because that and being able to use it are one errand", async () => {
    openAgents({
      "GET /agent-tokens": { body: [anAgentToken({ id: "a1", name: "terminal" })] },
      "GET /agent-tokens/a1/token": {
        body: {
          token: "logaffe_agent_7hb1zz_secret",
          clientConfiguration: '{ "mcpServers": { "logaffe": { "type": "http" } } }',
        },
      },
    });

    const operator = userEvent.setup();

    await operator.click(
      await screen.findByRole("button", { name: /show the configuration/i }),
    );

    expect(await screen.findByText(/mcpServers/)).toBeInTheDocument();
  });

  it("renames one, which the agent holding it does not notice", async () => {
    const installation = openAgents({
      "GET /agent-tokens": [
        { body: [anAgentToken({ id: "a1", name: "terminal" })] },
        { body: [anAgentToken({ id: "a1", name: "the laptop" })] },
      ],
      "PATCH /agent-tokens/a1": {},
    });

    const operator = userEvent.setup();

    await operator.click(await screen.findByRole("button", { name: "Rename" }));

    const field = screen.getByLabelText("Name of terminal");

    await operator.clear(field);
    await operator.type(field, "the laptop");
    await operator.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("the laptop")).toBeInTheDocument();

    // The name is a label for this list: it does not identify the token to the
    // server, so nothing else is asked and nothing has to be reconnected.
    expect(installation.asked.filter((route) => route.startsWith("PATCH"))).toEqual([
      "PATCH /agent-tokens/a1",
    ]);
  });

  it("revokes one only after saying what it ends", async () => {
    const installation = openAgents({
      "GET /agent-tokens": [
        { body: [anAgentToken({ id: "a1", name: "terminal" })] },
        { body: [] },
      ],
      "DELETE /agent-tokens/a1": {},
    });

    const operator = userEvent.setup();

    await operator.click(await screen.findByRole("button", { name: "Revoke" }));
    expect(installation.asked).not.toContain("DELETE /agent-tokens/a1");

    await operator.click(screen.getByRole("button", { name: /the agent using it stops reading/i }));

    expect(await screen.findByText(/no agent has been given a token yet/i)).toBeInTheDocument();
  });
});

describe("the operator's own credentials", () => {
  it("says which of the two passwords the installation refused", async () => {
    openCredentials({
      "PUT /password": {
        status: 400,
        body: { errors: { currentPassword: ["That is not your current password."] } },
      },
    });

    const operator = userEvent.setup();
    const password = section("Password");

    await operator.type(password.getByLabelText("Current password"), "not the password");
    await operator.type(password.getByLabelText("New password"), "a passphrase nobody guesses");
    await operator.type(
      password.getByLabelText("New password again"),
      "a passphrase nobody guesses",
    );
    await operator.click(password.getByRole("button", { name: /change the password/i }));

    expect(
      await screen.findByText("That is not your current password."),
    ).toBeInTheDocument();
  });

  it("refuses a new password typed twice differently before sending it", async () => {
    const installation = openCredentials();

    const operator = userEvent.setup();
    const password = section("Password");

    await operator.type(password.getByLabelText("Current password"), "the current one");
    await operator.type(password.getByLabelText("New password"), "a passphrase nobody guesses");
    await operator.type(password.getByLabelText("New password again"), "a passphrase nobody");
    await operator.click(password.getByRole("button", { name: /change the password/i }));

    // There is no reset over the network and no email to send one to, so a typo
    // that gets stored is answered by Host Recovery and nothing smaller.
    expect(installation.asked).not.toContain("PUT /password");
    expect(screen.getByText("These two are not the same.")).toBeInTheDocument();
  });

  it("shows a fresh sheet once, and says it replaces the one before it", async () => {
    openCredentials({ "POST /backup-codes": { body: { codes: ["4RTY-8HQ2", "9KDP-2LMN"] } } });

    const operator = userEvent.setup();
    const codes = section("Backup codes");

    await operator.type(codes.getByLabelText("Password"), "a passphrase nobody guesses");
    await operator.click(codes.getByRole("button", { name: /issue a fresh sheet/i }));

    expect(await screen.findByText("4RTY-8HQ2")).toBeInTheDocument();
    expect(screen.getByText(/replace the sheet you have now/i)).toBeInTheDocument();
  });

  it("stores nothing of a re-enrolment until the confirming request", async () => {
    const installation = openCredentials({ "POST /second-factor/enrolment": ENROLMENT });

    const operator = userEvent.setup();

    await operator.click(
      await screen.findByRole("button", { name: /replace the second factor/i }),
    );

    expect(await screen.findByText("JBSWY3DPEHPK3PXP")).toBeInTheDocument();

    // The authenticator in the operator's pocket keeps working until the moment
    // it is replaced, and abandoning this screen costs the enrolment and
    // nothing else.
    expect(installation.asked).not.toContain("PUT /second-factor");
  });

  it("says which of the three credentials a re-enrolment refused", async () => {
    openCredentials({
      "POST /second-factor/enrolment": ENROLMENT,
      "PUT /second-factor": {
        status: 400,
        body: {
          errors: {
            newSecondFactorCode: [
              "That is not a code the authenticator you just enrolled produces now.",
            ],
          },
        },
      },
    });

    const operator = userEvent.setup();

    await operator.click(
      await screen.findByRole("button", { name: /replace the second factor/i }),
    );
    await operator.click(await screen.findByRole("checkbox"));
    await operator.click(screen.getByRole("button", { name: "Continue" }));

    const factor = section("Second factor");

    await operator.type(factor.getByLabelText("Password"), "a passphrase nobody guesses");
    await operator.type(factor.getByLabelText(/authenticator you have now/i), "000000");
    await operator.type(factor.getByLabelText(/you just enrolled/i), "111111");
    await operator.click(factor.getByRole("button", { name: "Replace it" }));

    expect(
      await screen.findByText(/not a code the authenticator you just enrolled produces/i),
    ).toBeInTheDocument();
  });

  it("offers the backup code for the phone that is already gone", async () => {
    openCredentials({ "POST /second-factor/enrolment": ENROLMENT });

    const operator = userEvent.setup();

    await operator.click(
      await screen.findByRole("button", { name: /replace the second factor/i }),
    );
    await operator.click(await screen.findByRole("checkbox"));
    await operator.click(screen.getByRole("button", { name: "Continue" }));

    const factor = section("Second factor");

    await operator.click(factor.getByRole("button", { name: /the old phone is gone/i }));

    expect(factor.getByLabelText(/backup code off the sheet/i)).toBeInTheDocument();
  });
});

/**
 * The area holding the groups, which is the one area of this screen that reads
 * what the signed-in application already holds rather than asking on its own.
 */
function openGroups(routes: Record<string, Answer | Answer[]> = {}) {
  const installation = anInstallationAnswering({
    "GET /sessions": { body: [aSession({ id: "s1", isCurrent: true })] },
    "GET /agent-tokens": { body: [] },
    "GET /projects": { body: [] },
    "GET /groups": { body: [] },
    ...routes,
  });

  render(
    <MemoryRouter initialEntries={["/settings/groups"]}>
      <ProjectsProvider>
        <GroupsProvider>
          <Routes>
            <Route path="/settings/:section" element={<InstallationSettings />} />
          </Routes>
        </GroupsProvider>
      </ProjectsProvider>
    </MemoryRouter>,
  );

  return installation;
}

describe("the groups", () => {
  it("counts the projects each holds off the list it already reads", async () => {
    openGroups({
      "GET /groups": {
        body: [aGroup({ id: "g1", name: "shop" }), aGroup({ id: "g2", name: "blog" })],
      },
      "GET /projects": {
        body: [
          aProject({ id: "p1", name: "api", groupId: "g1" }),
          aProject({ id: "p2", name: "web", groupId: "g1" }),
          aProject({ id: "p3", name: "loose" }),
        ],
      },
    });

    const shop = (await screen.findByRole("rowheader", { name: "shop" })).closest("tr")!;
    const blog = screen.getByRole("rowheader", { name: "blog" }).closest("tr")!;

    // A group holding nothing is an ordinary state — one made before its first
    // project, or left behind by its last.
    expect(within(shop).getByRole("cell", { name: "2" })).toBeInTheDocument();
    expect(within(blog).getByRole("cell", { name: "0" })).toBeInTheDocument();
  });

  it("says what removing one leaves behind, and asks for no name to be typed", async () => {
    const installation = openGroups({
      "GET /groups": { body: [aGroup({ id: "g1", name: "shop" })] },
      "GET /projects": {
        body: [
          aProject({ id: "p1", name: "api", groupId: "g1" }),
          aProject({ id: "p2", name: "web", groupId: "g1" }),
        ],
      },
      "DELETE /groups/g1": {},
    });

    const operator = userEvent.setup();

    await operator.click(await screen.findByRole("button", { name: "Remove" }));

    // Nothing is destroyed, so the guard that fits deleting a project — typing
    // its name — would say the two acts weigh the same (ADR 0039).
    const removing = await screen.findByRole("button", {
      name: /remove it — 2 projects are left in no group/i,
    });

    await operator.click(removing);

    await waitFor(() => expect(installation.asked).toContain("DELETE /groups/g1"));
  });

  it("says which name is already taken", async () => {
    openGroups({ "POST /groups": { status: 409 } });

    const operator = userEvent.setup();

    await operator.type(await screen.findByLabelText(/name for a new group/i), "shop");
    await operator.click(screen.getByRole("button", { name: /make a group/i }));

    expect(
      await screen.findByText(/already holds a group by that name/i),
    ).toBeInTheDocument();
  });
});
