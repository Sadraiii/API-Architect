import { useRef, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { ApiError } from "../api/client";
import { useCollection, useCollections, useCreateCollection, useImportCollection } from "../api/collections";
import { useProject } from "../api/projects";
import { CollectionTree } from "../components/CollectionTree";
import { EnvironmentSwitcher } from "../components/EnvironmentSwitcher";
import { RequestEditor } from "../components/RequestEditor";
import { Button, ErrorMessage } from "../components/ui";

/** The request workspace: collections on the left, the selected request on the right. */
export function CollectionsPage() {
  const { id: projectId = "" } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  // Both ids are in the URL, so a request can be linked to and survives a reload.
  const selectedRequestId = searchParams.get("request");
  const selectedCollectionId = searchParams.get("collection");
  const project = useProject(projectId);
  const collections = useCollections(projectId);
  const select = (collectionId: string, requestId: string | null) =>
    setSearchParams(requestId ? { collection: collectionId, request: requestId } : {});

  if (project.error instanceof ApiError && project.error.status === 404) {
    return <p className="text-sm">This project doesn't exist, or it isn't yours. <Link className="text-indigo-600 hover:underline" to="/projects">Back to projects</Link></p>;
  }

  return (
    <div className="flex h-[calc(100vh-3rem)] flex-col">
      <header className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="text-sm">
          <Link to={`/projects/${projectId}`} className="text-zinc-500 hover:underline">{project.data?.name ?? "Project"}</Link>
          <span className="mx-1.5 text-zinc-400">/</span>
          <span className="font-semibold">Collections</span>
        </div>
        <EnvironmentSwitcher projectId={projectId} />
      </header>

      <div className="flex min-h-0 flex-1 gap-6">
        <aside className="w-80 shrink-0 overflow-y-auto border-r border-zinc-200 pr-3 dark:border-zinc-800">
          <SidebarActions projectId={projectId} />
          <ErrorMessage error={collections.error} />
          {collections.isPending && <p className="text-sm text-zinc-500">Loading…</p>}
          {collections.data?.length === 0 && (
            <p className="mt-3 text-sm text-zinc-500">No collections yet. Create one, or import a Postman collection.</p>
          )}
          {collections.data?.map((summary) => (
            <CollectionTree
              key={summary.id}
              projectId={projectId}
              summary={summary}
              selectedRequestId={selectedRequestId}
              onSelect={(requestId) => select(summary.id, requestId)}
            />
          ))}
        </aside>

        <section className="min-w-0 flex-1 overflow-y-auto pb-8">
          {selectedRequestId && selectedCollectionId ? (
            <SelectedRequest
              projectId={projectId}
              collectionId={selectedCollectionId}
              requestId={selectedRequestId}
              onSelect={(requestId) => select(selectedCollectionId, requestId)}
            />
          ) : (
            <p className="mt-10 text-center text-sm text-zinc-500">Select a request, or add one from a collection's actions.</p>
          )}
        </section>
      </div>
    </div>
  );
}

function SelectedRequest({ projectId, collectionId, requestId, onSelect }: { projectId: string; collectionId: string; requestId: string; onSelect: (id: string | null) => void }) {
  const collection = useCollection(collectionId);
  if (collection.error) return <ErrorMessage error={collection.error} />;
  if (!collection.data) return null;
  return (
    <RequestEditor
      projectId={projectId}
      requestId={requestId}
      collection={collection.data}
      onDuplicated={onSelect}
      onDeleted={() => onSelect(null)}
    />
  );
}

function SidebarActions({ projectId }: { projectId: string }) {
  const [naming, setNaming] = useState(false);
  const [name, setName] = useState("");
  const [warnings, setWarnings] = useState<string[]>([]);
  const [fileError, setFileError] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const create = useCreateCollection(projectId);
  const importCollection = useImportCollection(projectId);

  async function handleFile(file: File) {
    setWarnings([]);
    setFileError(null);
    let json: unknown;
    try {
      json = JSON.parse(await file.text());
    } catch {
      setFileError(`${file.name} isn't a JSON file.`);
      return;
    }
    importCollection.mutate(json, { onSuccess: (result) => setWarnings(result.warnings) });
  }

  return (
    <div className="mb-2 space-y-2">
      {naming ? (
        <form
          className="flex gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate({ name, description: null }, { onSuccess: () => { setNaming(false); setName(""); } });
          }}
        >
          <input
            aria-label="Collection name"
            placeholder="Collection name"
            className="min-w-0 flex-1 rounded-md border border-zinc-300 bg-white px-2 py-1 text-sm dark:border-zinc-700 dark:bg-zinc-900"
            value={name}
            maxLength={200}
            required
            autoFocus
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => e.key === "Escape" && setNaming(false)}
          />
          <Button type="submit" disabled={create.isPending}>Create</Button>
        </form>
      ) : (
        <div className="flex gap-2">
          <Button onClick={() => setNaming(true)}>New collection</Button>
          <Button variant="secondary" disabled={importCollection.isPending} onClick={() => fileInput.current?.click()}>
            {importCollection.isPending ? "Importing…" : "Import Postman"}
          </Button>
          <input
            ref={fileInput}
            type="file"
            accept=".json,application/json"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0];
              e.target.value = "";
              if (file) void handleFile(file);
            }}
          />
        </div>
      )}
      <ErrorMessage error={create.error ?? importCollection.error} />
      {fileError && <p role="alert" className="text-sm text-red-600 dark:text-red-400">{fileError}</p>}
      {warnings.length > 0 && (
        <div className="rounded-md border border-amber-200 bg-amber-50 p-2 text-xs text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <p className="font-medium">Imported, with some parts skipped:</p>
          <ul className="mt-1 list-disc pl-4">{warnings.map((w) => <li key={w}>{w}</li>)}</ul>
          <button type="button" className="mt-1 underline" onClick={() => setWarnings([])}>Dismiss</button>
        </div>
      )}
    </div>
  );
}
