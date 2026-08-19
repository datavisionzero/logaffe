import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { GroupsProvider } from "../projects/groups";
import { ProjectsProvider } from "../projects/projects";
import { InstallationSettings } from "./InstallationSettings";
import {
  aHost,
  aProject,
  aSampleBucket,
  aSampleWindow,
  anInstallationAnswering,
  noGroups,
  noHosts,
  type Answer,
} from "../shared/testing";

function aHostToken(token: {
  id: string;
  identifier: string;
  issuedAt?: string;
  lastUsedAt?: string | null;
}) {
  return {
    id: token.id,
    identifier: token.identifier,
    issuedAt: token.issuedAt ?? "2026-08-01T09:00:00.000Z",
    lastUsedAt: token.lastUsedAt ?? null,
  };
}

const COMMAND = "docker run -d --name logaffe-collector ... -e LOGAFFE_HOST_TOKEN=logaffe_host_x";

/** The window every host's samples are kept for, which sits on this area. */
const RETENTION: Answer = { body: { retentionDays: 30 } };

function open(routes: Record<string, Answer | Answer[]> = {}, at = "/settings/hosts") {
  const installation = anInstallationAnswering({
    "GET /groups": noGroups,
    "GET /projects": { body: [aProject({ id: "p1", name: "checkout" })] },
    "GET /second-factor": { body: { isEnrolled: true, enrolledAt: "2026-08-01T09:00:00Z" } },
    "GET /hosts": noHosts,
    "GET /samples/retention": RETENTION,
    ...routes,
  });

  render(
    <MemoryRouter initialEntries={[at]}>
      <ProjectsProvider>
        <GroupsProvider>
          <Routes>
            <Route path="/settings" element={<InstallationSettings />} />
            <Route path="/settings/:section" element={<InstallationSettings />} />
            <Route path="/settings/hosts/:hostId" element={<InstallationSettings />} />
          </Routes>
        </GroupsProvider>
      </ProjectsProvider>
    </MemoryRouter>,
  );

  return installation;
}

afterEach(() => vi.unstubAllGlobals());

describe("the list", () => {
  it("says when each host last reported, and that never is an answer", async () => {
    open({
      "GET /hosts": {
        body: [
          aHost({ id: "h1", name: "web-01", lastReportedAt: "2026-08-08T11:42:07.318Z" }),
          aHost({ id: "h2", name: "db-01" }),
        ],
      },
    });

    const rows = within(await screen.findByRole("table")).getAllByRole("row");

    // In the order of their names, which is the order they are listed in.
    expect(within(rows[1]!).getByRole("link").textContent).toBe("db-01");
    expect(within(rows[1]!).getByText("Never used")).toBeInTheDocument();

    expect(within(rows[2]!).getByRole("link").textContent).toBe("web-01");
    // To the minute, which is how accurately a last use is recorded.
    expect(within(rows[2]!).getByText(/2026-08-08 1[0-9]:42/)).toBeInTheDocument();
  });

  it("says an installation with no hosts is an installation that has not asked", async () => {
    open();

    expect(await screen.findByText(/There are no hosts/)).toBeInTheDocument();
  });

  it("makes one, and does not hand back the command for it", async () => {
    const installation = open({
      "POST /hosts": [
        { status: 201, body: { id: "h1", name: "web-01", createdAt: "2026-08-08T09:00:00Z" } },
      ],
      "GET /hosts": [noHosts, { body: [aHost({ id: "h1", name: "web-01" })] }],
    });

    await userEvent.type(await screen.findByLabelText("Name for a new host"), "web-01");
    await userEvent.click(screen.getByRole("button", { name: "Make a host" }));

    await screen.findByRole("link", { name: "web-01" });

    expect(installation.sentTo("POST /hosts")).toEqual([{ name: "web-01" }]);

    // Issuing its token hands the command back, exactly as an ingest token
    // hands back a delivery snippet. Making the host is not that act.
    expect(screen.queryByText(/docker run/)).toBeNull();
  });

  it("refuses a second host by a name one already has", async () => {
    open({
      "GET /hosts": { body: [aHost({ id: "h1", name: "web-01" })] },
      "POST /hosts": { status: 409 },
    });

    await userEvent.type(await screen.findByLabelText("Name for a new host"), "web-01");
    await userEvent.click(screen.getByRole("button", { name: "Make a host" }));

    expect(
      await screen.findByText("This installation already holds a host by that name."),
    ).toBeInTheDocument();
  });
});

