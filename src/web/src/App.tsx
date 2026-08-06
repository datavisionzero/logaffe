import { browserTimeZone, formatTimestamp } from "./shared/time";

/**
 * The shell, and nothing more. The three surfaces of docs/ui.md — the project
 * list, the log view, and settings — arrive with the query surface they read
 * through.
 */
export function App() {
  const now = new Date();

  return (
    <main>
      <h1>logaffe</h1>
      <p>
        This installation is running. The interface is not built yet — see{" "}
        <code>docs/ui.md</code> for what belongs here.
      </p>
      <p>
        Times are shown in <strong>{browserTimeZone()}</strong>, absolute and to the
        millisecond: <time dateTime={now.toISOString()}>{formatTimestamp(now)}</time>
      </p>
    </main>
  );
}
