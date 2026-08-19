/**
 * What came of asking the browser to put something on the clipboard.
 *
 * <p>
 * `unavailable` is the one worth telling apart, because it is not the browser
 * being difficult: the Clipboard API exists only in a secure context, so a page
 * an operator reached over plain http — which a self-hosted installation with no
 * proxy in front of it is — does not have it at all. `localhost` counts as
 * secure and an address on the network does not.
 */
export type Copying = "copied" | "unavailable" | "refused";

/**
 * Copies, and says what happened.
 *
 * Every block this is used on is on the screen beside the button, so the answer
 * to both failures is the same and the operator loses nothing by hearing it —
 * what they must not be told is that a copy happened when it did not.
 */
export async function copyToClipboard(text: string): Promise<Copying> {
  if (navigator.clipboard === undefined) {
    return "unavailable";
  }

  try {
    await navigator.clipboard.writeText(text);

    return "copied";
  } catch {
    return "refused";
  }
}

/** What to say about a copy that did not happen, and nothing about one that did. */
export function whyNotCopied(copying: Copying | undefined): string | undefined {
  switch (copying) {
    case "unavailable":
      return (
        "This browser only copies from a page served over https. "
        + "Select the text and copy it by hand."
      );

    case "refused":
      return "The browser refused to copy. Select the text and copy it by hand.";

    default:
      return undefined;
  }
}
