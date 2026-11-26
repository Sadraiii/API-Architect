import type { InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from "react";

// Compact controls for the designer's dense tables and forms.

const small =
  "h-8 w-full rounded-md border border-zinc-300 bg-white px-2 text-sm placeholder:text-zinc-400 focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900";

export function SmallInput({ className = "", ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={`${small} ${className}`} {...props} />;
}

export function SmallSelect({ className = "", ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select className={`${small} pr-7 ${className}`} {...props} />;
}

/** A number input that maps an empty box to null. */
export function NumberInput({ value, onChange, ...props }: Omit<InputHTMLAttributes<HTMLInputElement>, "value" | "onChange"> & {
  value: number | null | undefined;
  onChange: (value: number | null) => void;
}) {
  return (
    <SmallInput
      type="number"
      value={value ?? ""}
      onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
      {...props}
    />
  );
}

export function LabeledField({ label, children, className = "" }: { label: string; children: ReactNode; className?: string }) {
  return (
    <label className={`block space-y-1 ${className}`}>
      <span className="text-xs font-medium text-zinc-600 dark:text-zinc-400">{label}</span>
      {children}
    </label>
  );
}

export function Check({ label, checked, onChange, disabled }: { label: string; checked: boolean; onChange: (checked: boolean) => void; disabled?: boolean }) {
  return (
    <label className={`inline-flex items-center gap-1.5 text-sm ${disabled ? "opacity-50" : ""}`}>
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} className="accent-indigo-600" />
      {label}
    </label>
  );
}

const methodColors: Record<string, string> = {
  GET: "text-emerald-700 bg-emerald-50 dark:text-emerald-300 dark:bg-emerald-950",
  POST: "text-sky-700 bg-sky-50 dark:text-sky-300 dark:bg-sky-950",
  PUT: "text-amber-700 bg-amber-50 dark:text-amber-300 dark:bg-amber-950",
  PATCH: "text-violet-700 bg-violet-50 dark:text-violet-300 dark:bg-violet-950",
  DELETE: "text-red-700 bg-red-50 dark:text-red-300 dark:bg-red-950",
};

export function MethodBadge({ method }: { method: string }) {
  return (
    <span className={`inline-block w-14 shrink-0 rounded px-1 py-0.5 text-center font-mono text-[11px] font-semibold ${methodColors[method] ?? ""}`}>
      {method}
    </span>
  );
}

/** A list item in the designer's left-hand column. */
export function ListButton({ selected, onClick, children }: { selected: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm ${
        selected ? "bg-indigo-50 font-medium text-indigo-900 dark:bg-indigo-950 dark:text-indigo-100" : "hover:bg-zinc-100 dark:hover:bg-zinc-800/60"
      }`}
    >
      {children}
    </button>
  );
}

export function TextButton({ onClick, children, danger }: { onClick: () => void; children: ReactNode; danger?: boolean }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`text-sm hover:underline ${danger ? "text-red-600 dark:text-red-400" : "text-indigo-600 dark:text-indigo-400"}`}
    >
      {children}
    </button>
  );
}
