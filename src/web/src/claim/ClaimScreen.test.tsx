import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ClaimScreen } from "./ClaimScreen";
import { anInstallationAnswering } from "../shared/testing";

const ENROLMENT = {
  secondFactorSecret: "JBSWY3DPEHPK3PXP",
  enrolmentUri: "otpauth://totp/logaffe:operator?secret=JBSWY3DPEHPK3PXP&issuer=logaffe",
  backupCodes: ["4RTY-8HQ2", "K2WM-9PLD", "7XCN-4BVR"],
  ticket: "sealed",
};

const PASSWORD = "a passphrase nobody guesses";

afterEach(() => vi.unstubAllGlobals());

async function walkToTheSheet() {
  const operator = userEvent.setup();

  await operator.type(screen.getByLabelText("Password"), PASSWORD);
  await operator.type(screen.getByLabelText("Password again"), PASSWORD);
  await operator.click(screen.getByRole("button", { name: "Continue" }));

  return operator;
}

describe("the claim", () => {
  it("shows the second factor and the sheet before anything is stored", async () => {
    const installation = anInstallationAnswering({
      "POST /claim/enrolment": { body: ENROLMENT },
    });

    render(<ClaimScreen closesAt={null} onClaimed={() => undefined} />);

    await walkToTheSheet();

    expect(await screen.findByText(ENROLMENT.secondFactorSecret)).toBeInTheDocument();

    for (const code of ENROLMENT.backupCodes) {
      expect(screen.getByText(code)).toBeInTheDocument();
    }

    // Only the last step stores anything (ADR 0014). Up to here the
    // installation has drawn an enrolment and kept none of it.
    expect(installation.asked).toEqual(["POST /claim/enrolment"]);
  });

  it("is finished by a code from the app and one off the sheet", async () => {
    const claimed = vi.fn();

    anInstallationAnswering({
      "POST /claim/enrolment": { body: ENROLMENT },
      "POST /claim": { status: 204 },
    });

    render(<ClaimScreen closesAt={null} onClaimed={claimed} />);

    const operator = await walkToTheSheet();

    await operator.click(await screen.findByRole("checkbox"));
    await operator.click(screen.getByRole("button", { name: "Continue" }));

    await operator.type(screen.getByLabelText(/six digits/i), "123456");
    await operator.type(screen.getByLabelText(/backup code/i), ENROLMENT.backupCodes[0]!);
    await operator.click(screen.getByRole("button", { name: /claim this installation/i }));

    expect(claimed).toHaveBeenCalled();
  });

  it("says which step failed, because the door is open on purpose", async () => {
    anInstallationAnswering({
      "POST /claim/enrolment": { body: ENROLMENT },
      "POST /claim": {
        status: 400,
        body: {
          errors: {
            secondFactorCode: ["That is not a code the authenticator you just enrolled produces now."],
          },
        },
      },
    });

    render(<ClaimScreen closesAt={null} onClaimed={() => undefined} />);

    const operator = await walkToTheSheet();

    await operator.click(await screen.findByRole("checkbox"));
    await operator.click(screen.getByRole("button", { name: "Continue" }));

    await operator.type(screen.getByLabelText(/six digits/i), "000000");
    await operator.type(screen.getByLabelText(/backup code/i), ENROLMENT.backupCodes[0]!);
    await operator.click(screen.getByRole("button", { name: /claim this installation/i }));

    expect(
      await screen.findByText(/not a code the authenticator you just enrolled produces/i),
    ).toBeInTheDocument();
  });

  it("names the conflict when somebody else finished first", async () => {
    anInstallationAnswering({
      "POST /claim/enrolment": { body: ENROLMENT },
      "POST /claim": { status: 409 },
    });

    render(<ClaimScreen closesAt={null} onClaimed={() => undefined} />);

    const operator = await walkToTheSheet();

    await operator.click(await screen.findByRole("checkbox"));
    await operator.click(screen.getByRole("button", { name: "Continue" }));

    await operator.type(screen.getByLabelText(/six digits/i), "123456");
    await operator.type(screen.getByLabelText(/backup code/i), ENROLMENT.backupCodes[0]!);
    await operator.click(screen.getByRole("button", { name: /claim this installation/i }));

    expect(await screen.findByText(/somebody else finished first/i)).toBeInTheDocument();
  });

  it("refuses a password the installation would refuse, without asking it", async () => {
    const installation = anInstallationAnswering({});

    render(<ClaimScreen closesAt={null} onClaimed={() => undefined} />);

    const operator = userEvent.setup();

    await operator.type(screen.getByLabelText("Password"), "short");
    await operator.type(screen.getByLabelText("Password again"), "short");
    await operator.click(screen.getByRole("button", { name: "Continue" }));

    expect(screen.getByText(/at least twelve characters/i)).toBeInTheDocument();
    expect(installation.asked).toEqual([]);
  });
});
