/**
 * The window a project keeps its entries for, as the two screens that set one
 * have to state it.
 *
 * The ceiling is the one no installation can raise
 * ([ADR 0020](docs/adr/0020-retention-has-a-maximum.md)): without it a settings
 * box quietly turns logaffe into the multi-year archive `VISION.md` says it is
 * not. The installation refuses anything outside these bounds anyway — this is
 * what lets a field say so before it is sent rather than after.
 */
export const RETENTION_MINIMUM = 1;
export const RETENTION_MAXIMUM = 90;

/**
 * A month, offered because a form needs something in the box and this is the
 * span an operator debugging last week's incident still has. It is the
 * operator's number, and it is changed in the project's settings afterwards.
 */
export const RETENTION_OFFERED = 30;
