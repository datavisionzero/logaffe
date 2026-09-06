import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BrowserRouter } from "react-router";
import { FirstRun } from "./FirstRun";
import { anInstallationAnswering } from "../shared/testing";

const SNIPPET =
  'curl -X POST https://logs.example.com/ingest -H "Authorization: Bearer logaffe_ingest_abc"';

function open(onDone = () => undefined) {
  window.history.pushState({}, "", "/");

  return render(
    <BrowserRouter>
      <FirstRun onDone={onDone} />
    </BrowserRouter>,
  );
}

afterEach(() => vi.unstubAllGlobals());

/**
 * Past the offer the guide opens with. The second factor is optional
 * (ADR 0041), and declining it is a plain button rather than a dare — so every
 * test below that is about the project starts by taking it.
 */
async function skipTheSecondFactor() {
  const operator = userEvent.setup();

  await operator.click(screen.getByRole("button", { name: /skip this/i }));

  return operator;
}

describe("the first-run guide", () => {
  it("opens with the second factor, and takes no for an answer", async () => {
    const installation = anInstallationAnswering({});

    open();

    expect(screen.getByRole("heading", { name: /a second factor/i })).toBeInTheDocument();

    await skipTheSecondFactor();

    expect(
      screen.getByRole("heading", { name: /a project for the entries/i }),
    ).toBeInTheDocument();

    // Declining costs nothing and asks the installation nothing: the enrolment
    // is drawn only when the operator says yes.
    expect(installation.asked).toEqual([]);
  });

  it("walks the project and the token, and hands over the delivery", async () => {
    const installation = anInstallationAnswering({
      "POST /projects": { body: { id: "3f0", name: "orders-api", retentionDays: 30 } },
      "POST /projects/3f0/ingest-tokens": { body: { deliverySnippet: SNIPPET } },
    });

    let done = false;
    open(() => {
      done = true;
    });

    const operator = await skipTheSecondFactor();

    await operator.type(screen.getByLabelText(/name/i), "orders-api");
    await operator.click(screen.getByRole("button", { name: /create the project/i }));

    await operator.click(
      await screen.findByRole("button", { name: /issue an ingest token/i }),
    );

    // The handover, with the address and the token already in it.
    expect(await screen.findByText(SNIPPET)).toBeInTheDocument();

    await operator.click(screen.getByRole("button", { name: /take me to orders-api/i }));

    expect(done).toBe(true);
    expect(window.location.pathname).toBe("/project/3f0");

    // Two acts and nothing else: the guide is the interface's, and the backend
    // knows nothing about it.
    expect(installation.asked).toEqual([
      "POST /projects",
      "POST /projects/3f0/ingest-tokens",
    ]);
  });

  /**
   * The block is handed over and the copy does not happen. What the operator
   * must not be told is that it did — the text is on the screen to be selected,
   * and only the button knows it failed.
   *
   * The click is fired rather than driven by `userEvent`, because
   * `userEvent.setup()` installs a clipboard of its own and would hand the
   * screen back the very thing this test takes away.
   */
  it("says so when the browser will not copy the delivery", async () => {
    anInstallationAnswering({
      "POST /projects": { body: { id: "3f0", name: "orders-api", retentionDays: 30 } },
      "POST /projects/3f0/ingest-tokens": { body: { deliverySnippet: SNIPPET } },
    });

    open();

    const operator = await skipTheSecondFactor();

    await operator.type(screen.getByLabelText(/name/i), "orders-api");
    await operator.click(screen.getByRole("button", { name: /create the project/i }));
    await operator.click(
      await screen.findByRole("button", { name: /issue an ingest token/i }),
    );

    // A page served over plain http has no Clipboard API at all, and a
    // self-hosted installation with no proxy in front of it is that page.
    vi.stubGlobal("navigator", { userAgent: "a browser on an http page" });

    fireEvent.click(screen.getByRole("button", { name: /copy the delivery/i }));

    expect(await screen.findByText(/only copies from a page served over https/i))
      .toBeInTheDocument();

    expect(screen.queryByRole("button", { name: /^copied$/i })).not.toBeInTheDocument();
  });

  it("can be left before anything is made, and makes nothing", async () => {
    const installation = anInstallationAnswering({});

    let done = false;
    open(() => {
      done = true;
    });

    const operator = await skipTheSecondFactor();
    await operator.click(screen.getByRole("button", { name: /skip this/i }));

    expect(done).toBe(true);
    expect(installation.asked).toEqual([]);
  });

  /**
   * Nothing is half-configured when it is abandoned: the installation was fully
   * claimed the moment the claim completed, and a project without a token is
   * one the ordinary screens already offer to issue one for.
   */
  it("can be left after the project, which stays", async () => {
    const installation = anInstallationAnswering({
      "POST /projects": { body: { id: "3f0", name: "orders-api", retentionDays: 30 } },
    });

    open();

    const operator = await skipTheSecondFactor();

    await operator.type(screen.getByLabelText(/name/i), "orders-api");
    await operator.click(screen.getByRole("button", { name: /create the project/i }));

    await operator.click(await screen.findByRole("button", { name: /skip this/i }));

    expect(installation.asked).toEqual(["POST /projects"]);
    expect(window.location.pathname).toBe("/project/3f0");
  });

  it("says a name the installation already holds, in place", async () => {
    anInstallationAnswering({ "POST /projects": { status: 409 } });

    open();

    const operator = await skipTheSecondFactor();

    await operator.type(screen.getByLabelText(/name/i), "orders-api");
    await operator.click(screen.getByRole("button", { name: /create the project/i }));

    expect(await screen.findByText(/already holds a project by that name/i))
      .toBeInTheDocument();
  });
});
