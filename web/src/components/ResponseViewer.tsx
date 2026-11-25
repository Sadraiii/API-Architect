import { useState } from "react";
import type { Execution, HeaderPair } from "../api/execution";
import { CodeView, formatJson } from "./CodeView";
import { Tabs } from "./Tabs";

const tabs = [
  { id: "body", label: "Body" },
  { id: "headers", label: "Headers" },
  { id: "request", label: "Request" },
] as const;
type Tab = (typeof tabs)[number]["id"];

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function StatusBadge({ status }: { status: number | null }) {
  const color =
    status === null
      ? "bg-zinc-200 text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
      : status < 300
        ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300"
        : status < 400
          ? "bg-sky-100 text-sky-800 dark:bg-sky-950 dark:text-sky-300"
          : status < 500
            ? "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300"
            : "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300";
  return <span className={`rounded px-1.5 py-0.5 font-mono text-xs font-medium ${color}`}>{status ?? "—"}</span>;
}

function HeaderTable({ headers }: { headers: HeaderPair[] }) {
  if (headers.length === 0) return <p className="text-sm text-zinc-500">None.</p>;
  return (
    <table className="w-full text-left font-mono text-xs">
      <tbody>
        {headers.map((header, index) => (
          <tr key={index} className="border-b border-zinc-100 align-top last:border-0 dark:border-zinc-800/60">
            <th className="w-1/3 py-1.5 pr-4 font-medium text-zinc-600 dark:text-zinc-400">{header.name}</th>
            <td className="py-1.5 break-all">{header.value}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function hasJsonContentType(headers: HeaderPair[]) {
  return headers.some((h) => h.name.toLowerCase() === "content-type" && /[/+]json\b/i.test(h.value));
}

function Body({ execution }: { execution: Execution }) {
  const [raw, setRaw] = useState(false);
  if (execution.responseBodyIsBinary) {
    return <p className="text-sm text-zinc-500">Binary content ({formatBytes(execution.responseSizeBytes)}), not shown.</p>;
  }
  if (execution.responseBody === "") return <p className="text-sm text-zinc-500">Empty body.</p>;

  // A truncated JSON body won't parse, so it falls back to plain text, which is what the user should see anyway.
  const pretty = hasJsonContentType(execution.responseHeaders) || execution.responseBody.trimStart().startsWith("{")
    ? formatJson(execution.responseBody)
    : null;

  return (
    <div className="space-y-2">
      {pretty !== null && (
        <label className="flex items-center gap-2 text-xs text-zinc-600 dark:text-zinc-400">
          <input type="checkbox" checked={raw} onChange={(e) => setRaw(e.target.checked)} />
          Show raw
        </label>
      )}
      <CodeView text={pretty !== null && !raw ? pretty : execution.responseBody} json={pretty !== null && !raw} />
    </div>
  );
}

function RequestDetails({ execution }: { execution: Execution }) {
  const auth = execution.auth;
  return (
    <div className="space-y-4 text-sm">
      <p className="font-mono text-xs break-all">
        <span className="font-semibold">{execution.method}</span> {execution.url}
      </p>
      {auth && (
        <p className="text-zinc-600 dark:text-zinc-400">
          Auth:{" "}
          {auth.kind === "Bearer"
            ? "Bearer token"
            : auth.kind === "ApiKeyHeader"
              ? `API key in header ${auth.name}`
              : `API key in query parameter ${auth.name}`}{" "}
          (value hidden)
        </p>
      )}
      <div>
        <h3 className="mb-1 text-xs font-semibold text-zinc-500 uppercase">Headers</h3>
        <HeaderTable headers={execution.requestHeaders} />
      </div>
      {execution.requestBody !== null && (
        <div>
          <h3 className="mb-1 text-xs font-semibold text-zinc-500 uppercase">
            Body {execution.requestContentType && <span className="font-normal normal-case">({execution.requestContentType})</span>}
          </h3>
          <CodeView text={execution.requestBody} />
          {execution.requestBodyTruncated && <p className="mt-1 text-xs text-zinc-500">Shortened in history.</p>}
        </div>
      )}
      <p className="text-xs text-zinc-500">Secret values are masked as ••••••.</p>
    </div>
  );
}

/** Shows one execution: status line, then body, headers and the request that was sent. */
export function ResponseViewer({ execution, fromHistory }: { execution: Execution; fromHistory: boolean }) {
  const [tab, setTab] = useState<Tab>("body");

  return (
    <section className="space-y-3">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
        <StatusBadge status={execution.statusCode} />
        {execution.reasonPhrase && <span className="text-zinc-600 dark:text-zinc-400">{execution.reasonPhrase}</span>}
        <span className="text-zinc-500">{execution.durationMs} ms</span>
        {!execution.error && <span className="text-zinc-500">{formatBytes(execution.responseSizeBytes)}</span>}
        <span className="text-zinc-500">{new Date(execution.executedAt).toLocaleString()}</span>
      </div>

      {execution.error ? (
        <div role="alert" className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200">
          <p className="font-mono text-xs">{execution.error.code}</p>
          <p>{execution.error.message}</p>
        </div>
      ) : (
        execution.responseBodyTruncated && (
          <p className="text-xs text-amber-700 dark:text-amber-400">
            {fromHistory
              ? "History keeps a shortened copy of the body. Send the request again to see all of it."
              : "The response was larger than the limit, so only the first part is shown."}
          </p>
        )
      )}

      <Tabs tabs={tabs} active={tab} onChange={setTab} />
      {tab === "body" && !execution.error && <Body execution={execution} />}
      {tab === "body" && execution.error && <p className="text-sm text-zinc-500">No response.</p>}
      {tab === "headers" && <HeaderTable headers={execution.responseHeaders} />}
      {tab === "request" && <RequestDetails execution={execution} />}
    </section>
  );
}
