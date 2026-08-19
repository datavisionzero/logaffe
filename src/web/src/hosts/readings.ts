/**
 * The numbers a sample carries, as the band writes them.
 *
 * A reading is shown beside its track because a line without a figure says
 * *higher than before* and nothing else, and the question being asked is
 * whether the machine ran out of something.
 */

const UNITS = ["bytes", "kB", "MB", "GB", "TB", "PB"] as const;

/**
 * Bytes in the units a machine is described in.
 *
 * Powers of a thousand rather than of 1024, because that is what the number on
 * the invoice for the machine is in, and the operator is comparing what they
 * see here against what they were sold. Three significant figures: this is a
 * reading to glance at, and `6115295232 bytes` is not one.
 */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) {
    return "0 bytes";
  }

  const magnitude = Math.min(Math.floor(Math.log10(bytes) / 3), UNITS.length - 1);
  const scaled = bytes / 1000 ** magnitude;

  if (magnitude === 0) {
    return `${Math.round(scaled)} bytes`;
  }

  const figures = scaled >= 100 ? 0 : scaled >= 10 ? 1 : 2;

  return `${scaled.toFixed(figures)} ${UNITS[magnitude]}`;
}

/** A share of a whole, as whole percent — the resolution the eye reads. */
export function formatShare(part: number, whole: number): string {
  if (!Number.isFinite(part) || !Number.isFinite(whole) || whole <= 0) {
    return "—";
  }

  return `${Math.round((part / whole) * 100)}%`;
}

/**
 * A load average, which is a count of runnable processes and not a share. It
 * has no ceiling and is shown as the number it is, to two places.
 */
export function formatLoad(load: number): string {
  return Number.isFinite(load) ? load.toFixed(2) : "—";
}
