import type { ReactNode } from "react";

/** Above this size, highlighting costs more than it helps; the text is shown plain. */
const highlightLimit = 300_000;

const tokenPattern = /("(?:\\.|[^"\\])*")(\s*:)?|\b(true|false|null)\b|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)/g;

/** Splits pretty-printed JSON into coloured spans. Builds React elements, so nothing from the response is parsed as HTML. */
function highlightJson(text: string): ReactNode[] {
  const parts: ReactNode[] = [];
  let last = 0;
  for (const match of text.matchAll(tokenPattern)) {
    const index = match.index ?? 0;
    if (index > last) parts.push(text.slice(last, index));
    const [token, string, colon, literal] = match;
    const className = string
      ? colon
        ? "text-sky-700 dark:text-sky-300"
        : "text-emerald-700 dark:text-emerald-400"
      : literal
        ? "text-purple-700 dark:text-purple-300"
        : "text-amber-700 dark:text-amber-300";
    parts.push(
      <span key={index} className={className}>
        {string ?? token}
      </span>,
    );
    if (colon) parts.push(colon);
    last = index + token.length;
  }
  parts.push(text.slice(last));
  return parts;
}

/** Pretty-prints JSON, or returns null when the text isn't JSON. */
export function formatJson(text: string): string | null {
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return null;
  }
}

export function CodeView({ text, json = false }: { text: string; json?: boolean }) {
  return (
    <pre className="max-h-[32rem] overflow-auto rounded-md border border-zinc-200 bg-zinc-50 p-3 font-mono text-xs leading-relaxed whitespace-pre-wrap break-all dark:border-zinc-800 dark:bg-zinc-900">
      {json && text.length <= highlightLimit ? highlightJson(text) : text}
    </pre>
  );
}
