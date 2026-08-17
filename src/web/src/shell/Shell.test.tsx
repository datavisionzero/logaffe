import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { BrowserRouter } from "react-router";
import { Shell } from "./Shell";
import {
  anInstallationAnswering,
  noGroups,
  withSecondFactor,
  withoutSecondFactor,
  type Answer,
} from "../shared/testing";

function open(routes: Record<string, Answer | Answer[]>) {
  window.history.pushState({}, "", "/");

  anInstallationAnswering({
    "GET /groups": noGroups,
    "GET /projects": { body: [] },
    ...routes,
  });

  return render(
    <BrowserRouter>
      <Shell backupCodesRemaining={null} onSignedOut={() => undefined} />
    </BrowserRouter>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("an installation with no second factor", () => {
  /**
   * The second factor is offered rather than required (ADR 0041), and the
   * interface is the only thing that can keep an omission from passing for a
   * setting — so this is said on every screen and cannot be dismissed.
   */
  it("says so, and points at the act that ends it", async () => {
    open({ "GET /second-factor": withoutSecondFactor });

    expect(await screen.findByText(/has no second factor/i)).toBeInTheDocument();

    const enrol = screen.getByRole("link", { name: /enrol one/i });
    expect(enrol).toHaveAttribute("href", "/settings/credentials");

    // Not dismissible: it is the state of the account rather than a warning
    // about something that went wrong.
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("says nothing at all when one is enrolled", async () => {
    open({ "GET /second-factor": withSecondFactor });

    await screen.findByRole("link", { name: "logaffe" });

    expect(screen.queryByText(/has no second factor/i)).not.toBeInTheDocument();
  });
});
