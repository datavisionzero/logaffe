import type { HeldWindow } from "./samples";
import { formatBytes, formatLoad, formatShare } from "./readings";
import { formatTimestamp } from "../shared/time";

/**
 * One point of a track: where it sits across the range, and the two numbers the
 * span carried.
 */
interface Point {
  /** Across the range, from 0 at its start to 1 at its end. */
  at: number;
  average: number;
  peak: number;
}

/**
 * What a machine was doing over a range, drawn.
 *
 * **It is a band and not a dashboard** (`docs/metrics.md`): there is nothing on
 * it to configure, pick or save, no metric to choose and no arrangement to
 * keep. The same component draws it above a project's entries and on a host's
 * own screen, because those are the same numbers over two ranges rather than
 * two views of them.
 *
 * **The peak is drawn behind the average**, filled, with the average as a line
 * on top. An average is exactly what hides the spike that was worth finding,
 * and a band that showed only one of the two would be a band that flattens the
 * minute an operator came here to look at.
 *
 * **A gap is drawn as a gap.** A span the host reported nothing in is a span
 * that is not there at all, and the line stops and starts again around it — the
 * most interesting thing a missing minute can mean is that the machine was too
 * busy to report, and a line drawn through it says the opposite.
 */
export function SampleBand({
  window,
  from,
  to,
}: {
  window: HeldWindow;
  from: Date;
  to: Date;
}) {
  const range = to.getTime() - from.getTime();

  // A span as a share of the range, which is the width one bucket is drawn at
  // and the distance beyond which two of them are not neighbours.
  const step = range > 0 ? Math.min((window.bucketSeconds * 1000) / range, 1) : 1;

  const at = (start: Date) => (range > 0 ? (start.getTime() - from.getTime()) / range : 0);

  const newest = window.samples.at(-1);

  // How large the machine is, rather than how much of it was in use, and it is
  // the newest reading of it: a machine that was given more memory halfway
  // through the range is drawn against what it has now.
  const memoryTotal = newest?.memoryTotal ?? 0;

  // The load has no ceiling of its own — it is a count of runnable processes,
  // not a share — so the track is scaled to the tallest peak in the range, and
  // never to less than one, or an idle machine would draw a mountain.
  const loadCeiling = Math.max(1, ...window.samples.map((bucket) => bucket.loadPeak));

  const mounts = [...new Set(window.filesystems.map((bucket) => bucket.mount))].sort();

  if (window.samples.length === 0) {
    return (
      <div className="band">
        <BandHead window={window} from={from} to={to} />
        <p className="quiet">
          This host reported nothing over this range. A host with no samples is an
          ordinary state — it is what a machine that is switched off looks like, and what
          a host looks like before its collector is started.
        </p>
      </div>
    );
  }

  return (
    <div className="band">
      <BandHead window={window} from={from} to={to} />

      <div className="band-tracks">
        <Track
          name="Processor"
          reading={newest === undefined ? "—" : formatShare(newest.cpuAverage, 1)}
          points={window.samples.map((bucket) => ({
            at: at(bucket.start),
            average: bucket.cpuAverage,
            peak: bucket.cpuPeak,
          }))}
          ceiling={1}
          step={step}
        />

        <Track
          name="Memory"
          reading={
            newest === undefined
              ? "—"
              : `${formatBytes(newest.memoryUsedAverage)} of ${formatBytes(newest.memoryTotal)}`
          }
          points={window.samples.map((bucket) => ({
            at: at(bucket.start),
            average: bucket.memoryUsedAverage,
            peak: bucket.memoryUsedPeak,
          }))}
          ceiling={memoryTotal}
          step={step}
        />

        <Track
          name="Load"
          reading={newest === undefined ? "—" : formatLoad(newest.loadAverage)}
          points={window.samples.map((bucket) => ({
            at: at(bucket.start),
            average: bucket.loadAverage,
            peak: bucket.loadPeak,
          }))}
          ceiling={loadCeiling}
          step={step}
        />

        {/* One track per mount, and which mounts there are is named in the
            collector's configuration rather than discovered — a machine that
            mounts forty container overlays does not become forty tracks. */}
        {mounts.map((mount) => {
          const readings = window.filesystems.filter((bucket) => bucket.mount === mount);
          const last = readings.at(-1);

          return (
            <Track
              key={mount}
              name={mount}
              reading={
                last === undefined
                  ? "—"
                  : `${formatBytes(last.usedAverage)} of ${formatBytes(last.total)}`
              }
              points={readings.map((bucket) => ({
                at: at(bucket.start),
                average: bucket.usedAverage,
                peak: bucket.usedPeak,
              }))}
              ceiling={last?.total ?? 0}
              step={step}
            />
          );
        })}
      </div>
    </div>
  );
}

