/**
 * The window a project keeps its entries for, as the two screens that set one
 * have to state it.
 *
 * The ceiling is the one no installation can raise
 * ([ADR 0020](docs/adr/0020-retention-has-a-maximum.md)): without it a settings
 * box quietly turns logaffe into the multi-year archive `VISION.md` says it is
 * not. The installation refuses anything outside these bounds anyway — this is
 * what lets a field say so before it is sent rather than after.
 *
 * **It is a year, and it is not what keeps a window sensible**
 * ([ADR 0048](docs/adr/0048-retentions-ceiling-is-a-year-and-the-setting-says-what-it-costs.md)).
 * What does that is the footprint beside the field: the operator is shown what
 * the window will cost before they apply it, and is refused only where the
 * product genuinely ends.
 */
export const RETENTION_MINIMUM = 1;
export const RETENTION_MAXIMUM = 365;

/**
 * A month, offered because a form needs something in the box and this is the
 * span an operator debugging last week's incident still has. It is the
 * operator's number, and it is changed in the project's settings afterwards.
 */
export const RETENTION_OFFERED = 30;
