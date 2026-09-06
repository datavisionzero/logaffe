import { useState, type FormEvent } from "react";
import { api, problemWith } from "../api/client";
import { PASSWORD_MINIMUM } from "../session/password";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { Field } from "../components/Field";
import { About, Area, Said } from "./Area";

/**
 * Changing the password, which requires the current one and ends every other
 * session.
 *
 * It is confirmed by typing it twice, which is furniture the product avoids
 * everywhere else and earns here for the reason the claim earns it: there is no
 * password reset over the network and no email to send one to (ADR 0015), so a
 * typo that gets stored is answered by Host Recovery and nothing smaller.
 */
export function ChangePassword() {
  const [current, setCurrent] = useState("");
  const [chosen, setChosen] = useState("");
  const [again, setAgain] = useState("");
  const [problems, setProblems] = useState<{ current?: string; chosen?: string }>({});
  const [refusal, setRefusal] = useState<string>();
  const [changed, setChanged] = useState(false);
  const [changing, setChanging] = useState(false);

  const mismatched = again.length > 0 && again !== chosen;

  async function change(event: FormEvent) {
    event.preventDefault();
    setProblems({});
    setRefusal(undefined);
    setChanged(false);

    if (chosen.length < PASSWORD_MINIMUM || again !== chosen) {
      setProblems({
        chosen: `Give a password of at least ${PASSWORD_MINIMUM} characters, twice.`,
      });
      return;
    }

    setChanging(true);

    try {
      const { response, error } = await api.PUT("/password", {
        body: { currentPassword: current, newPassword: chosen },
      });

      if (response.status === 204) {
        setChanged(true);
        setCurrent("");
        setChosen("");
        setAgain("");
        return;
      }

      if (response.status === 400) {
        setProblems({
          current: problemWith(error, "currentPassword"),
          chosen: problemWith(error, "newPassword"),
        });
        return;
      }

      setRefusal("This installation refused the change.");
    } catch {
      setRefusal("This installation did not answer.");
    } finally {
      setChanging(false);
    }
  }

  return (
    <Area title="Password">
      <About>
        At least {PASSWORD_MINIMUM} characters, and nothing else is asked of it: length is
        the property that matters and the second factor carries the rest. Changing it
        <b> ends every other session</b>, which is what makes it the thing to reach for
        after a cookie has gone somewhere it should not have.
      </About>

      <form onSubmit={change} className="grid max-w-md gap-3">
        <Field label="Current password">
          <Input
            type="password"
            autoComplete="current-password"
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
            aria-invalid={problems.current !== undefined || undefined}
          />
        </Field>
        <Said problem={problems.current} />

        <Field label="New password">
          <Input
            type="password"
            autoComplete="new-password"
            value={chosen}
            onChange={(e) => setChosen(e.target.value)}
            aria-invalid={problems.chosen !== undefined || undefined}
          />
        </Field>
        <Said problem={problems.chosen} />

        <Field label="New password again">
          <Input
            type="password"
            autoComplete="new-password"
            value={again}
            onChange={(e) => setAgain(e.target.value)}
            aria-invalid={mismatched || undefined}
          />
        </Field>
        {mismatched && <p className="refusal text-sm">These two are not the same.</p>}

        <Said problem={refusal} />
        {changed && (
          <p className="quiet">
            Changed. Every other session has ended; this browser stays signed in.
          </p>
        )}

        <Button type="submit" disabled={changing} className="w-fit">
          Change the password
        </Button>
      </form>
    </Area>
  );
}
