import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CannotBeClaimed, ClaimScreen } from "./ClaimScreen";
import { anInstallationAnswering } from "../shared/testing";

const PASSWORD = "a passphrase nobody guesses";

const SECRET = "7xcn4bvrk2wm9pld4rty8hq2mnpq";

afterEach(() => vi.unstubAllGlobals());

async function fillIn({ secret }: { secret?: string } = {}) {
  const operator = userEvent.setup();

  if (secret !== undefined) {
    await operator.type(screen.getByLabelText("Claim secret"), secret);
  }

  await operator.type(screen.getByLabelText("Password"), PASSWORD);
  await operator.type(screen.getByLabelText("Password again"), PASSWORD);

  return operator;
}

describe("the claim", () => {
  it("is one request carrying the secret and the password", async () => {
    const claimed = vi.fn();

    const installation = anInstallationAnswering({ "POST /claim": { status: 204 } });

    render(<ClaimScreen needsSecret closesAt={null} onClaimed={claimed} />);

    const operator = await fillIn({ secret: SECRET });
    await operator.click(screen.getByRole("button", { name: /claim this installation/i }));

    expect(claimed).toHaveBeenCalled();

    // One request and no step before it: with the second factor out of the claim
    // there is nothing to draw and nothing to carry (ADR 0041).
    expect(installation.asked).toEqual(["POST /claim"]);
  });

  it("asks for no secret on an installation guarded by a window", async () => {
    const claimed = vi.fn();

    anInstallationAnswering({ "POST /claim": { status: 204 } });

    render(
      <ClaimScreen needsSecret={false} closesAt={null} onClaimed={claimed} />,
    );

    expect(screen.queryByLabelText("Claim secret")).not.toBeInTheDocument();

    const operator = await fillIn();
    await operator.click(screen.getByRole("button", { name: /claim this installation/i }));

    expect(claimed).toHaveBeenCalled();
  });

  it("says the secret was wrong, and says only that", async () => {
    anInstallationAnswering({
      "POST /claim": {
        status: 400,
        body: {
          errors: { secret: ["That is not this installation's claim secret."] },
        },
      },
    });

    render(<ClaimScreen needsSecret closesAt={null} onClaimed={() => undefined} />);

    const operator = await fillIn({ secret: "not the one" });
    await operator.click(screen.getByRole("button", { name: /claim this installation/i }));

    expect(
      await screen.findByText(/not this installation's claim secret/i),
    ).toBeInTheDocument();
  });

  it("names the conflict when somebody else finished first", async () => {
    anInstallationAnswering({ "POST /claim": { status: 409 } });

    render(<ClaimScreen needsSecret={false} closesAt={null} onClaimed={() => undefined} />);

    const operator = await fillIn();
    await operator.click(screen.getByRole("button", { name: /claim this installation/i }));

    expect(await screen.findByText(/somebody else finished first/i)).toBeInTheDocument();
  });

  it("refuses a password the installation would refuse, without asking it", async () => {
    const installation = anInstallationAnswering({});

    render(<ClaimScreen needsSecret={false} closesAt={null} onClaimed={() => undefined} />);

    const operator = userEvent.setup();

    await operator.type(screen.getByLabelText("Password"), "short");
    await operator.type(screen.getByLabelText("Password again"), "short");
    await operator.click(screen.getByRole("button", { name: /claim this installation/i }));

    expect(
      screen.getByText(/^A password is at least 16 characters\.$/),
    ).toBeInTheDocument();
    expect(installation.asked).toEqual([]);
  });
});

describe("an installation that cannot be claimed", () => {
  it("names the host command for a window that lapsed", () => {
    render(<CannotBeClaimed needsSecret={false} />);

    expect(screen.getByText(/claim window opened when this installation first ran/i))
      .toBeInTheDocument();
    expect(screen.getByText(/logaffe recover/)).toBeInTheDocument();
  });

  it("names it for a secret the installation never drew", () => {
    render(<CannotBeClaimed needsSecret />);

    expect(screen.getByText(/guarded by a claim secret and holds none/i)).toBeInTheDocument();
    expect(screen.getByText(/logaffe recover/)).toBeInTheDocument();
  });
});
