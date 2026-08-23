import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { InstallationSettings } from "./InstallationSettings";
import { anInstallationAnswering, type Answer } from "../shared/testing";

/** One tolerated silence, as the area reads it off the installation. */
function aTolerance(name: string, toleratedHours: number) {
  return { projectId: `p-${name}`, name, toleratedHours };
}

/** The whole of the alerts area, with everything the contract requires on it. */
function theAlerts(alerts: {
  notifier?: { server: string; topic: string; hasAccessToken: boolean } | null;
  switches?: {
    fillingUp: boolean;
    goneQuiet: boolean;
    flooding: boolean;
    failing: boolean;
  };
  store?: {
    blindness: string;
    hostId?: string | null;
    hostName?: string | null;
    mount?: string | null;
    percent?: number | null;
  };
  quiet?: {
    busiest?: ReturnType<typeof aTolerance> | null;
    quietest?: ReturnType<typeof aTolerance> | null;
    withoutAFortnight?: number;
  };
  fired?: { subjectId: string; subject: string; condition: string; at: string }[];
}): Answer {
  return {
    body: {
      notifier: alerts.notifier ?? null,
      switches: alerts.switches
        ?? { fillingUp: false, goneQuiet: false, flooding: false, failing: false },
      store: {
        blindness: alerts.store?.blindness ?? "noHostNamed",
        hostId: alerts.store?.hostId ?? null,
        hostName: alerts.store?.hostName ?? null,
        mount: alerts.store?.mount ?? null,
        percent: alerts.store?.percent ?? null,
        firstThreshold: 85,
        secondThreshold: 95,
      },
      quiet: {
        busiest: alerts.quiet?.busiest ?? null,
        quietest: alerts.quiet?.quietest ?? null,
        withoutAFortnight: alerts.quiet?.withoutAFortnight ?? 0,
        multiple: 3,
        leastToleratedHours: 1,
        baselineDays: 14,
      },
      flood: { multiple: 10, floor: 1000, baselineDays: 14 },
      failure: { multiple: 10, floor: 10, baselineDays: 14, consecutiveHours: 2 },
      fired: alerts.fired ?? [],
    },
  };
}

/** One host, as the machine picker reads it off the list. */
function aHost(host: { id: string; name: string }) {
  return {
    id: host.id,
    name: host.name,
    createdAt: "2026-08-01T09:00:00.000Z",
    hostTokens: 1,
    lastReportedAt: "2026-08-08T11:42:07.318Z",
    projects: 1,
  };
}

/** The alerts area, which is an address like every other (`docs/ui.md`). */
function open(routes: Record<string, Answer | Answer[]> = {}) {
  const installation = anInstallationAnswering({
    "GET /alerts": theAlerts({}),
    "GET /hosts": { body: [] },
    ...routes,
  });

  render(
    <MemoryRouter initialEntries={["/settings/alerts"]}>
      <Routes>
        <Route path="/settings" element={<InstallationSettings />} />
        <Route path="/settings/:section" element={<InstallationSettings />} />
      </Routes>
    </MemoryRouter>,
  );

  return installation;
}

afterEach(() => vi.unstubAllGlobals());

