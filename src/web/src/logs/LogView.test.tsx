import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, useLocation } from "react-router";
import type { HeldProject } from "../projects/projects";
import { LogView } from "./LogView";
import {
  aSampleBucket,
  aSampleWindow,
  anInstallationAnswering,
  type Answer,
} from "../shared/testing";

const PROJECT: HeldProject = {
  id: "p1",
  name: "checkout",
  groupId: null,
  hostId: null,
  retentionDays: 30,
  createdAt: new Date("2026-08-01T09:00:00.000Z"),
  ingestTokens: 1,
  lastReceivedAt: new Date("2026-08-08T11:59:00.000Z"),
  muted: false,
};

/** A project nothing has ever delivered to, which is a different screen. */
const UNTOUCHED: HeldProject = { ...PROJECT, lastReceivedAt: null };

/** The same project, on a machine — which is what puts a band above its log. */
const ON_A_HOST: HeldProject = { ...PROJECT, hostId: "h1" };

function anEntry(entry: {
  id: number;
  eventTime: string;
  level?: string;
  loggerName?: string | null;
  instance?: string | null;
  trace?: string | null;
  message?: string;
  messageTruncated?: boolean;
  hasException?: boolean;
}) {
  return {
    id: entry.id,
    eventTime: entry.eventTime,
    level: entry.level ?? "Information",
    loggerName: entry.loggerName ?? null,
    instance: entry.instance ?? null,
    trace: entry.trace ?? null,
    message: entry.message ?? "Something happened",
    messageTruncated: entry.messageTruncated ?? false,
    hasException: entry.hasException ?? false,
  };
}

/** The tail that answers nothing, which is every view that is not testing it. */
const QUIET_TAIL: Answer = { body: { entries: [], next: "arrived-at-0", more: false } };

function Address() {
  const { search } = useLocation();

  return <span data-testid="address">{search}</span>;
}

function open(project: HeldProject = PROJECT, at = "/project/p1") {
  render(
    <MemoryRouter initialEntries={[at]}>
      <LogView project={project} />
      <Address />
    </MemoryRouter>,
  );

  return {
    address: () => screen.getByTestId("address").textContent ?? "",
  };
}

/**
 * The lines of the list, scoped to it. A native `<option>` inside the filter
 * bar's selects carries the same ARIA role as an entry, so an unscoped query
 * finds the controls as well as the log.
 */
async function lines() {
  const list = await screen.findByRole("listbox", { name: "Entries" });

  return within(list).getAllByRole("option");
}

afterEach(() => vi.unstubAllGlobals());

describe("the entry line", () => {
  it("carries the time to the millisecond, the level as a word, and the shortened logger", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": {
        body: {
          entries: [
            anEntry({
              id: 41,
              eventTime: "2026-08-08T11:59:07.318Z",
              level: "Error",
              loggerName: "Logaffe.Api.Http.EntryEndpoints",
              message: "The read expired",
            }),
          ],
          next: null,
        },
      },
      "GET /projects/p1/entries/tail": QUIET_TAIL,
    });

    open();

    const line = (await lines())[0]!;

    expect(within(line).getByRole("time").textContent).toMatch(
      /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}[.,]\d{3}$/,
    );

    // A word with a colour behind it, and never a colour alone.
    expect(within(line).getByText("Error")).toBeInTheDocument();

    // The segments that tell two loggers apart are at the end.
    expect(within(line).getByText("Http.EntryEndpoints")).toBeInTheDocument();
    expect(within(line).getByText("The read expired")).toBeInTheDocument();
  });

  it("marks an entry that carries an exception", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": {
        body: {
          entries: [anEntry({ id: 41, eventTime: "2026-08-08T11:59:07.318Z", hasException: true })],
          next: null,
        },
      },
      "GET /projects/p1/entries/tail": QUIET_TAIL,
    });

    open();

    expect(await screen.findByTitle("Carries an exception")).toBeInTheDocument();
  });
});

