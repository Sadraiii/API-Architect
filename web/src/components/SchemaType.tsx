import { Link } from "react-router";
import { schemaRefName, typeLabel, type Schema } from "../openapi/model";

const methodStyles: Record<string, string> = {
  get: "text-sky-700 bg-sky-50 dark:text-sky-300 dark:bg-sky-950",
  post: "text-emerald-700 bg-emerald-50 dark:text-emerald-300 dark:bg-emerald-950",
  put: "text-amber-700 bg-amber-50 dark:text-amber-300 dark:bg-amber-950",
  patch: "text-violet-700 bg-violet-50 dark:text-violet-300 dark:bg-violet-950",
  delete: "text-red-700 bg-red-50 dark:text-red-300 dark:bg-red-950",
};

export function MethodBadge({ method }: { method: string }) {
  return (
    <span
      className={`inline-block w-16 shrink-0 rounded px-1.5 py-0.5 text-center font-mono text-xs font-semibold uppercase ${
        methodStyles[method] ?? "bg-zinc-100 text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
      }`}
    >
      {method}
    </span>
  );
}

/** Link to a component schema in the Schemas view of the current specification page. */
export function SchemaLink({ name }: { name: string }) {
  return (
    <Link
      to={`?view=schemas&schema=${encodeURIComponent(name)}`}
      className="font-mono text-indigo-600 hover:underline dark:text-indigo-400"
    >
      {name}
    </Link>
  );
}

/** A schema's type label, with component schema references (directly or as array items) linked. */
export function SchemaType({ schema }: { schema: Schema | undefined }) {
  const direct = schemaRefName(schema?.$ref);
  if (direct) return <SchemaLink name={direct} />;

  const item = schema?.type === "array" ? schemaRefName(schema.items?.$ref) : undefined;
  if (item) {
    return (
      <span className="font-mono">
        <SchemaLink name={item} />[]
      </span>
    );
  }

  return <span className="font-mono text-zinc-700 dark:text-zinc-300">{typeLabel(schema)}</span>;
}
