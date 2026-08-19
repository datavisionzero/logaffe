import { afterEach, describe, expect, it, vi } from "vitest";
import { copyToClipboard, whyNotCopied } from "./clipboard";

afterEach(() => vi.unstubAllGlobals());

describe("copying", () => {
  it("hands the text over when it can", async () => {
    const written: string[] = [];

    vi.stubGlobal("navigator", {
      clipboard: {
        writeText: (text: string) => {
          written.push(text);

          return Promise.resolve();
        },
      },
    });

    expect(await copyToClipboard("the delivery")).toBe("copied");
    expect(written).toEqual(["the delivery"]);
  });

  /**
   * The Clipboard API is a secure-context interface. A page an operator reached
   * over plain http, on anything but localhost, does not have one at all — and a
   * self-hosted installation with no proxy in front of it is exactly that page.
   */
  it("says so when the page has no clipboard at all", async () => {
    vi.stubGlobal("navigator", { userAgent: "a browser on an http page" });

    expect(await copyToClipboard("the delivery")).toBe("unavailable");
  });

  it("says so when the browser refuses", async () => {
    vi.stubGlobal("navigator", {
      clipboard: { writeText: () => Promise.reject(new Error("not allowed")) },
    });

    expect(await copyToClipboard("the delivery")).toBe("refused");
  });

  it("has nothing to say about a copy that happened, or one not yet asked for", () => {
    expect(whyNotCopied("copied")).toBeUndefined();
    expect(whyNotCopied(undefined)).toBeUndefined();
  });

  it("names the reason the page cannot argue with", () => {
    expect(whyNotCopied("unavailable")).toContain("https");
    expect(whyNotCopied("refused")).toBeDefined();
  });
});