describe("the alerts area", () => {
  it("states what a switch will do in this installation's own numbers", async () => {
    open({
      "GET /alerts": theAlerts({
        quiet: {
          busiest: aTolerance("api", 1),
          quietest: aTolerance("nightly-batch", 15),
        },
      }),
    });

    // Not "three times its longest quiet stretch" but the two projects the
    // operator can name and the hours they can picture. A switch whose
    // behaviour has to be looked up is one that gets turned on and distrusted.
    const said = await screen.findByText(/would be noticed after/);

    expect(said).toHaveTextContent("api");
    expect(said).toHaveTextContent("one hour");
    expect(said).toHaveTextContent("nightly-batch");
    expect(said).toHaveTextContent("15 hours");
  });

  it("says a project without a fortnight behind it cannot fire yet", async () => {
    open({ "GET /alerts": theAlerts({ quiet: { withoutAFortnight: 2 } }) });

    expect(
      await screen.findAllByText(/2 projects have less than 14 days of history/),
    ).not.toHaveLength(0);
  });

  it("says a condition that is switched on and cannot see, rather than staying silent", async () => {
    open({
      "GET /alerts": theAlerts({
        switches: { fillingUp: true, goneQuiet: false, flooding: false, failing: false },
        store: { blindness: "mountAbsent", hostId: "h1", mount: "/var/lib/postgresql" },
      }),
    });

    // An operator who believes a disk is being watched when it is not is worse
    // off than one who was never offered the switch.
    expect(
      await screen.findByText(/on and cannot see: the mount named above is not among/),
    ).toBeInTheDocument();
  });

  it("does not warn about a condition nobody has switched on", async () => {
    open({ "GET /alerts": theAlerts({ store: { blindness: "noHostNamed" } }) });

    await screen.findByText("The conditions");

    expect(screen.queryByText(/cannot see/)).not.toBeInTheDocument();
  });

  it("writes all four switches whenever one of them moves", async () => {
    const installation = open({
      "GET /alerts": [
        theAlerts({
          switches: { fillingUp: false, goneQuiet: true, flooding: false, failing: false },
        }),
        theAlerts({
          switches: { fillingUp: false, goneQuiet: true, flooding: true, failing: false },
        }),
      ],
      "PUT /alerts/switches": {},
    });

    await userEvent.click(
      await screen.findByLabelText("Say something when a project floods"),
    );

    // They are one setting with four parts: a screen that saved them
    // separately would have four ways to be half-applied.
    await waitFor(() =>
      expect(installation.sentTo("PUT /alerts/switches")).toEqual([
        { fillingUp: false, goneQuiet: true, flooding: true, failing: false },
      ]),
    );
  });

  it("carries the fourth condition, and what its second hour costs", async () => {
    open({ "GET /alerts": theAlerts({}) });

    await screen.findByText("A project is failing far more than it does");

    // The floor is the errors' own and not the flood's, and the latency the
    // second hour buys is stated rather than left to be worked out.
    // The flood condition states the same multiple and the same fortnight, so
    // what identifies this one is its subject and its own floor.
    expect(screen.getByText(/counted over entries at Error or above/)).toBeInTheDocument();
    expect(screen.getByText(/with a floor of 10 under it/)).toBeInTheDocument();
    expect(screen.getByText(/two closed hours in a row/)).toBeInTheDocument();
    expect(screen.getByText(/up to three when it starts too late/)).toBeInTheDocument();
  });

  it("writes the fourth switch as part of the one setting", async () => {
    const installation = open({
      "GET /alerts": [
        theAlerts({
          switches: { fillingUp: true, goneQuiet: false, flooding: false, failing: false },
        }),
        theAlerts({
          switches: { fillingUp: true, goneQuiet: false, flooding: false, failing: true },
        }),
      ],
      "PUT /alerts/switches": {},
    });

    await userEvent.click(
      await screen.findByLabelText("Say something when a project starts failing"),
    );

    await waitFor(() =>
      expect(installation.sentTo("PUT /alerts/switches")).toEqual([
        { fillingUp: true, goneQuiet: false, flooding: false, failing: true },
      ]),
    );
  });

  it("shows the token only when it is asked for", async () => {
    open({
      "GET /alerts": theAlerts({
        notifier: { server: "https://ntfy.sh/", topic: "logaffe", hasAccessToken: true },
      }),
      "GET /alerts/notifier/token": { body: { token: "tk_secret" } },
    });

    const box = await screen.findByLabelText("Access token");

    expect(box).toHaveValue("");

    await userEvent.click(screen.getByRole("button", { name: "Show the token" }));

    await waitFor(() => expect(box).toHaveValue("tk_secret"));
  });

  it("keeps the sealed token when the operator only corrects the topic", async () => {
    const installation = open({
      "GET /alerts": theAlerts({
        notifier: { server: "https://ntfy.sh/", topic: "logaffe", hasAccessToken: true },
      }),
      "PUT /alerts/notifier": {},
    });

    const topic = await screen.findByLabelText("Topic");

    await userEvent.clear(topic);
    await userEvent.type(topic, "logaffe-alerts");
    await userEvent.click(screen.getByRole("button", { name: "Save the notifier" }));

    // A screen cannot show a secret it is about to overwrite, so a box nobody
    // typed in is "keep what is sealed" rather than "there is none".
    await waitFor(() =>
      expect(installation.sentTo("PUT /alerts/notifier")).toEqual([
        { server: "https://ntfy.sh/", topic: "logaffe-alerts", accessToken: null },
      ]),
    );
  });

  it("says which of the four ways a test send went", async () => {
    open({
      "GET /alerts": theAlerts({
        notifier: { server: "https://ntfy.sh/", topic: "logaffe", hasAccessToken: false },
      }),
      "POST /alerts/notifier/test": { body: { proof: "refused" } },
    });

    await userEvent.click(
      await screen.findByRole("button", { name: "Send a test notification" }),
    );

    // The two refusals are told apart because the operator's next move differs:
    // a server that says no is the notifier's own settings, anything else is
    // the address or the network.
    expect(
      await screen.findByText(/The server answered and said no/),
    ).toBeInTheDocument();
  });

  it("loads a machine's mounts and writes nothing until one is picked", async () => {
    const installation = open({
      "GET /alerts": theAlerts({}),
      "GET /hosts": { body: [aHost({ id: "h1", name: "db" })] },
      "GET /hosts/h1/mounts": { body: ["/", "/var/lib/postgresql"] },
      "PUT /alerts/host": {},
    });

    // The machines arrive after the area does, so the option is what is waited
    // for rather than the box it will be in.
    await screen.findByRole("option", { name: "db" });

    await userEvent.selectOptions(
      screen.getByLabelText("The machine this installation runs on"),
      "h1",
    );

    // The pair goes together: a machine without a mount does not say which of
    // its filesystems the database is on, so choosing one writes nothing.
    await screen.findByLabelText("The mount holding its database");
    expect(installation.sentTo("PUT /alerts/host")).toEqual([]);

    await userEvent.selectOptions(
      screen.getByLabelText("The mount holding its database"),
      "/var/lib/postgresql",
    );

    await waitFor(() =>
      expect(installation.sentTo("PUT /alerts/host")).toEqual([
        { hostId: "h1", mount: "/var/lib/postgresql" },
      ]),
    );
  });

  it("shows when each condition last fired, and says so when none has", async () => {
    open({
      "GET /alerts": theAlerts({
        fired: [
          {
            subjectId: "p1",
            subject: "checkout",
            condition: "goneQuiet",
            at: "2026-08-08T11:00:00.000Z",
          },
        ],
      }),
    });

    // It is the only history there is, and it is not an alert list: one row per
    // subject per condition, with nothing to acknowledge or dismiss.
    const row = (await screen.findByText("checkout")).closest("tr");

    expect(row).toHaveTextContent("Gone quiet");
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("says nothing has fired on an installation nothing has gone wrong on", async () => {
    open();

    expect(await screen.findByText(/Nothing has fired/)).toBeInTheDocument();
  });
});