describe("a host's own screen", () => {
  const ONE_HOST: Answer = { body: [aHost({ id: "h1", name: "web-01", projects: 2 })] };

  it("is an address, reached from the list and reloadable", async () => {
    open(
      {
        "GET /hosts": ONE_HOST,
        "GET /hosts/h1/host-tokens": { body: [] },
        "GET /hosts/h1/samples": aSampleWindow({}),
      },
      "/settings/hosts/h1",
    );

    expect(await screen.findByRole("heading", { name: "web-01" })).toBeInTheDocument();

    // The rail still marks the area the host is inside, which is the area it
    // is an address within rather than a sixth thing beside it.
    expect(
      within(screen.getByRole("navigation", { name: "Settings" })).getByRole("link", {
        name: "Hosts",
      }),
    ).toHaveAttribute("aria-current", "page");
  });

  it("draws what the machine was doing over a plain range", async () => {
    open(
      {
        "GET /hosts": ONE_HOST,
        "GET /hosts/h1/host-tokens": { body: [] },
        "GET /hosts/h1/samples": aSampleWindow({
          samples: [aSampleBucket({ start: "2026-08-08T11:00:00.000Z", cpuAverage: 0.42 })],
        }),
      },
      "/settings/hosts/h1",
    );

    expect(await screen.findByRole("img", { name: "Processor: 42%" })).toBeInTheDocument();

    // A plain range and nothing else: no filter set, no absolute from-and-to,
    // and no arrangement to save.
    expect(screen.getByLabelText("Over")).toBeInTheDocument();
  });

  it("hands back the collector command when its token is issued", async () => {
    open(
      {
        "GET /hosts": ONE_HOST,
        "GET /hosts/h1/samples": aSampleWindow({}),
        "GET /hosts/h1/host-tokens": [
          { body: [] },
          { body: [aHostToken({ id: "t1", identifier: "3kf9q2" })] },
        ],
        "POST /hosts/h1/host-tokens": {
          status: 201,
          body: {
            id: "t1",
            token: "logaffe_host_3kf9q2_secret",
            collectorCommand: COMMAND,
            issuedAt: "2026-08-08T09:00:00Z",
          },
        },
      },
      "/settings/hosts/h1",
    );

    await userEvent.click(await screen.findByRole("button", { name: "Issue a host token" }));

    expect(await screen.findByText(COMMAND)).toBeInTheDocument();

    // The two read-only mounts are the whole of what it asks for, and the
    // screen says so: it is not privileged, it joins no namespace and it
    // touches no socket.
    expect(screen.getByText(/read-only/)).toBeInTheDocument();
    expect(screen.getByText(/opens\s+no port/)).toBeInTheDocument();
  });

  it("takes the host's name typed back before it deletes it", async () => {
    const installation = open(
      {
        "GET /hosts": ONE_HOST,
        "GET /hosts/h1/samples": aSampleWindow({}),
        "GET /hosts/h1/host-tokens": { body: [] },
        "DELETE /hosts/h1": { status: 204 },
      },
      "/settings/hosts/h1",
    );

    const remove = await screen.findByRole("button", { name: "Delete web-01" });

    // A group holds nothing and is removed with a click; a host holds its
    // samples, and the guard is proportionate to what does not come back.
    expect(remove).toBeDisabled();

    // And it says what the projects on it lose, which is the band and nothing
    // else.
    expect(screen.getByText(/2 projects are left sitting on no host/)).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(/Type/), "web-01");
    await userEvent.click(remove);

    await waitFor(() => expect(installation.asked).toContain("DELETE /hosts/h1"));
  });

  it("says so when the host is one another browser deleted", async () => {
    open({ "GET /hosts": noHosts }, "/settings/hosts/h1");

    expect(await screen.findByText(/holds no host by that identity/)).toBeInTheDocument();
  });
});

describe("the window samples are kept for", () => {
  it("says how many lowering it removes, before it is applied", async () => {
    const installation = open({
      "GET /samples/retention/outside": { body: { retentionDays: 7, samples: 41_231 } },
      "PUT /samples/retention": { status: 204 },
    });

    const days = await screen.findByLabelText(/kept for/i);

    await userEvent.clear(days);
    await userEvent.type(days, "7");
    await userEvent.click(screen.getByRole("button", { name: "Change the window" }));

    // Counted first and applied second, with the number in between: a settings
    // field that silently destroys data is a bad settings field.
    expect(await screen.findByText(/41231/)).toBeInTheDocument();
    expect(installation.asked).not.toContain("PUT /samples/retention");

    await userEvent.click(screen.getByRole("button", { name: "Lower it and remove them" }));

    await waitFor(() =>
      expect(installation.sentTo("PUT /samples/retention")).toEqual([{ retentionDays: 7 }]),
    );
  });

  it("raises without counting, because nothing leaves and nothing comes back", async () => {
    const installation = open({ "PUT /samples/retention": { status: 204 } });

    const days = await screen.findByLabelText(/kept for/i);

    await userEvent.clear(days);
    await userEvent.type(days, "60");
    await userEvent.click(screen.getByRole("button", { name: "Change the window" }));

    await waitFor(() =>
      expect(installation.sentTo("PUT /samples/retention")).toEqual([{ retentionDays: 60 }]),
    );

    expect(installation.asked).not.toContain("GET /samples/retention/outside");
  });
});
