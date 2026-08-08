import { describe, expect, it } from "vitest";
import {
  addressOf,
  carriedToAnotherProject,
  filtersIn,
  keepsGrowing,
  NO_FILTERS,
  namesOfSetFilters,
  queryOf,
} from "./filters";

const NOW = new Date("2026-08-08T12:00:00.000Z");

describe("the filter set is the address", () => {
  it("comes back from a reload as the view it was", () => {
    const set = {
      ...NO_FILTERS,
      span: "1d" as const,
      minimumLevel: "Warning" as const,
      instance: "api-7c4f",
      loggerName: "Orders.Checkout",
      trace: "4bf92f3577b34da6a3ce929d0e0e4736",
      search: "timeout",
      exception: "nullreference",
    };

    expect(filtersIn(new URLSearchParams(addressOf(set)))).toEqual(set);
  });

  it("writes nothing for the view nobody narrowed", () => {
    // The plainest view has the plainest address, so a narrowing is visible in
    // the bar as the thing it is.
    expect(addressOf(NO_FILTERS)).toBe("");
  });

  it("opens at everything, because a view that hides Information shows nothing happened", () => {
    expect(filtersIn(new URLSearchParams("")).minimumLevel).toBeNull();
  });

  it("reads an address carrying from and until as an absolute range", () => {
    const filters = filtersIn(
      new URLSearchParams("?from=2026-08-08T09:00:00.000Z&until=2026-08-08T10:00:00.000Z"),
    );

    expect(filters.span).toBeNull();
    expect(filters.from).toBe("2026-08-08T09:00:00.000Z");
  });
});

describe("what the query surface is asked", () => {
  it("turns a span into a beginning measured from now, and leaves the end open", () => {
    const query = queryOf({ ...NO_FILTERS, span: "15m" }, NOW);

    expect(query.From).toBe("2026-08-08T11:45:00.000Z");
    expect(query.Until).toBeUndefined();
  });

  it("does not send a search text below the minimum", () => {
    // Refused where it was typed rather than spending a request to be told.
    expect(queryOf({ ...NO_FILTERS, search: "ab" }, NOW).Search).toBeUndefined();
    expect(queryOf({ ...NO_FILTERS, search: "abc" }, NOW).Search).toBe("abc");
  });

  it("sends no level when the threshold is everything", () => {
    expect(queryOf(NO_FILTERS, NOW).MinimumLevel).toBeUndefined();
  });
});

describe("whether the range can still grow", () => {
  it("does for a span, which is the live case", () => {
    expect(keepsGrowing({ ...NO_FILTERS, span: "1h" }, NOW)).toBe(true);
  });

  it("does not for an absolute range with an end in the past", () => {
    const history = { ...NO_FILTERS, span: null, from: null, until: "2026-08-08T11:00:00.000Z" };

    expect(keepsGrowing(history, NOW)).toBe(false);
  });

  it("does for an absolute range that has not ended yet", () => {
    const running = { ...NO_FILTERS, span: null, from: null, until: "2026-08-08T13:00:00.000Z" };

    expect(keepsGrowing(running, NOW)).toBe(true);
  });
});

describe("switching project", () => {
  it("keeps the two questions about the world and drops the rest", () => {
    const carried = carriedToAnotherProject({
      ...NO_FILTERS,
      span: "1d",
      minimumLevel: "Error",
      instance: "api-7c4f",
      loggerName: "Orders.Checkout",
      trace: "4bf92f3577b34da6a3ce929d0e0e4736",
      search: "timeout",
      exception: "nullreference",
    });

    expect(carried.span).toBe("1d");
    expect(carried.minimumLevel).toBe("Error");

    // Carrying these over would produce an empty list that looks like an
    // outage: they belong to the project they were found in.
    expect(carried.instance).toBeNull();
    expect(carried.loggerName).toBeNull();
    expect(carried.trace).toBeNull();
    expect(carried.search).toBeNull();
    expect(carried.exception).toBeNull();
  });
});

describe("naming the filters an empty answer is on", () => {
  it("names the range even when nothing else is set", () => {
    expect(namesOfSetFilters(NO_FILTERS)).toEqual(["last hour"]);
  });

  it("leaves out a search text that was never applied", () => {
    expect(namesOfSetFilters({ ...NO_FILTERS, search: "ab" })).toEqual(["last hour"]);
  });
});
