# The Web Interface Is Tailwind, Base UI, and planaffe's Own Tokens

`src/web` is built on Tailwind CSS v4 as the token layer, Base UI as the
primitive layer, and components generated once by the shadcn CLI into this
repository and owned here. The token values — the warm off-white, IBM Plex, one
teal accent, the radii — are **planaffe's, unchanged**, and the two products are
meant to be recognised as one family.

The comparison behind the foundation was made once and is not repeated here:
[`planaffe/docs/research/frontend-design-foundation.md`](https://github.com/datavisionzero/planaffe/blob/main/docs/research/frontend-design-foundation.md)
measures the options against each other with real bundle sizes, and planaffe's
ADR 0017 draws the conclusion. What it found holds here for the same reasons. A
component library with a design system attached — Mantine is the fastest route
to a working shell — brings its own identity onto every screen and charges for
it on every cold load. Tailwind alone, with everything written by hand, loses on
the part that is hardest to get right and least visible when it is wrong: focus
trapping, roving focus, dismissal, ARIA wiring. A project of one should own its
look and not its focus management.

What logaffe has to decide for itself is everything below.

## The token values are copied, and that is the decision

planaffe's ADR 0017 says the identity of an application built this way lives in
its token set, because the construction is common property. That cuts both ways:
copying the values is not laziness, it is the whole of the choice, and it is the
reason this section exists rather than a line in a stylesheet.

Two products from one author, deployed by the same operator, often open in two
tabs of the same browser, should not argue with each other about what grey is.
The alternative — a second palette, logaffe's own — would have bought a
distinction nobody asked for and paid for it twice, once in the deciding and
again in every screen that has to be kept consistent with a sibling it no longer
matches.

What is **not** copied is the semantic layer. planaffe's `--status-todo` through
`--status-canceled` describe a workflow logaffe does not have. Log levels get
their own tokens, named in the same style and living in the same layer.

## There is no theme switch, and the mechanism arrives without one

[The interface](../ui.md) says there is no column configuration, no layout
setting and no theme setting: the interface follows the colour scheme the
operating system asks for, so that no screenshot carries the question of which
mode it was taken in. That rule stands.

planaffe's ThemeProvider puts the class `dark` on `<html>` and offers a control
for it. The class is the mechanism the whole token layer is written against, so
it comes across; the control does not. The class is set from
`prefers-color-scheme` and follows it when it changes.

## The shell gets a sidebar, and that reverses a written rule

This is the part of the decision that is not about colour.

[The interface](../ui.md) says, in as many words, **no sidebar in the shell**,
and gives a good reason: there are three surfaces and one of them is a list of
lines that must not wrap, so a permanent column beside it spends the width the
log is read in on a menu of three entries. logaffe's shell was built the other
way instead — the installation across the top, the open project in a row beneath
it — because a project's settings had previously sat in the status line of the
log view, which put navigation inside the content.

The rule is reversed. logaffe takes planaffe's model: a sidebar on the left, the
account menu at the top right, a drawer on the phone. The reason is the one this
document opened with. If the two products are to be one family, looking alike is
the smaller half; being operated alike is the larger one, and navigation is
where that is decided. A person who runs both should not have to remember which
of the two puts the project switcher where.

**The objection is answered rather than argued past.** The width the log is read
in is real, and it is bought back by the sidebar's collapse control — which
comes across from planaffe with one change: **its state is not remembered**.
Every load starts expanded. That keeps it a handle and not a setting, which is
what lets the no-layout-settings rule above stay exactly as it is, and what keeps
every screenshot comparable to every other.

**The separation the top bar achieved is kept, not lost.** It was the real
argument for the two-level header, and it survives the move: the open project
sits at the top of the sidebar with its switcher and its destinations, the
installation sits at the bottom behind a separator. What must not come back is
the state before the header — a project's settings in the status line of the log
view. They are a destination of their own and they stay one.

An ADR that overturns a rule written elsewhere in the documentation has to say
so. Quietly editing the sentence out of `docs/ui.md` would leave the next reader
to wonder whether anyone had noticed it was there.

## Consequences

**The components belong to this repository.** Once generated, a Sidebar or a
Dialog is a file under `src/web`, and it is fixed here when it breaks — there is
no `npm update` for a file that has been edited. That is the price of ownership
and it is paid on purpose, the same way planaffe pays it.

**Two repositories now share files that no package connects.** `components/ui/`,
the theme provider, `lib/utils.ts` and the token layer are copies, not a
dependency, and they will drift. That is accepted: extracting a shared package
would mean a third artifact to version and publish for two applications built by
the same person, and the coupling would be tighter than the benefit. When a fix
matters in both, it is carried across by hand, and the copies are small enough
that this is cheaper than the alternative.

**Tailwind's own palette is switched off.** `--color-*: initial` in the token
layer means `bg-red-500` is not a class this project has; a screen speaks the
vocabulary or does not build. This is the mechanism that keeps a copied token set
from quietly growing a local exception.

**The old stylesheet leaves in stages, not at once.** Tailwind and the
handwritten `index.css` coexist while the screens are converted one at a time,
and the file is cut back to its token layer at the end. During the conversion the
application is briefly built two ways, which is the cost of not doing it in one
commit.

**The interface documentation is now downstream of a sibling product.** A change
to planaffe's token values is a change logaffe has decided to want. Nothing
enforces that, and nothing should: it is a judgement made once per change, and
the family resemblance is worth the attention it asks for.
