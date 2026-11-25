import { useMemo, useState, type ReactNode } from "react";
import { useSearchParams } from "react-router";
import { constraints, describeSecurity, httpMethods, type OpenApiDocument, type Operation } from "../openapi/model";
import { MethodBadge, SchemaType } from "./SchemaType";

const untagged = "Other";

/** Searchable, tag-grouped list of a document's endpoints. Each one expands to its parameters, body and responses. */
export function EndpointExplorer({ document, operations }: { document: OpenApiDocument; operations: Operation[] }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const [query, setQuery] = useState("");
  const [method, setMethod] = useState("");
  const tag = searchParams.get("tag") ?? "";
  const open = searchParams.get("open");

  const setParam = (key: string, value: string | null) =>
    setSearchParams((params) => {
      if (value) params.set(key, value);
      else params.delete(key);
      return params;
    }, { replace: true });

  // Tags in the order the document declares them, then any only used on operations.
  const tags = useMemo(() => {
    const names = (document.tags ?? []).map((t) => t.name);
    for (const operation of operations) {
      for (const name of operation.tags.length ? operation.tags : [untagged]) {
        if (!names.includes(name)) names.push(name);
      }
    }
    return names.filter((name) => operations.some((o) => (o.tags.length ? o.tags : [untagged]).includes(name)));
  }, [document, operations]);

  const matches = useMemo(() => {
    const needle = query.trim().toLowerCase();
    return operations.filter(
      (o) =>
        (!method || o.method === method) &&
        (!needle || [o.path, o.summary, o.operationId, o.description].some((text) => text?.toLowerCase().includes(needle))),
    );
  }, [operations, query, method]);

  const groups = tags
    .filter((name) => !tag || name === tag)
    .map((name) => ({
      name,
      description: document.tags?.find((t) => t.name === name)?.description,
      operations: matches.filter((o) => (o.tags.length ? o.tags : [untagged]).includes(name)),
    }))
    .filter((group) => group.operations.length > 0);

  return (
    <div>
      <div className="flex flex-wrap items-center gap-2">
        <input
          type="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Search paths, summaries, operation ids"
          aria-label="Search endpoints"
          className="h-9 min-w-64 flex-1 rounded-md border border-zinc-300 bg-white px-3 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 dark:border-zinc-700 dark:bg-zinc-900"
        />
        <select
          value={method}
          onChange={(event) => setMethod(event.target.value)}
          aria-label="Filter by method"
          className="h-9 rounded-md border border-zinc-300 bg-white px-2 text-sm dark:border-zinc-700 dark:bg-zinc-900"
        >
          <option value="">All methods</option>
          {httpMethods.filter((m) => operations.some((o) => o.method === m)).map((m) => (
            <option key={m} value={m}>{m.toUpperCase()}</option>
          ))}
        </select>
      </div>

      {tags.length > 1 && (
        <div className="mt-3 flex flex-wrap gap-1.5" role="group" aria-label="Filter by tag">
          {["", ...tags].map((name) => (
            <button
              key={name || "all"}
              onClick={() => setParam("tag", name || null)}
              aria-pressed={tag === name}
              className={`rounded-full border px-2.5 py-0.5 text-xs ${
                tag === name
                  ? "border-indigo-600 bg-indigo-600 text-white"
                  : "border-zinc-300 text-zinc-700 hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
              }`}
            >
              {name || "All tags"}
            </button>
          ))}
        </div>
      )}

      <p className="mt-4 text-xs text-zinc-500">
        {matches.length === operations.length ? `${operations.length} endpoints` : `${matches.length} of ${operations.length} endpoints`}
      </p>

      {groups.length === 0 && <p className="mt-6 text-sm text-zinc-500">No endpoints match.</p>}
      {groups.map((group) => (
        <section key={group.name} className="mt-5">
          <h3 className="text-sm font-semibold">{group.name}</h3>
          {group.description && <p className="mt-0.5 text-sm text-zinc-500">{group.description}</p>}
          <ul className="mt-2 divide-y divide-zinc-200 rounded-lg border border-zinc-200 dark:divide-zinc-800 dark:border-zinc-800">
            {group.operations.map((operation) => (
              <EndpointRow
                key={operation.key}
                document={document}
                operation={operation}
                expanded={open === operation.key}
                onToggle={() => setParam("open", open === operation.key ? null : operation.key)}
              />
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

function EndpointRow({
  document,
  operation,
  expanded,
  onToggle,
}: {
  document: OpenApiDocument;
  operation: Operation;
  expanded: boolean;
  onToggle: () => void;
}) {
  const auth = describeSecurity(document, operation.security);
  return (
    <li>
      <button
        onClick={onToggle}
        aria-expanded={expanded}
        className="flex w-full items-center gap-3 px-3 py-2 text-left hover:bg-zinc-50 dark:hover:bg-zinc-900"
      >
        <MethodBadge method={operation.method} />
        <span className={`truncate font-mono text-sm ${operation.deprecated ? "text-zinc-400 line-through" : ""}`}>{operation.path}</span>
        <span className="min-w-0 flex-1 truncate text-sm text-zinc-500">{operation.summary}</span>
        {operation.deprecated && <span className="text-xs text-amber-700 dark:text-amber-400">Deprecated</span>}
        {auth !== "None" && (
          <span className="shrink-0 text-xs text-zinc-500" title={`Authentication: ${auth}`}>
            {auth}
          </span>
        )}
      </button>
      {expanded && <EndpointDetails operation={operation} auth={auth} />}
    </li>
  );
}

function EndpointDetails({ operation, auth }: { operation: Operation; auth: string }) {
  return (
    <div className="space-y-5 border-t border-zinc-200 bg-zinc-50/60 px-4 py-4 text-sm dark:border-zinc-800 dark:bg-zinc-900/40">
      {operation.description && <p className="whitespace-pre-line text-zinc-700 dark:text-zinc-300">{operation.description}</p>}

      <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-1">
        {operation.operationId && (
          <>
            <dt className="text-zinc-500">Operation id</dt>
            <dd className="font-mono">{operation.operationId}</dd>
          </>
        )}
        <dt className="text-zinc-500">Authentication</dt>
        <dd>{auth}</dd>
      </dl>

      {operation.parameters.length > 0 && (
        <DetailTable title="Parameters" headings={["Name", "In", "Type", "Description"]}>
          {operation.parameters.map((p) => (
            <tr key={`${p.in}:${p.name}`}>
              <td className="py-1 pr-4 font-mono">
                {p.name}
                {p.required && <span className="ml-1 text-red-600" title="Required">*</span>}
              </td>
              <td className="py-1 pr-4 text-zinc-500">{p.in}</td>
              <td className="py-1 pr-4"><SchemaType schema={p.schema} /></td>
              <td className="py-1 text-zinc-600 dark:text-zinc-400">
                {p.deprecated ? "Deprecated. " : ""}
                {p.description}
                {constraints(p.schema).length > 0 && <span className="block text-xs text-zinc-500">{constraints(p.schema).join(" · ")}</span>}
              </td>
            </tr>
          ))}
        </DetailTable>
      )}

      {operation.requestBody && (
        <DetailTable title={`Request body${operation.requestBody.required ? " (required)" : ""}`} headings={["Content type", "Schema"]}>
          {operation.requestBody.content.map((c) => (
            <tr key={c.contentType}>
              <td className="py-1 pr-4 font-mono">{c.contentType}</td>
              <td className="py-1"><SchemaType schema={c.schema} /></td>
            </tr>
          ))}
        </DetailTable>
      )}

      {operation.responses.length > 0 && (
        <DetailTable title="Responses" headings={["Status", "Description", "Schema"]}>
          {operation.responses.map((r) => (
            <tr key={r.status}>
              <td className="py-1 pr-4 font-mono">{r.status}</td>
              <td className="py-1 pr-4 text-zinc-600 dark:text-zinc-400">{r.description}</td>
              <td className="py-1">
                {r.content.length === 0 ? (
                  <span className="text-zinc-400">No body</span>
                ) : (
                  r.content.map((c) => (
                    <div key={c.contentType}>
                      <SchemaType schema={c.schema} /> <span className="text-xs text-zinc-500">{c.contentType}</span>
                    </div>
                  ))
                )}
              </td>
            </tr>
          ))}
        </DetailTable>
      )}
    </div>
  );
}

/** A compact table. Every column but the last is sized to its content; the last takes the remaining width. */
function DetailTable({ title, headings, children }: { title: string; headings: string[]; children: ReactNode }) {
  return (
    <div>
      <h4 className="mb-1 text-xs font-semibold uppercase tracking-wide text-zinc-500">{title}</h4>
      <table className="w-full text-left align-top [&_td:not(:last-child)]:w-px [&_td:not(:last-child)]:whitespace-nowrap">
        <thead className="sr-only">
          <tr>{headings.map((h) => <th key={h}>{h}</th>)}</tr>
        </thead>
        <tbody className="align-top">{children}</tbody>
      </table>
    </div>
  );
}
