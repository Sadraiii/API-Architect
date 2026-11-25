import { useState, type FormEvent } from "react";
import { targetFrameworks, type ProjectInput } from "../api/projects";
import { Button, ErrorMessage, Select, TextArea, TextField } from "./ui";

interface Props {
  initial?: ProjectInput;
  submitLabel: string;
  pending: boolean;
  error: unknown;
  onSubmit: (input: ProjectInput) => void;
  onCancel?: () => void;
}

export function ProjectForm({ initial, submitLabel, pending, error, onSubmit, onCancel }: Props) {
  const [name, setName] = useState(initial?.name ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [targetFramework, setTargetFramework] = useState(initial?.targetFramework ?? "");

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    onSubmit({ name, description: description || null, targetFramework: targetFramework || null });
  }

  return (
    <form onSubmit={handleSubmit} className="max-w-lg space-y-4">
      <TextField label="Name" value={name} onChange={(e) => setName(e.target.value)} required maxLength={100} autoFocus />
      <TextArea label="Description" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={2000} />
      <Select label="Target framework" value={targetFramework} onChange={(e) => setTargetFramework(e.target.value)}>
        <option value="">Not specified</option>
        {targetFrameworks.map((tfm) => (
          <option key={tfm} value={tfm}>
            {tfm}
          </option>
        ))}
      </Select>
      <ErrorMessage error={error} />
      <div className="flex gap-2">
        <Button type="submit" disabled={pending}>
          {pending ? "Saving…" : submitLabel}
        </Button>
        {onCancel && (
          <Button type="button" variant="secondary" onClick={onCancel}>
            Cancel
          </Button>
        )}
      </div>
    </form>
  );
}
