import { describe, expect, it } from "vitest";
import { formatTimestamp, formatTimestampWithOffset } from "./time";

const instant = new Date("2026-08-06T07:12:03.417Z");

describe("timestamps", () => {
  it("are shown to the millisecond", () => {
    expect(formatTimestamp(instant, "UTC")).toBe("2026-08-06 07:12:03,417");
  });

  it("are shown in the zone the reader is in, not in UTC", () => {
    expect(formatTimestamp(instant, "Europe/Berlin")).toBe("2026-08-06 09:12:03,417");
  });

  it("carry the offset where an instant has to stand on its own", () => {
    expect(formatTimestampWithOffset(instant, "Europe/Berlin")).toBe(
      "2026-08-06 09:12:03,417 UTC+02:00",
    );
  });

  it("are never relative", () => {
    // Whatever else changes here, a formatted timestamp names a moment rather
    // than a distance from now.
    expect(formatTimestamp(instant, "UTC")).not.toMatch(/ago|seit|vor/i);
  });
});
