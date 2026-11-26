import { lazy, Suspense, useEffect, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { ApiError } from "../api/client";
import {
  openApiUrl,
  useDeleteDesign,
  useDesign,
  useDesignCode,
  useDesignOpenApi,
  useSaveAsSpecification,
  useSaveDesign,
  zipUrl,
  type Design,
  type DesignDocument,
} from "../api/designs";
import { useProject } from "../api/projects";
import { LabeledField, SmallInput } from "../components/design/controls";
import { EndpointsEditor } from "../components/design/EndpointsEditor";
import { ModelsEditor } from "../components/design/ModelsEditor";
import { Button, ErrorMessage } from "../components/ui";

const ScalarReference = lazy(() => import("../components/ScalarReference"));

const tabs = [
  { id: "overview", label: "Overview" },
  { id: "models", label: "Models" },
  { id: "endpoints", label: "Endpoints" },
  { id: "preview", label: "API reference" },
  { id: "code", label: "Generated code" },
] as const;

type TabId = (typeof tabs)[number]["id"];

export function DesignPage() {
  const { id = "" } = useParams();
  const design = useDesign(id);

  if (design.isPending) return <p className="text-sm text-zinc-500">Loading…</p>;
  if (design.error instanceof ApiError && design.error.status === 404) {
    return (
      <div>
        <p className="text-sm">This design doesn't exist, or it isn't yours.</p>
        <Link className="text-sm text-indigo-600 hover:underline dark:text-indigo-400" to="/projects">Back to projects</Link>
      </div>
    );
  }
  if (design.error) return <ErrorMessage error={design.error} />;

  return <DesignWorkspace key={design.data.id} design={design.data} />;
}

function DesignWorkspace({ design }: { design: Design }) {
  const project = useProject(design.projectId);
  const save = useSaveDesign(design.id);
  const saveAsSpecification = useSaveAsSpecification(design);
  const deleteDesign = useDeleteDesign();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const tab: TabId = tabs.find((t) => t.id === searchParams.get("tab"))?.id ?? "overview";

  // The editors change a local draft; nothing reaches the server until Save.
  const [name, setName] = useState(design.name);
  const [draft, setDraft] = useState<DesignDocument>(design.document);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const dirty = name !== design.name || JSON.stringify(draft) !== JSON.stringify(design.document);

  // Warn before a reload or tab close loses unsaved changes. (In-app navigation warnings need React Router's
  // data router; the app uses <BrowserRouter>, so the header shows "unsaved changes" instead.)
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  const onSave = () => save.mutate({ name, document: draft });

  return (
    <div className="max-w-6xl">
      <nav className="text-sm text-zinc-500">
        <Link to="/projects" className="hover:underline">Projects</Link>
        <span className="mx-1.5">/</span>
        <Link to={`/projects/${design.projectId}`} className="hover:underline">{project.data?.name ?? "Project"}</Link>
      </nav>

      <header className="mt-2 flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <h1 className="text-lg font-semibold">{design.name}</h1>
          <p className="mt-0.5 text-sm text-zinc-500">
            API design · {draft.models.length} models · {draft.endpoints.length} endpoints ·{" "}
            {dirty ? <span className="text-amber-600 dark:text-amber-400">unsaved changes</span> : `saved ${new Date(design.updatedAt).toLocaleString()}`}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button onClick={onSave} disabled={!dirty || save.isPending}>{save.isPending ? "Saving…" : "Save"}</Button>
          <Button
            variant="secondary"
            disabled={dirty || saveAsSpecification.isPending}
            title={dirty ? "Save first" : "Adds the OpenAPI document to the project's specifications, or updates the one saved before"}
            onClick={() => saveAsSpecification.mutate()}
          >
            {design.specificationId ? "Update specification" : "Save as specification"}
          </Button>
          <a
            href={dirty ? undefined : zipUrl(design.id)}
            aria-disabled={dirty}
            title={dirty ? "Save first" : "Download the generated ASP.NET Core project"}
            className={`inline-flex h-9 items-center rounded-md border border-zinc-300 bg-white px-3.5 text-sm font-medium dark:border-zinc-700 dark:bg-zinc-900 ${
              dirty ? "cursor-not-allowed opacity-50" : "hover:bg-zinc-50 dark:hover:bg-zinc-800"
            }`}
          >
            Download .zip
          </a>
          <Button variant="secondary" onClick={() => setConfirmingDelete(true)}>Delete…</Button>
        </div>
      </header>

      <div className="mt-3 space-y-2">
        <ErrorMessage error={save.error} />
        <ErrorMessage error={saveAsSpecification.error} />
        {saveAsSpecification.isSuccess && design.specificationId && (
          <p className="text-sm text-emerald-700 dark:text-emerald-400">
            Saved. <Link className="underline" to={`/specifications/${design.specificationId}`}>Open the specification</Link> to browse it with the endpoint and schema explorers.
          </p>
        )}
        {confirmingDelete && (
          <div className="rounded-lg border border-red-200 p-4 dark:border-red-900">
            <p className="text-sm">Delete {design.name}? A specification saved from it is kept. This can't be undone.</p>
            <ErrorMessage error={deleteDesign.error} />
            <div className="mt-3 flex gap-2">
              <Button variant="danger" disabled={deleteDesign.isPending}
                onClick={() => deleteDesign.mutate(design, { onSuccess: () => navigate(`/projects/${design.projectId}`) })}>
                Yes, delete it
              </Button>
              <Button variant="secondary" onClick={() => setConfirmingDelete(false)}>Cancel</Button>
            </div>
          </div>
        )}
      </div>

      <div role="tablist" className="mt-5 flex gap-1 border-b border-zinc-200 dark:border-zinc-800">
        {tabs.map((t) => (
          <button
            key={t.id}
            role="tab"
            aria-selected={tab === t.id}
            onClick={() => setSearchParams({ tab: t.id })}
            className={`-mb-px border-b-2 px-3 py-2 text-sm ${
              tab === t.id
                ? "border-indigo-600 font-medium text-zinc-900 dark:text-zinc-100"
                : "border-transparent text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200"
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div className="mt-5" role="tabpanel">
        {(tab === "preview" || tab === "code") && dirty && (
          <p className="mb-4 rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200">
            This shows the last saved version. Save to see your changes here.
          </p>
        )}
        {tab === "overview" && <Overview name={name} onNameChange={setName} document={draft} onChange={setDraft} />}
        {tab === "models" && <ModelsEditor document={draft} onChange={setDraft} />}
        {tab === "endpoints" && <EndpointsEditor document={draft} onChange={setDraft} />}
        {tab === "preview" && <Preview design={design} />}
        {tab === "code" && <Code design={design} />}
      </div>
    </div>
  );
}

function Overview({ name, onNameChange, document, onChange }: {
  name: string;
  onNameChange: (name: string) => void;
  document: DesignDocument;
  onChange: (document: DesignDocument) => void;
}) {
  return (
    <div className="max-w-2xl space-y-4">
      <div className="grid grid-cols-2 gap-3">
        <LabeledField label="Design name (in this app)">
          <SmallInput value={name} maxLength={100} onChange={(e) => onNameChange(e.target.value)} />
        </LabeledField>
        <LabeledField label="Namespace and project name">
          <SmallInput className="font-mono" value={document.namespace} onChange={(e) => onChange({ ...document, namespace: e.target.value })} />
        </LabeledField>
        <LabeledField label="API title">
          <SmallInput value={document.title} onChange={(e) => onChange({ ...document, title: e.target.value })} />
        </LabeledField>
        <LabeledField label="API version">
          <SmallInput className="font-mono" value={document.version} onChange={(e) => onChange({ ...document, version: e.target.value })} />
        </LabeledField>
      </div>
      <LabeledField label="Description">
        <textarea
          className="min-h-24 w-full rounded-md border border-zinc-300 bg-white px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 dark:border-zinc-700 dark:bg-zinc-900"
          value={document.description ?? ""}
          onChange={(e) => onChange({ ...document, description: e.target.value || null })}
        />
      </LabeledField>
      <div className="rounded-lg bg-zinc-50 p-4 text-sm text-zinc-600 dark:bg-zinc-900 dark:text-zinc-400">
        <p className="font-medium text-zinc-800 dark:text-zinc-200">How this works</p>
        <ol className="mt-2 list-decimal space-y-1 pl-5">
          <li>Add the <b>models</b> your API sends and receives, with their validation rules.</li>
          <li>Add <b>endpoints</b>: method, route, parameters, request and response models, status codes and auth.</li>
          <li>Save, then check the <b>API reference</b> and the <b>generated code</b>.</li>
          <li><b>Download .zip</b> for a .NET 11 minimal API project that builds and runs. Each handler is a stub that returns 501 until you write it.</li>
        </ol>
      </div>
    </div>
  );
}

function Preview({ design }: { design: Design }) {
  const openApi = useDesignOpenApi(design);
  return (
    <div>
      <p className="mb-3 text-sm text-zinc-500">
        The OpenAPI 3.1 document for this design.{" "}
        <a className="text-indigo-600 hover:underline dark:text-indigo-400" href={openApiUrl(design.id)} target="_blank" rel="noreferrer">Open the JSON</a>
      </p>
      <ErrorMessage error={openApi.error} />
      {openApi.isPending && <p className="text-sm text-zinc-500">Loading…</p>}
      {openApi.data && (
        <Suspense fallback={<p className="text-sm text-zinc-500">Loading reference…</p>}>
          <ScalarReference document={openApi.data} />
        </Suspense>
      )}
    </div>
  );
}

function Code({ design }: { design: Design }) {
  const code = useDesignCode(design);
  const [selected, setSelected] = useState<string | null>(null);
  if (code.error) return <ErrorMessage error={code.error} />;
  if (code.isPending) return <p className="text-sm text-zinc-500">Generating…</p>;

  const files = code.data;
  const file = files.find((f) => f.path === selected) ?? files.find((f) => f.path.endsWith("Endpoints.cs")) ?? files[0];
  const root = files[0]?.path.split("/")[0] ?? "";

  return (
    <div className="grid grid-cols-[16rem_1fr] gap-4">
      <ul className="space-y-0.5 text-sm">
        {files.map((f) => {
          const relative = f.path.slice(root.length + 1);
          const depth = relative.split("/").length - 1;
          return (
            <li key={f.path}>
              <button
                type="button"
                onClick={() => setSelected(f.path)}
                style={{ paddingLeft: `${0.5 + depth * 0.75}rem` }}
                className={`w-full truncate rounded-md py-1 pr-2 text-left font-mono text-xs ${
                  f === file ? "bg-indigo-50 text-indigo-900 dark:bg-indigo-950 dark:text-indigo-100" : "hover:bg-zinc-100 dark:hover:bg-zinc-800/60"
                }`}
              >
                {relative}
              </button>
            </li>
          );
        })}
      </ul>
      {file && (
        <div className="min-w-0 overflow-hidden rounded-lg border border-zinc-200 dark:border-zinc-800">
          <p className="border-b border-zinc-200 bg-zinc-50 px-3 py-1.5 font-mono text-xs text-zinc-600 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-400">{file.path}</p>
          <pre className="max-h-[70vh] overflow-auto p-3 font-mono text-xs leading-relaxed">{file.content}</pre>
        </div>
      )}
    </div>
  );
}