describe("narrowing", () => {
  it("takes its value from a line that is already on the screen", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": {
        body: {
          entries: [
            anEntry({
              id: 41,
              eventTime: "2026-08-08T11:59:07.318Z",
              loggerName: "Orders.Checkout",
            }),
          ],
          next: null,
        },
      },
      "GET /projects/p1/entries/tail": QUIET_TAIL,
    });

    const view = open();
    const operator = userEvent.setup();

    await operator.click(await screen.findByRole("button", { name: "Orders.Checkout" }));

    // The address is where a filter set is kept, so the back button walks the
    // narrowings just made.
    await waitFor(() => expect(view.address()).toContain("loggerName=Orders.Checkout"));
  });

  it("is removed one chip at a time", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": { body: { entries: [], next: null } },
      "GET /projects/p1/entries/tail": QUIET_TAIL,
    });

    const view = open(PROJECT, "/project/p1?loggerName=Orders.Checkout&instance=api-7c4f");
    const operator = userEvent.setup();

    await operator.click(await screen.findByRole("button", { name: /remove the logger filter/i }));

    await waitFor(() => expect(view.address()).not.toContain("loggerName"));
    expect(view.address()).toContain("instance=api-7c4f");
  });
});

describe("the entry detail", () => {
  it("names both clocks, shows no message template, and narrows to a trace", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": {
        body: {
          entries: [anEntry({ id: 41, eventTime: "2026-08-08T11:59:07.318Z" })],
          next: null,
        },
      },
      "GET /projects/p1/entries/tail": QUIET_TAIL,
      "GET /projects/p1/entries/41": {
        body: {
          id: 41,
          eventTime: "2026-08-08T11:59:07.318Z",
          receiptTime: "2026-08-08T11:59:07.512Z",
          level: "Error",
          loggerName: "Orders.Checkout",
          instance: "api-7c4f",
          trace: "4bf92f3577b34da6a3ce929d0e0e4736",
          span: "00f067aa0ba902b7",
          messageTemplate: "Order {OrderId} failed",
          message: "Order 4711 failed",
          exception: "System.NullReferenceException: …",
          properties: { OrderId: 4711 },
          messageTruncated: false,
          exceptionTruncated: false,
        },
      },
    });

    const view = open();
    const operator = userEvent.setup();

    await operator.click((await lines())[0]!);

    const detail = await screen.findByRole("complementary", { name: "Entry" });

    expect(within(detail).getByText(/the sender's clock/)).toBeInTheDocument();
    expect(within(detail).getByText(/^— ours$/)).toBeInTheDocument();

    // Stored for fidelity and never displayed (ADR 0005): the operator reads
    // the sentence, not the shape it was made from.
    expect(within(detail).queryByText("Order {OrderId} failed")).toBeNull();
    expect(within(detail).getByText("Order 4711 failed")).toBeInTheDocument();

    await operator.click(
      within(detail).getByRole("button", { name: "4bf92f3577b34da6a3ce929d0e0e4736" }),
    );

    await waitFor(() =>
      expect(view.address()).toContain("trace=4bf92f3577b34da6a3ce929d0e0e4736"),
    );
  });

  it("states a truncation in words where the text ends", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": {
        body: {
          entries: [
            anEntry({ id: 41, eventTime: "2026-08-08T11:59:07.318Z", messageTruncated: true }),
          ],
          next: null,
        },
      },
      "GET /projects/p1/entries/tail": QUIET_TAIL,
      "GET /projects/p1/entries/41": {
        body: {
          id: 41,
          eventTime: "2026-08-08T11:59:07.318Z",
          receiptTime: "2026-08-08T11:59:07.512Z",
          level: "Information",
          loggerName: null,
          instance: null,
          trace: null,
          span: null,
          messageTemplate: "…",
          message: "A very long sentence that",
          exception: null,
          properties: null,
          messageTruncated: true,
          exceptionTruncated: false,
        },
      },
    });

    open();

    const operator = userEvent.setup();

    await operator.click((await lines())[0]!);

    expect(await screen.findByText(/cut at its cap on the way in/i)).toBeInTheDocument();
  });
});

describe("the keyboard", () => {
  it("walks the entries, opens the detail and closes it", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": {
        body: {
          entries: [
            anEntry({ id: 42, eventTime: "2026-08-08T11:59:08.000Z" }),
            anEntry({ id: 41, eventTime: "2026-08-08T11:59:07.000Z" }),
          ],
          next: null,
        },
      },
      "GET /projects/p1/entries/tail": QUIET_TAIL,
      "GET /projects/p1/entries/42": {
        body: {
          id: 42,
          eventTime: "2026-08-08T11:59:08.000Z",
          receiptTime: "2026-08-08T11:59:08.100Z",
          level: "Information",
          loggerName: null,
          instance: null,
          trace: null,
          span: null,
          messageTemplate: "x",
          message: "Something happened",
          exception: null,
          properties: null,
          messageTruncated: false,
          exceptionTruncated: false,
        },
      },
    });

    open();

    const operator = userEvent.setup();

    await lines();

    await operator.keyboard("{ArrowDown}");
    expect((await lines())[0]).toHaveAttribute("aria-selected", "true");

    await operator.keyboard("{Enter}");
    expect(await screen.findByRole("complementary", { name: "Entry" })).toBeInTheDocument();

    await operator.keyboard("{Escape}");
    await waitFor(() =>
      expect(screen.queryByRole("complementary", { name: "Entry" })).toBeNull(),
    );
  });
});

