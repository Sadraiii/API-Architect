export interface KeyValueRow {
  name: string;
  value: string;
}

/** Editable name/value rows. Always keeps one empty row at the end to type into; empty rows are ignored on send. */
export function KeyValueEditor({
  rows,
  onChange,
  namePlaceholder,
}: {
  rows: KeyValueRow[];
  onChange: (rows: KeyValueRow[]) => void;
  namePlaceholder: string;
}) {
  const shown = rows.at(-1)?.name === "" && rows.at(-1)?.value === "" ? rows : [...rows, { name: "", value: "" }];
  const update = (index: number, row: KeyValueRow) => onChange(shown.map((r, i) => (i === index ? row : r)));
  const inputStyles =
    "w-full rounded-md border border-zinc-300 bg-white px-2 py-1.5 font-mono text-xs focus:border-indigo-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900";

  return (
    <div className="space-y-1.5">
      {shown.map((row, index) => (
        <div key={index} className="flex gap-2">
          <input
            aria-label="Name"
            className={inputStyles}
            placeholder={namePlaceholder}
            value={row.name}
            onChange={(e) => update(index, { ...row, name: e.target.value })}
          />
          <input
            aria-label="Value"
            className={inputStyles}
            placeholder="Value"
            value={row.value}
            onChange={(e) => update(index, { ...row, value: e.target.value })}
          />
          <button
            type="button"
            aria-label="Remove"
            className="px-1.5 text-zinc-400 hover:text-zinc-700 disabled:invisible dark:hover:text-zinc-200"
            disabled={index === shown.length - 1}
            onClick={() => onChange(shown.filter((_, i) => i !== index))}
          >
            ×
          </button>
        </div>
      ))}
    </div>
  );
}

export function filledRows(rows: KeyValueRow[]): KeyValueRow[] {
  return rows.filter((row) => row.name.trim() !== "");
}
