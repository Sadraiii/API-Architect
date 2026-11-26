import { lazy, Suspense, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { ApiError } from "../api/client";
import { useServerSend } from "../api/execution";
import { useProject } from "../api/projects";
import {
  documentUrl,
  useDeleteSpecification,
  useReplaceSpecification,
  useSpecification,
  useSpecificationDocument,
  type Specification,
} from "../api/specifications";
import { EndpointExplorer } from "../components/EndpointExplorer";
import { SchemaViewer } from "../components/SchemaViewer";
import { SpecificationForm } from "../components/SpecificationForm";
import { Button, ErrorMessage } from "../components/ui";
import { listOperations, listSchemas, type OpenApiDocument } from "../openapi/model";

// Scalar is large; load it only when someone opens a reference.
const ScalarReference = lazy(() => import("../components/ScalarReference"));

const views = [
  { id: "reference", label: "Reference" },
  { id: "endpoints", label: "Endpoints" },
  { id: "schemas", label: "Schemas" },
] as const;

export function SpecificationPage() {
  const { id = "" } = useParams();
  const specification = useSpecification(id);

  if (specification.isPending) return <p className="text-sm text-zinc-500">Loading…</p>;
  if (specification.error instanceof ApiError && specification.error.status === 404) {
    return (
      <div>
        <p className="text-sm">This specification doesn't exist, or it isn't yours.</p>
        <Link className="text-sm text-indigo-600 hover:underline dark:text-indigo-400" to="/projects">Back to projects</Link>
      </div>
    );
  }
  if (specification.error) return <ErrorMessage error={specification.error} />;

  return <SpecificationWorkspace specification={specification.data} />;
}

function SpecificationWorkspace({ specification }: { specification: Specification }) {
  const project = useProject(specification.projectId);
  const document = useSpecificationDocument(specification.id);
  const [searchParams, setSearchParams] = useSearchParams();
  const view = views.find((v) => v.id === searchParams.get("view"))?.id ?? "reference";
  const [mode, setMode] = useState<"view" | "replace" | "delete">("view");

  return (
    <div>
      <nav className="text-sm text-zinc-500">
        <Link to="/projects" className="hover:underline">Projects</Link>
        <span className="mx-1.5">/</span>
        <Link to={`/projects/${specification.projectId}`} className="hover:underline">{project.data?.name ?? "Project"}</Link>
      </nav>

      <header className="mt-2 flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <h1 className="text-lg font-semibold">{specification.name}</h1>
          <p className="mt-0.5 text-sm text-zinc-500">
            {specification.title} · v{specification.version} ·{" "}
            <span className="font-mono">OpenAPI {specification.openApiVersion}</span> · updated{" "}
            {new Date(specification.updatedAt).toLocaleString()}
          </p>
        </div>
        <div className="flex gap-2">
          {/* Same-origin link, so the download attribute works and the auth cookie goes along. */}
          <a
            href={documentUrl(specification.id)}
            download={`${specification.name.replace(/[^\w.-]+/g, "-")}.json`}
            className="inline-flex h-9 items-center rounded-md border border-zinc-300 bg-white px-3.5 text-sm font-medium hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900 dark:hover:bg-zinc-800"
          >
            Export JSON
          </a>
          <Button variant="secondary" onClick={() => setMode("replace")}>Upload new version</Button>
          <Button variant="secondary" onClick={() => setMode("delete")}>Delete…</Button>
        </div>
      </header>

      {mode === "replace" && <ReplacePanel specification={specification} onDone={() => setMode("view")} />}
      {mode === "delete" && <DeletePanel specification={specification} onCancel={() => setMode("view")} />}

      <div role="tablist" className="mt-6 flex gap-1 border-b border-zinc-200 dark:border-zinc-800">
        {views.map((v) => (
          <button
            key={v.id}
            role="tab"
            aria-selected={view === v.id}
            onClick={() => setSearchParams({ view: v.id })}
            className={`-mb-px border-b-2 px-3 py-2 text-sm ${
              view === v.id
                ? "border-indigo-600 font-medium text-zinc-900 dark:text-zinc-100"
                : "border-transparent text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200"
            }`}
          >
            {v.label}
          </button>
        ))}
      </div>

      <div className="mt-5" role="tabpanel">
        <ErrorMessage error={document.error} />
        {document.isPending && <p className="text-sm text-zinc-500">Loading document…</p>}
        {document.data && <DocumentView view={view} document={document.data} projectId={specification.projectId} />}
      </div>
    </div>
  );
}

function DocumentView({ view, document, projectId }: { view: (typeof views)[number]["id"]; document: OpenApiDocument; projectId: string }) {
  const sendRequest = useServerSend(projectId);
  const operations = useMemo(() => listOperations(document), [document]);
  const schemas = useMemo(() => listSchemas(document, operations), [document, operations]);

  switch (view) {
    case "reference":
      return (
        <Suspense fallback={<p className="text-sm text-zinc-500">Loading reference…</p>}>
          <ScalarReference document={document} sendRequest={sendRequest} />
        </Suspense>
      );
    case "endpoints":
      return <EndpointExplorer document={document} operations={operations} />;
    case "schemas":
      return <SchemaViewer document={document} schemas={schemas} />;
  }
}

function ReplacePanel({ specification, onDone }: { specification: Specification; onDone: () => void }) {
  const replace = useReplaceSpecification(specification.id);
  return (
    <section className="mt-4 max-w-3xl rounded-lg border border-zinc-200 p-5 dark:border-zinc-800">
      <h2 className="mb-1 text-sm font-semibold">Upload a new version</h2>
      <p className="mb-4 text-sm text-zinc-500">The new document replaces the current one. Export it first if you want to keep a copy.</p>
      <SpecificationForm
        initialName={specification.name}
        submitLabel="Replace document"
        pending={replace.isPending}
        error={replace.error}
        onSubmit={(input) => replace.mutate(input, { onSuccess: onDone })}
        onCancel={onDone}
      />
    </section>
  );
}

function DeletePanel({ specification, onCancel }: { specification: Specification; onCancel: () => void }) {
  const deleteSpecification = useDeleteSpecification();
  const navigate = useNavigate();
  return (
    <section className="mt-4 max-w-3xl rounded-lg border border-red-200 p-4 dark:border-red-900">
      <p className="text-sm">Delete {specification.name}? This can't be undone.</p>
      <ErrorMessage error={deleteSpecification.error} />
      <div className="mt-3 flex gap-2">
        <Button
          variant="danger"
          disabled={deleteSpecification.isPending}
          onClick={() =>
            deleteSpecification.mutate(specification, { onSuccess: () => navigate(`/projects/${specification.projectId}`) })
          }
        >
          Yes, delete it
        </Button>
        <Button variant="secondary" onClick={onCancel}>Cancel</Button>
      </div>
    </section>
  );
}
