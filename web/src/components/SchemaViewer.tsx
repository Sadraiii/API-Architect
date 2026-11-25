import { useMemo, useState, type ReactNode } from "react";
import { Link, useSearchParams } from "react-router";
import { constraints, objectProperties, typeLabel, type OpenApiDocument, type SchemaEntry } from "../openapi/model";
import { MethodBadge, SchemaLink, SchemaType } from "./SchemaType";

/** The document's component schemas: a searchable list, and the selected schema's properties and relationships. */
export function SchemaViewer({ document, schemas }: { document: OpenApiDocument; schemas: SchemaEntry[] }) {
  const [searchParams] = useSearchParams();
  const [query, setQuery] = useState("");
  const selectedName = searchParams.get("schema") ?? schemas[0]?.name;
  const selected = schemas.find((s) => s.name === selectedName);

  const visible = useMemo(() => {
    const needle = query.trim().toLowerCase();
    return needle ? schemas.filter((s) => s.name.toLowerCase().includes(needle)) : schemas;
  }, [schemas, query]);

  if (schemas.length === 0) {
    return <p className="text-sm text-zinc-500">This document defines no schemas under components.schemas.</p>;
  }

  return (
    <div className="grid grid-cols-[16rem_1fr] gap-6">
      <nav aria-label="Schemas" className="min-w-0">
        <input
          type="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder={`Filter ${schemas.length} schemas`}
          aria-label="Filter schemas"
          className="mb-2 h-9 w-full rounded-md border border-zinc-300 bg-white px-3 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 dark:border-zinc-700 dark:bg-zinc-900"
        />
        <ul className="max-h-[70vh] space-y-0.5 overflow-y-auto">
          {visible.map((s) => (
            <li key={s.name}>
              <Link
                to={`?view=schemas&schema=${encodeURIComponent(s.name)}`}
                replace
                className={`block truncate rounded-md px-2 py-1 font-mono text-sm ${
                  s.name === selectedName
                    ? "bg-zinc-200 font-medium dark:bg-zinc-800"
                    : "text-zinc-600 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800/60"
                }`}
              >
                {s.name}
              </Link>
            </li>
          ))}
        </ul>
      </nav>

      {selected ? <SchemaDetails document={document} entry={selected} /> : <p className="text-sm text-zinc-500">No schema named “{selectedName}”.</p>}
    </div>
  );
}

function SchemaDetails({ document, entry }: { document: OpenApiDocument; entry: SchemaEntry }) {
  const { schema } = entry;
  const properties = objectProperties(document, schema);
  const ownConstraints = constraints(schema);

  return (
    <article className="min-w-0 space-y-6 text-sm">
      <header>
        <h3 className="font-mono text-base font-semibold">{entry.name}</h3>
        <p className="mt-0.5 font-mono text-xs text-zinc-500">{typeLabel(schema)}</p>
        {schema.description && <p className="mt-2 whitespace-pre-line text-zinc-700 dark:text-zinc-300">{schema.description}</p>}
        {ownConstraints.length > 0 && <p className="mt-2 text-xs text-zinc-500">{ownConstraints.join(" · ")}</p>}
      </header>

      {properties.length > 0 && (
        <table className="w-full text-left">
          <thead className="border-b border-zinc-200 text-xs text-zinc-500 dark:border-zinc-800">
            <tr>
              <th className="py-1.5 pr-4 font-medium">Property</th>
              <th className="py-1.5 pr-4 font-medium">Type</th>
              <th className="py-1.5 font-medium">Rules and description</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-zinc-100 align-top dark:divide-zinc-900">
            {properties.map((property) => {
              const rules = constraints(property.schema);
              return (
                <tr key={property.name}>
                  <td className="py-1.5 pr-4 font-mono">
                    {property.name}
                    {property.required && <span className="ml-1 text-red-600" title="Required">*</span>}
                  </td>
                  <td className="py-1.5 pr-4"><SchemaType schema={property.schema} /></td>
                  <td className="py-1.5 text-zinc-600 dark:text-zinc-400">
                    {rules.length > 0 && <p className="text-xs text-zinc-500">{rules.join(" · ")}</p>}
                    {property.schema.description && <p className="whitespace-pre-line">{property.schema.description}</p>}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
      {properties.length > 0 && <p className="text-xs text-zinc-500"><span className="text-red-600">*</span> required</p>}

      <Relationship title="References" empty="No other schemas.">
        {entry.references.map((name) => <SchemaLink key={name} name={name} />)}
      </Relationship>
      <Relationship title="Used by schemas" empty="No other schemas reference this one.">
        {entry.usedBy.map((name) => <SchemaLink key={name} name={name} />)}
      </Relationship>
      <section>
        <h4 className="mb-1 text-xs font-semibold uppercase tracking-wide text-zinc-500">Used by endpoints</h4>
        {entry.usedByOperations.length === 0 ? (
          <p className="text-zinc-500">No endpoint uses this schema directly.</p>
        ) : (
          <ul className="space-y-1">
            {entry.usedByOperations.map((operation) => (
              <li key={operation.key}>
                <Link
                  to={`?view=endpoints&open=${encodeURIComponent(operation.key)}`}
                  className="flex items-center gap-2 hover:underline"
                >
                  <MethodBadge method={operation.method} />
                  <span className="font-mono">{operation.path}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>
    </article>
  );
}

function Relationship({ title, empty, children }: { title: string; empty: string; children: ReactNode[] }) {
  return (
    <section>
      <h4 className="mb-1 text-xs font-semibold uppercase tracking-wide text-zinc-500">{title}</h4>
      {children.length === 0 ? <p className="text-zinc-500">{empty}</p> : <p className="flex flex-wrap gap-x-3 gap-y-1">{children}</p>}
    </section>
  );
}