/**
 * The way into a project's settings is the shell's second level and is asserted
 * there (`shell/Shell.test.tsx`). This view carries no navigation of its own:
 * everything on it narrows the list in front of it.
 */

describe("the two ways of being empty", () => {
  it("hands over the delivery when nothing has ever arrived", async () => {
    anInstallationAnswering({
      "GET /projects/p1/ingest-tokens": {
        body: [
          {
            id: "t1",
            identifier: "abc",
            issuedAt: "2026-08-01T09:00:00.000Z",
            lastUsedAt: null,
          },
        ],
      },
      "GET /ingest-tokens/t1/token": {
        body: { token: "logaffe_ingest_…", deliverySnippet: "curl -X POST https://logs/ingest" },
      },
    });

    open(UNTOUCHED);

    expect(await screen.findByText(/curl -X POST/)).toBeInTheDocument();

    // No page was read: there is nothing to filter and nothing to page.
    expect(screen.queryByRole("listbox", { name: "Entries" })).toBeNull();
  });

  it("offers the act that issues a token when the project holds none", async () => {
    anInstallationAnswering({
      "GET /projects/p1/ingest-tokens": { body: [] },
      "POST /projects/p1/ingest-tokens": {
        status: 201,
        body: {
          id: "t1",
          token: "logaffe_ingest_…",
          deliverySnippet: "curl -X POST https://logs/ingest",
          issuedAt: "2026-08-08T12:00:00.000Z",
        },
      },
    });

    open(UNTOUCHED);

    const operator = userEvent.setup();

    await operator.click(await screen.findByRole("button", { name: /issue an ingest token/i }));

    expect(await screen.findByText(/curl -X POST/)).toBeInTheDocument();
  });

  it("names the filters when a filter set matched nothing", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": { body: { entries: [], next: null } },
      "GET /projects/p1/entries/tail": QUIET_TAIL,
    });

    const view = open(PROJECT, "/project/p1?minimumLevel=Error&loggerName=Orders.Checkout");

    const empty = await screen.findByText(/no entries match these filters/i);
    const named = empty.parentElement!;

    expect(within(named).getByText("Error and above")).toBeInTheDocument();
    expect(within(named).getByText("logger Orders.Checkout")).toBeInTheDocument();

    const operator = userEvent.setup();

    await operator.click(screen.getByRole("button", { name: /clear the filters/i }));

    await waitFor(() => expect(view.address()).toBe(""));
  });
});

describe("a read that took too long", () => {
  it("says what to narrow and keeps the filters exactly as they were", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": {
        status: 408,
        body: { narrow: ["SmallerTimeRange", "ExceptionText"] },
      },
      "GET /projects/p1/entries/tail": QUIET_TAIL,
    });

    const view = open(PROJECT, "/project/p1?exception=nullreference");

    const said = await screen.findByText(/shorter one/i);
    const expired = said.closest(".expired") as HTMLElement;

    expect(within(expired).getByText(/no index serves it/i)).toBeInTheDocument();

    // Never a database error, and the next attempt is one adjustment rather
    // than a rebuild.
    expect(within(expired).queryByText(/error/i)).toBeNull();
    expect(view.address()).toContain("exception=nullreference");
  });
});

