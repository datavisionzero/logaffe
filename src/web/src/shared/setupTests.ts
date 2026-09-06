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

// The same emptiness, one level up: the sidebar asks the viewport whether it is
// a phone's, and the theme asks it which colour scheme the operating system
// wants. Neither question has an answer in jsdom, and the false both get here
// is the desktop in its light scheme — the shape every test was written
// against.
window.matchMedia ??= (query: string) =>
  ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  }) as MediaQueryList;
