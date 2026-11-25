import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router";
import { ApiError } from "../api/client";
import {
  httpMethods,
  useClearHistory,
  useExecute,
  useExecution,
  useExecutionHistory,
  type AuthKind,
  type ExecuteInput,
  type Execution,
  type HttpMethod,
} from "../api/execution";
import { useProject } from "../api/projects";
import { formatJson } from "../components/CodeView";
import { filledRows, KeyValueEditor, type KeyValueRow } from "../components/KeyValueEditor";
import { ResponseViewer, StatusBadge } from "../components/ResponseViewer";
import { Tabs } from "../components/Tabs";
import { Button, ErrorMessage } from "../components/ui";

const editorTabs = [
  { id: "params", label: "Params" },
  { id: "headers", label: "Headers" },
  { id: "auth", label: "Auth" },
  { id: "body", label: "Body" },
] as const;
type EditorTab = (typeof editorTabs)[number]["id"];

const contentTypes: string[] = ["application/json", "text/plain", "application/xml", "application/x-www-form-urlencoded"];

const fieldStyles =
  "rounded-md border border-zinc-300 bg-white px-2.5 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 dark:border-zinc-700 dark:bg-zinc-900";

/** Adds the params table to whatever query string the URL already has. */
function buildUrl(url: string, params: KeyValueRow[]): string {
  const rows = filledRows(params);
  if (rows.length === 0) return url.trim();
  const query = new URLSearchParams(rows.map((row) => [row.name, row.value])).toString();
  const [base = "", hash] = url.trim().split("#", 2);
  const joined = base + (base.includes("?") ? (base.endsWith("?") || base.endsWith("&") ? "" : "&") : "?") + query;
  return hash === undefined ? joined : `${joined}#${hash}`;
}