/**
 * Which machine this is and which range it is over. The name comes back with
 * the samples rather than from a second request: a project carries the host's
 * identity and nothing that names it.
 */
function BandHead({ window, from, to }: { window: HeldWindow; from: Date; to: Date }) {
  return (
    <div className="band-head">
      <b>{window.hostName}</b>
      <span className="quiet">
        <time dateTime={from.toISOString()}>{formatTimestamp(from)}</time> to{" "}
        <time dateTime={to.toISOString()}>{formatTimestamp(to)}</time>
      </span>
    </div>
  );
}

/**
 * One number over the range: the peak as an area, the average as a line on it.
 *
 * The drawing is an inline `<svg>` and nothing else. A charting library here
 * would be a dependency, a bundle and a configuration surface bought to draw
 * four polylines that carry no axes, no legend, no tooltip and no interaction —
 * a band is looked at, not operated.
 */
function Track({
  name,
  reading,
  points,
  ceiling,
  step,
}: {
  name: string;
  reading: string;
  points: Point[];
  ceiling: number;
  /** The width of one span across the range, which is what makes a gap one. */
  step: number;
}) {
  // Stretched to whatever width the screen gives it, which is why the stroke
  // asks not to be scaled with it: the box is in the drawing's own units and
  // the line is in the reader's.
  const height = (value: number) =>
    ceiling > 0 ? 100 - Math.min(Math.max(value / ceiling, 0), 1) * 100 : 100;

  const across = (at: number) => Math.min(Math.max(at, 0), 1) * 100;

  /**
   * A bucket is a span and not an instant, so each one is drawn as the width it
   * covers rather than as a point joined to the next. That is what a bucketed
   * answer actually says — *over this minute, the peak was this* — and it is
   * also what makes a single reading in an empty range visible instead of a
   * line with one end.
   */
  const steps = (run: Point[], value: (point: Point) => number) =>
    run.flatMap((point) => [
      `${across(point.at)},${height(value(point))}`,
      `${across(point.at + step)},${height(value(point))}`,
    ]);

  return (
    <div className="track">
      <div className="track-name">
        <span>{name}</span>
        <span className="track-reading">{reading}</span>
      </div>

      <svg
        className="track-drawing"
        viewBox="0 0 100 100"
        preserveAspectRatio="none"
        role="img"
        aria-label={`${name}: ${reading}`}
      >
        {runs(points, step).map((run, index) => (
          <g key={index}>
            <polygon
              className="track-peak"
              points={[
                `${across(run[0]!.at)},100`,
                ...steps(run, (point) => point.peak),
                `${across(run[run.length - 1]!.at + step)},100`,
              ].join(" ")}
            />
            <polyline
              className="track-average"
              vectorEffect="non-scaling-stroke"
              points={steps(run, (point) => point.average).join(" ")}
            />
          </g>
        ))}
      </svg>
    </div>
  );
}

/**
 * The contiguous runs of a track, which is what draws a gap as a gap.
 *
 * Spans come back only where the host reported, so two neighbouring points in
 * the answer can be an hour apart in the range. A step and a half is the
 * threshold: buckets are contiguous and equal, so anything wider than one is a
 * span the machine said nothing in.
 */
export function runs(points: Point[], step: number): Point[][] {
  const found: Point[][] = [];
  let run: Point[] = [];

  for (const point of points) {
    const previous = run.at(-1);

    if (previous !== undefined && point.at - previous.at > step * 1.5) {
      found.push(run);
      run = [];
    }

    run.push(point);
  }

  if (run.length > 0) {
    found.push(run);
  }

  return found;
}
