import "@testing-library/jest-dom/vitest";
import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

afterEach(cleanup);

// jsdom lays nothing out, so it implements neither of these. They are how a
// list keeps the row the keyboard walked in view and how it returns to the top,
// and neither is a thing to assert about — what matters is that calling them is
// not what a screen falls over on.
Element.prototype.scrollIntoView ??= () => undefined;
Element.prototype.scrollTo ??= () => undefined;