export function RequestPage() {
  const { id: projectId = "" } = useParams();
  const project = useProject(projectId);
  const execute = useExecute(projectId);
  const history = useExecutionHistory(projectId);
  const clearHistory = useClearHistory(projectId);

  const [method, setMethod] = useState<HttpMethod>("GET");
  const [url, setUrl] = useState("");
  const [params, setParams] = useState<KeyValueRow[]>([]);
  const [headers, setHeaders] = useState<KeyValueRow[]>([]);
  const [authKind, setAuthKind] = useState<AuthKind | "None">("None");
  const [authName, setAuthName] = useState("X-Api-Key");
  const [authValue, setAuthValue] = useState("");
  const [body, setBody] = useState("");
  const [contentType, setContentType] = useState("application/json");
  const [tab, setTab] = useState<EditorTab>("params");

  // What the viewer shows: the response just received, or an entry picked from history.
  const [live, setLive] = useState<Execution | null>(null);
  const [historyId, setHistoryId] = useState<string | null>(null);
  const fromHistory = useExecution(historyId);
  const shown = historyId ? fromHistory.data : live;

  if (project.error instanceof ApiError && project.error.status === 404) {
    return <p className="text-sm">This project doesn't exist, or it isn't yours.</p>;
  }

  function send(event: FormEvent) {
    event.preventDefault();
    const input: ExecuteInput = {
      method,
      url: buildUrl(url, params),
      headers: filledRows(headers),
      body: body === "" ? null : body,
      contentType: body === "" ? null : contentType,
      auth: authKind === "None" ? null : { kind: authKind, value: authValue, name: authKind === "Bearer" ? null : authName },
    };
    execute.mutate(input, {
      onSuccess: (execution) => {
        setLive(execution);
        setHistoryId(null);
      },
    });
  }

  const jsonBodyError = contentType === "application/json" && body.trim() !== "" && formatJson(body) === null;

  return (
    <div className="grid gap-8 xl:grid-cols-[1fr_18rem]">
      <div className="min-w-0 space-y-6">
        <div>
          <Link to={`/projects/${projectId}`} className="text-sm text-zinc-500 hover:underline">
            {project.data?.name ?? "Project"}
          </Link>
          <h1 className="mt-1 text-lg font-semibold">Send a request</h1>
        </div>

        <form onSubmit={send} className="space-y-4">
          <div className="flex gap-2">
            <select aria-label="Method" className={`${fieldStyles} font-mono`} value={method} onChange={(e) => setMethod(e.target.value as HttpMethod)}>
              {httpMethods.map((m) => (
                <option key={m}>{m}</option>
              ))}
            </select>
            <input
              aria-label="URL"
              className={`${fieldStyles} min-w-0 flex-1 font-mono`}
              placeholder="https://api.example.com/orders"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              required
            />
            <Button type="submit" disabled={execute.isPending}>
              {execute.isPending ? "Sending…" : "Send"}
            </Button>
          </div>
          <p className="text-xs text-zinc-500">
            Requests are sent from the API Architect server, so they can reach public APIs only. localhost and private network
            addresses are blocked.
          </p>

          <Tabs tabs={editorTabs} active={tab} onChange={setTab} />

          {tab === "params" && <KeyValueEditor rows={params} onChange={setParams} namePlaceholder="Parameter" />}
          {tab === "headers" && <KeyValueEditor rows={headers} onChange={setHeaders} namePlaceholder="Header" />}
          {tab === "auth" && (
            <div className="space-y-3 text-sm">
              <select aria-label="Auth type" className={fieldStyles} value={authKind} onChange={(e) => setAuthKind(e.target.value as AuthKind | "None")}>
                <option value="None">No auth</option>
                <option value="Bearer">Bearer token</option>
                <option value="ApiKeyHeader">API key in header</option>
                <option value="ApiKeyQuery">API key in query string</option>
              </select>
              {authKind !== "None" && (
                <div className="flex gap-2">
                  {authKind !== "Bearer" && (
                    <input aria-label="Key name" className={`${fieldStyles} font-mono`} value={authName} onChange={(e) => setAuthName(e.target.value)} />
                  )}
                  <input
                    aria-label={authKind === "Bearer" ? "Token" : "Key"}
                    type="password"
                    autoComplete="off"
                    className={`${fieldStyles} min-w-0 flex-1 font-mono`}
                    placeholder={authKind === "Bearer" ? "Token" : "Key"}
                    value={authValue}
                    onChange={(e) => setAuthValue(e.target.value)}
                  />
                </div>
              )}
              {authKind !== "None" && <p className="text-xs text-zinc-500">Sent with the request but never saved in history.</p>}
            </div>
          )}
          {tab === "body" && (
            <div className="space-y-2">
              <div className="flex items-center gap-2">
                <select aria-label="Content type" className={`${fieldStyles} font-mono text-xs`} value={contentType} onChange={(e) => setContentType(e.target.value)}>
                  {contentTypes.map((type) => (
                    <option key={type}>{type}</option>
                  ))}
                </select>
                {contentType === "application/json" && (
                  <Button type="button" variant="secondary" disabled={jsonBodyError || body === ""} onClick={() => setBody(formatJson(body) ?? body)}>
                    Format
                  </Button>
                )}
              </div>
              <textarea
                aria-label="Body"
                spellCheck={false}
                className={`${fieldStyles} min-h-40 w-full font-mono text-xs`}
                value={body}
                onChange={(e) => setBody(e.target.value)}
              />
              {jsonBodyError && <p className="text-xs text-amber-700 dark:text-amber-400">This isn't valid JSON. It will be sent as written.</p>}
            </div>
          )}

          <ErrorMessage error={execute.error} />
        </form>

        {shown ? (
          <ResponseViewer key={shown.id} execution={shown} fromHistory={historyId !== null} />
        ) : historyId && fromHistory.error ? (
          <ErrorMessage error={fromHistory.error} />
        ) : (
          !historyId && <p className="text-sm text-zinc-500">Send a request to see the response here.</p>
        )}
      </div>

      <aside className="min-w-0">
        <div className="mb-2 flex items-center justify-between">
          <h2 className="text-sm font-semibold">History</h2>
          {(history.data?.length ?? 0) > 0 && (
            <button type="button" className="text-xs text-zinc-500 hover:underline" disabled={clearHistory.isPending} onClick={() => clearHistory.mutate(undefined, { onSuccess: () => setHistoryId(null) })}>
              Clear
            </button>
          )}
        </div>
        <ErrorMessage error={history.error ?? clearHistory.error} />
        {history.data?.length === 0 && <p className="text-sm text-zinc-500">No requests yet.</p>}
        <ul className="space-y-0.5">
          {history.data?.map((entry) => (
            <li key={entry.id}>
              <button
                type="button"
                onClick={() => setHistoryId(entry.id)}
                className={`w-full rounded-md px-2 py-1.5 text-left hover:bg-zinc-100 dark:hover:bg-zinc-800/60 ${
                  shown?.id === entry.id ? "bg-zinc-100 dark:bg-zinc-800/60" : ""
                }`}
              >
                <span className="flex items-center gap-2 text-xs">
                  <span className="w-12 font-mono font-semibold">{entry.method}</span>
                  <StatusBadge status={entry.statusCode} />
                  <span className="ml-auto text-zinc-500">{entry.durationMs} ms</span>
                </span>
                <span className="mt-0.5 block truncate font-mono text-xs text-zinc-600 dark:text-zinc-400" title={entry.url}>
                  {entry.url}
                </span>
              </button>
            </li>
          ))}
        </ul>
      </aside>
    </div>
  );
}
