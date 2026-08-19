import { describe, expect, it } from "vitest";
import { formatBytes, formatLoad, formatShare } from "./readings";

describe("bytes", () => {
  it("are in the units the machine was sold in", () => {
    // Powers of a thousand and not of 1024: the operator is comparing what they
    // see here against the number on the invoice for the machine.
    expect(formatBytes(16_769_712_128)).toBe("16.8 GB");
    expect(formatBytes(6_115_295_232)).toBe("6.12 GB");
    expect(formatBytes(107_374_182_400)).toBe("107 GB");
  });

  it("are three figures, because this is a reading to glance at", () => {
    expect(formatBytes(1_500)).toBe("1.50 kB");
    expect(formatBytes(999)).toBe("999 bytes");
  });

  it("say nothing rather than a negative or a NaN", () => {
    expect(formatBytes(0)).toBe("0 bytes");
    expect(formatBytes(Number.NaN)).toBe("0 bytes");
  });
});

describe("a share", () => {
  it("is whole percent, which is the resolution the eye reads", () => {
    expect(formatShare(0.42, 1)).toBe("42%");
    expect(formatShare(6_115_295_232, 16_769_712_128)).toBe("36%");
  });

  // A machine that reported no total is a machine that reported nothing about
  // its size, and a percentage of nothing is not zero.
  it("is a dash when there is no whole to be a share of", () => {
    expect(formatShare(1, 0)).toBe("—");
  });
});

describe("a load average", () => {
  // It is a count of runnable processes and not a share, so it is shown as the
  // number it is rather than as a percentage of anything.
  it("is the number it is", () => {
    expect(formatLoad(0.52)).toBe("0.52");
    expect(formatLoad(11)).toBe("11.00");
  });
});