describe("the live tail", () => {
  it("places a late entry among the entries it belongs with, below the newest line", async () => {
    vi.useFakeTimers();

    try {
      anInstallationAnswering({
        "GET /projects/p1/entries": {
          body: {
            entries: [
              anEntry({ id: 42, eventTime: "2026-08-08T11:59:08.000Z", message: "The newest" }),
              anEntry({ id: 40, eventTime: "2026-08-08T11:59:06.000Z", message: "The oldest" }),
            ],
            next: null,
          },
        },
        "GET /projects/p1/entries/tail": [
          // The first poll arms the tail and answers nothing.
          { body: { entries: [], next: "arrived-at-0", more: false } },
          {
            body: {
              entries: [
                anEntry({
                  id: 41,
                  eventTime: "2026-08-08T11:59:07.000Z",
                  message: "Delivered late",
                }),
              ],
              next: "arrived-at-1",
              more: false,
            },
          },
        ],
      });

      open();

      // The page answers, the first poll arms the tail, and the second one —
      // five seconds later — is the one carrying the late entry.
      await vi.advanceTimersByTimeAsync(0);
      await vi.advanceTimersByTimeAsync(0);
      await vi.advanceTimersByTimeAsync(5_000);
      await vi.advanceTimersByTimeAsync(0);

      const list = screen.getByRole("listbox", { name: "Entries" });
      const messages = within(list)
        .getAllByRole("option")
        .map((line) => line.querySelector(".entry-message")?.textContent);

      // The cursor runs on receipt time and the list stays ordered by event
      // time, so what arrived last is not what is on top (ADR 0009).
      expect(messages).toEqual(["The newest", "Delivered late", "The oldest"]);
    } finally {
      vi.useRealTimers();
    }
  });

  it("never starts on a range with an end in the past", async () => {
    const installation = anInstallationAnswering({
      "GET /projects/p1/entries": { body: { entries: [], next: null } },
    });

    open(PROJECT, "/project/p1?from=2026-01-01T00:00:00.000Z&until=2026-01-02T00:00:00.000Z");

    await screen.findByText(/no entries match/i);

    // A closed range cannot grow, so there is nothing to follow.
    expect(installation.asked).toEqual(["GET /projects/p1/entries"]);
    expect(screen.getByText(/not following/i)).toBeInTheDocument();
  });
});

describe("the band over the entries", () => {
  it("is absent for a project on no host, and asks for nothing", async () => {
    const installation = anInstallationAnswering({ "GET /projects/p1/entries": QUIET_TAIL });

    open();

    await screen.findByText(/Following/);

    expect(screen.queryByRole("img")).toBeNull();

    // A project on no host is ordinary — it is every project until the operator
    // says otherwise — and it costs that project nothing but the band.
    expect(installation.asked.some((route) => route.includes("/samples"))).toBe(false);
  });

  it("is drawn for the host the project sits on, over the range the filters state", async () => {
    const installation = anInstallationAnswering({
      "GET /projects/p1/entries": QUIET_TAIL,
      "GET /hosts/h1/samples": aSampleWindow({
        hostName: "web-01",
        samples: [aSampleBucket({ start: "2026-08-08T11:00:00.000Z", cpuAverage: 0.91 })],
      }),
    });

    open(ON_A_HOST);

    // The name comes back with the samples: the project carries the host's
    // identity and nothing that names it.
    expect(await screen.findByText("web-01")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Processor: 91%" })).toBeInTheDocument();

    const read = installation.asked.filter((route) => route === "GET /hosts/h1/samples");

    // Once, not on the entries' five-second interval: a sample changes once a
    // minute, and a band redrawn twelve times per reading would be eleven
    // requests for a picture that did not move.
    expect(read).toHaveLength(1);
  });

  it("moves when the range moves", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": QUIET_TAIL,
      "GET /hosts/h1/samples": [
        aSampleWindow({ samples: [aSampleBucket({ start: "2026-08-08T11:00:00.000Z" })] }),
        aSampleWindow({
          samples: [aSampleBucket({ start: "2026-08-08T11:00:00.000Z", cpuAverage: 0.07 })],
        }),
      ],
    });

    open(ON_A_HOST);

    await screen.findByRole("img", { name: "Processor: 42%" });

    await userEvent.selectOptions(
      screen.getByLabelText("Time range"),
      "15m",
    );

    expect(
      await screen.findByRole("img", { name: "Processor: 7%" }),
    ).toBeInTheDocument();
  });

  it("says a read that used up its five seconds in the filters' own terms", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": QUIET_TAIL,
      "GET /hosts/h1/samples": { status: 408, body: { narrow: ["SmallerTimeRange"] } },
    });

    open(ON_A_HOST);

    // Never a database error and never a failed request in a corner.
    expect(
      await screen.findByText("Make the time range a shorter one."),
    ).toBeInTheDocument();
  });

  it("keeps the entries when the host was deleted from another browser", async () => {
    anInstallationAnswering({
      "GET /projects/p1/entries": {
        body: {
          entries: [anEntry({ id: 1, eventTime: "2026-08-08T11:59:07.318Z" })],
          next: "arrived-at-1",
          more: false,
        },
      },
      "GET /hosts/h1/samples": { status: 404 },
    });

    open(ON_A_HOST);

    expect(await screen.findByText(/host this project sat on is gone/)).toBeInTheDocument();

    // A project on no host loses the band and nothing else.
    expect(await lines()).toHaveLength(1);
  });
});
