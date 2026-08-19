import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { SampleBand, runs } from "./SampleBand";
import type { HeldWindow } from "./samples";

const FROM = new Date("2026-08-08T10:00:00.000Z");
const TO = new Date("2026-08-08T11:00:00.000Z");

function aBucket(minute: number, over: Partial<HeldWindow["samples"][number]> = {}) {
  return {
    start: new Date(FROM.getTime() + minute * 60_000),
    cpuAverage: 0.42,
    cpuPeak: 0.42,
    memoryUsedAverage: 6_115_295_232,
    memoryUsedPeak: 6_115_295_232,
    memoryTotal: 16_769_712_128,
    loadAverage: 0.52,
    loadPeak: 0.52,
    ...over,
  };
}

function draw(window: Partial<HeldWindow>) {
  render(
    <SampleBand
      window={{ hostName: "web-01", bucketSeconds: 60, samples: [], filesystems: [], ...window }}
      from={FROM}
      to={TO}
    />,
  );
}

describe("the band", () => {
  it("names the machine, which the read hands back and the project does not", () => {
    draw({ samples: [aBucket(0)] });

    expect(screen.getByText("web-01")).toBeInTheDocument();
  });

  it("draws the processor, the memory and the load, and nothing to configure", () => {
    draw({ samples: [aBucket(0)] });

    expect(screen.getByRole("img", { name: "Processor: 42%" })).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Memory: 6.12 GB of 16.8 GB" })).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Load: 0.52" })).toBeInTheDocument();

    // A band and not a dashboard: there is no metric to pick and no arrangement
    // to save, so there is nothing on it to press.
    expect(screen.queryAllByRole("button")).toHaveLength(0);
    expect(screen.queryAllByRole("combobox")).toHaveLength(0);
  });

  it("reads the newest span, not the first", () => {
    draw({
      samples: [aBucket(0, { cpuAverage: 0.1 }), aBucket(1, { cpuAverage: 0.9 })],
    });

    expect(screen.getByRole("img", { name: "Processor: 90%" })).toBeInTheDocument();
  });

  it("draws one track per mount, named as the mount it is", () => {
    draw({
      samples: [aBucket(0)],
      filesystems: [
        {
          start: FROM,
          mount: "/",
          usedAverage: 41_234_567_890,
          usedPeak: 41_234_567_890,
          total: 107_374_182_400,
        },
      ],
    });

    expect(
      screen.getByRole("img", { name: "/: 41.2 GB of 107 GB" }),
    ).toBeInTheDocument();
  });

  // A host with no samples is an ordinary state and not an error: it is what a
  // machine that is switched off looks like, and what a host looks like before
  // its collector is started.
  it("says a range with nothing in it is a range with nothing in it", () => {
    draw({ samples: [] });

    expect(screen.getByText(/reported nothing over this range/)).toBeInTheDocument();
    expect(screen.queryByRole("img")).toBeNull();
  });
});

describe("a gap", () => {
  const step = 1 / 60;

  it("breaks the drawing rather than being drawn through", () => {
    // The most interesting thing a missing minute can mean is that the machine
    // was too busy to report, and a line drawn through it says the opposite.
    const found = runs(
      [
        { at: 0 * step, average: 1, peak: 1 },
        { at: 1 * step, average: 1, peak: 1 },
        { at: 40 * step, average: 1, peak: 1 },
      ],
      step,
    );

    expect(found).toHaveLength(2);
    expect(found[0]).toHaveLength(2);
    expect(found[1]).toHaveLength(1);
  });

  it("is not what two neighbouring spans are", () => {
    const found = runs(
      [
        { at: 0 * step, average: 1, peak: 1 },
        { at: 1 * step, average: 1, peak: 1 },
        { at: 2 * step, average: 1, peak: 1 },
      ],
      step,
    );

    expect(found).toHaveLength(1);
  });

  it("leaves nothing to draw when there is nothing", () => {
    expect(runs([], step)).toEqual([]);
  });
});
