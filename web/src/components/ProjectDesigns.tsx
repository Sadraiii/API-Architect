import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { useCreateDesign, useProjectDesigns } from "../api/designs";
import { Button, ErrorMessage, TextField } from "./ui";

/** The project's API designs, with the form to start a new one. */
export function ProjectDesigns({ projectId }: { projectId: string }) {
  const designs = useProjectDesigns(projectId);
  const createDesign = useCreateDesign(projectId);
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [startFrom, setStartFrom] = useState<"blank" | "example">("blank");

  const submit = (event: FormEvent) => {
    event.preventDefault();
    createDesign.mutate({ name, startFrom }, { onSuccess: (design) => navigate(`/designs/${design.id}`) });
  };

  return (
    <section className="mt-10">
      <header className="mb-3 flex items-center justify-between">
        <div>
          <h2 className="text-sm font-semibold">API designs</h2>
          <p className="text-sm text-zinc-500">Design an API's models and endpoints, then generate its OpenAPI document and a .NET 11 project.</p>
        </div>
        {!creating && <Button onClick={() => setCreating(true)}>New design</Button>}
      </header>

      {creating && (
        <form onSubmit={submit} className="mb-4 space-y-4 rounded-lg border border-zinc-200 p-5 dark:border-zinc-800">
          <TextField label="Name" value={name} onChange={(e) => setName(e.target.value)} maxLength={100} required autoFocus placeholder="Orders API" />
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium">Start from</legend>
            <label className="flex items-center gap-2 text-sm">
              <input type="radio" name="startFrom" checked={startFrom === "blank"} onChange={() => setStartFrom("blank")} />
              A blank design
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input type="radio" name="startFrom" checked={startFrom === "example"} onChange={() => setStartFrom("example")} />
              The Bookstore example: two models and five CRUD endpoints, some needing auth
            </label>
          </fieldset>
          <ErrorMessage error={createDesign.error} />
          <div className="flex gap-2">
            <Button type="submit" disabled={createDesign.isPending}>Create design</Button>
            <Button type="button" variant="secondary" onClick={() => { setCreating(false); createDesign.reset(); }}>Cancel</Button>
          </div>
        </form>
      )}

      <ErrorMessage error={designs.error} />
      {designs.data?.length === 0 && !creating && (
        <p className="rounded-lg border border-dashed border-zinc-300 p-6 text-center text-sm text-zinc-500 dark:border-zinc-700">
          No designs yet. Start one to define a new API.
        </p>
      )}
      {designs.data && designs.data.length > 0 && (
        <ul className="divide-y divide-zinc-200 rounded-lg border border-zinc-200 dark:divide-zinc-800 dark:border-zinc-800">
          {designs.data.map((design) => (
            <li key={design.id}>
              <Link to={`/designs/${design.id}`} className="flex items-baseline justify-between gap-4 px-4 py-3 hover:bg-zinc-50 dark:hover:bg-zinc-900">
                <p className="truncate text-sm font-medium">{design.name}</p>
                <span className="shrink-0 text-xs text-zinc-500">Updated {new Date(design.updatedAt).toLocaleDateString()}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
