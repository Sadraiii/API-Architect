import { useState, type FormEvent } from "react";
import { specificationContentMaxLength, type SpecificationInput } from "../api/specifications";
import { Button, ErrorMessage, TextArea, TextField } from "./ui";

/** The document's info.title, if the text is JSON that has one. Used to suggest a name. */
function titleOf(content: string): string | undefined {
  try {
    const title: unknown = JSON.parse(content)?.info?.title;
    return typeof title === "string" && title.trim() ? title.trim().slice(0, 100) : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Import an OpenAPI JSON document from a file or pasted text. Used both for a first import and for
 * uploading a new version of an existing specification. The API validates the document; this form
 * only checks the size so a huge file fails fast.
 */
export function SpecificationForm({
  initialName = "",
  submitLabel,
  pending,
  error,
  onSubmit,
  onCancel,
}: {
  initialName?: string;
  submitLabel: string;
  pending: boolean;
  error: unknown;
  onSubmit: (input: SpecificationInput) => void;
  onCancel: () => void;
}) {
  const [name, setName] = useState(initialName);
  const [content, setContent] = useState("");
  const [localError, setLocalError] = useState<string>();

  const takeContent = (text: string) => {
    setContent(text);
    setLocalError(text.length > specificationContentMaxLength ? "That document is over the 5,000,000 character limit." : undefined);
    if (!name.trim()) setName(titleOf(text) ?? "");
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!localError) onSubmit({ name: name.trim(), content });
  };

  return (
    <form onSubmit={submit} className="space-y-4">
      <label className="block space-y-1.5">
        <span className="text-sm font-medium">OpenAPI document</span>
        <input
          type="file"
          accept=".json,application/json"
          className="block w-full text-sm file:mr-3 file:rounded-md file:border file:border-zinc-300 file:bg-white file:px-3 file:py-1.5 file:text-sm file:font-medium hover:file:bg-zinc-50 dark:file:border-zinc-700 dark:file:bg-zinc-900 dark:file:text-zinc-100"
          onChange={async (event) => {
            const file = event.target.files?.[0];
            if (file) takeContent(await file.text());
          }}
        />
        <span className="block text-xs text-zinc-500">OpenAPI 3.0, 3.1 or 3.2 as JSON. ASP.NET Core serves yours at /openapi/v1.json by default.</span>
      </label>
      <TextArea
        label="Or paste it"
        value={content.length > 20_000 ? `${content.slice(0, 20_000)}\n…` : content}
        readOnly={content.length > 20_000}
        onChange={(event) => takeContent(event.target.value)}
        className="min-h-32 w-full rounded-md border border-zinc-300 bg-white px-3 py-2 font-mono text-xs dark:border-zinc-700 dark:bg-zinc-900"
        spellCheck={false}
      />
      <TextField label="Name" value={name} maxLength={100} required onChange={(event) => setName(event.target.value)} />
      {localError ? (
        <p role="alert" className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200">{localError}</p>
      ) : (
        <ErrorMessage error={error} />
      )}
      <div className="flex gap-2">
        <Button type="submit" disabled={pending || !content || !name.trim() || !!localError}>{submitLabel}</Button>
        <Button type="button" variant="secondary" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  );
}
