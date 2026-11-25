import type { KeyValue } from "../api/collections";

interface Props {
  label: string;
  rows: KeyValue[];
  onChange: (rows: KeyValue[]) => void;
  namePlaceholder?: string;
}

const cellStyles =
  "w-full min-w-0 rounded border border-zinc-300 bg-white px-2 py-1 font-mono text-xs focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 dark:border-zinc-700 dark:bg-zinc-900";

/** An editable list of headers or query parameters. Unticked rows are kept but not sent. */
export function KeyValueEditor({ label, rows, onChange, namePlaceholder = "Name" }: Props) {
  const update = (index: number, patch: Partial<KeyValue>) =>
    onChange(rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));

  return (
    <div className="space-y-1.5">
      {rows.length === 0 && <p className="text-xs text-zinc-500">No {label.toLowerCase()} yet.</p>}
      {rows.map((row, index) => (
        <div key={index} className="flex items-center gap-2">
          <input
            type="checkbox"
            aria-label={`Send ${row.name || "this row"}`}
            checked={row.enabled}
            onChange={(e) => update(index, { enabled: e.target.checked })}
            className="size-4 accent-indigo-600"
          />
          <input
            aria-label={`${label} name`}
            className={cellStyles}
            placeholder={namePlaceholder}
            value={row.name}
            maxLength={500}
            onChange={(e) => update(index, { name: e.target.value })}
          />
          <input
            aria-label={`${label} value`}
            className={cellStyles}
            placeholder="Value or {{variable}}"
            value={row.value}
            maxLength={8000}
            onChange={(e) => update(index, { value: e.target.value })}
          />
          <button
            type="button"
            aria-label={`Remove ${row.name || "row"}`}
            className="rounded px-1.5 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
            onClick={() => onChange(rows.filter((_, i) => i !== index))}
          >
            ×
          </button>
        </div>
      ))}
      <button
        type="button"
        className="text-xs font-medium text-indigo-600 hover:underline dark:text-indigo-400"
        onClick={() => onChange([...rows, { name: "", value: "", enabled: true }])}
      >
        Add {label.toLowerCase().replace(/s$/, "")}
      </button>
    </div>
  );
}
