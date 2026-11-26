import { useMemo, useState } from "react";
import { useSearchParams } from "react-router";
import { projectNamePattern, useGenerateClientCode, type ClientCode, type ClientTemplate } from "../api/clientCode";
import { useProjects } from "../api/projects";
import { useProjectSpecifications, useSpecification, useSpecificationDocument } from "../api/specifications";
import { CodeView } from "../components/CodeView";
import { Button, ErrorMessage, Select, TextField } from "../components/ui";
import { listOperations, type Operation } from "../openapi/model";

const templates: { id: ClientTemplate; label: string; vsTemplate: string; text: string }[] = [
  {
    id: "console",
    label: "Connector",
    vsTemplate: "Console App",
    text: "Runs once: calls the API and prints what comes back. No NuGet packages needed.",
  },
  {
    id: "worker",
    label: "Worker",
    vsTemplate: "Worker Service",
    text: "Keeps running and calls the API on a schedule, for syncing or polling.",
  },
];

/**
 * Generates plain C# that calls one of your APIs, to paste into a new Visual Studio project, with the steps to
 * set that project up. Pick a specification, the operations, and a template.
 */
export function ClientCodePage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const specificationId = searchParams.get("specification") ?? "";
  const specification = useSpecification(specificationId);
  const [projectId, setProjectId] = useState("");
  const selectedProjectId = projectId || specification.data?.projectId || "";

  return (
    <div className="max-w-5xl">
      <header className="mb-6">
        <h1 className="text-lg font-semibold">Client code</h1>
        <p className="mt-1 text-sm text-zinc-500">
          Get C# that calls one of your APIs, ready to paste into a new Visual Studio project. Pick an imported
          specification, the operations you need, and the kind of app.
        </p>
      </header>

      <SourcePicker
        projectId={selectedProjectId}
        specificationId={specificationId}
        onProject={(id) => {
          setProjectId(id);
          setSearchParams({});
        }}
        onSpecification={(id) => setSearchParams(id ? { specification: id } : {})}
      />

      {specificationId && <Generator key={specificationId} specificationId={specificationId} />}
    </div>
  );
}

function SourcePicker({
  projectId,
  specificationId,
  onProject,
  onSpecification,
}: {
  projectId: string;
  specificationId: string;
  onProject: (id: string) => void;
  onSpecification: (id: string) => void;
}) {
  const projects = useProjects();
  const specifications = useProjectSpecifications(projectId);

  if (projects.data?.length === 0) {
    return <p className="text-sm text-zinc-500">Create a project and import a specification first.</p>;
  }

  return (
    <section className="grid gap-4 sm:grid-cols-2">
      <ErrorMessage error={projects.error} />
      <Select label="Project" value={projectId} onChange={(e) => onProject(e.target.value)}>
        <option value="">Choose a project…</option>
        {projects.data?.map((p) => (
          <option key={p.id} value={p.id}>{p.name}</option>
        ))}
      </Select>
      <Select
        label="Specification"
        value={specificationId}
        disabled={!projectId}
        onChange={(e) => onSpecification(e.target.value)}
      >
        <option value="">
          {projectId && specifications.data?.length === 0 ? "This project has no specifications" : "Choose a specification…"}
        </option>
        {specifications.data?.map((s) => (
          <option key={s.id} value={s.id}>{s.name} ({s.title} v{s.version})</option>
        ))}
      </Select>
    </section>
  );
}

/** "Pet Store API" becomes "PetStore". */
function suggestedName(specificationName: string | undefined) {
  const words = (specificationName ?? "").replace(/\bapi\b/gi, " ").match(/[A-Za-z0-9]+/g) ?? [];
  const name = words.map((w) => w.charAt(0).toUpperCase() + w.slice(1)).join("").replace(/^\d+/, "");
  return name || "My";
}

function Generator({ specificationId }: { specificationId: string }) {
  const specification = useSpecification(specificationId);
  const document = useSpecificationDocument(specificationId);
  const generate = useGenerateClientCode(specificationId);
  const operations = useMemo(() => (document.data ? listOperations(document.data) : []), [document.data]);
  const [template, setTemplate] = useState<ClientTemplate>("console");
  const [projectName, setProjectName] = useState("");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [filter, setFilter] = useState("");

  const defaultName = suggestedName(specification.data?.name) + (template === "console" ? "Connector" : "Worker");
  const name = projectName || defaultName;
  const nameValid = projectNamePattern.test(name);
  const visible = operations.filter((o) =>
    `${o.method} ${o.path} ${o.summary ?? ""} ${o.operationId ?? ""}`.toLowerCase().includes(filter.toLowerCase()),
  );

  const toggle = (keys: string[], on: boolean) =>
    setSelected((current) => {
      const next = new Set(current);
      keys.forEach((k) => (on ? next.add(k) : next.delete(k)));
      return next;
    });

  if (document.isPending) return <p className="mt-6 text-sm text-zinc-500">Loading specification…</p>;
  if (document.error) return <div className="mt-6"><ErrorMessage error={document.error} /></div>;

  return (
    <div className="mt-8 space-y-8">
      <section>
        <h2 className="mb-3 text-sm font-semibold">Kind of app</h2>
        <div className="grid gap-3 sm:grid-cols-2" role="radiogroup">
          {templates.map((t) => (
            <button
              key={t.id}
              role="radio"
              aria-checked={template === t.id}
              onClick={() => setTemplate(t.id)}
              className={`rounded-lg border p-4 text-left ${
                template === t.id
                  ? "border-indigo-600 ring-1 ring-indigo-600"
                  : "border-zinc-200 hover:border-zinc-400 dark:border-zinc-800 dark:hover:border-zinc-600"
              }`}
            >
              <p className="text-sm font-medium">
                {t.label} <span className="font-normal text-zinc-500">· Visual Studio template: {t.vsTemplate}</span>
              </p>
              <p className="mt-1 text-sm text-zinc-500">{t.text}</p>
            </button>
          ))}
        </div>
        <div className="mt-4 max-w-sm">
          <TextField
            label="Project name"
            value={name}
            onChange={(e) => setProjectName(e.target.value)}
            hint={nameValid ? "Use the same name when you create the project in Visual Studio. It's also the namespace." : "Letters, digits and underscores, with dots between parts. It can't start with a digit."}
          />
        </div>
      </section>

      <section>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-semibold">
            Operations <span className="font-normal text-zinc-500">({selected.size} of {operations.length} chosen)</span>
          </h2>
          <div className="flex items-center gap-2">
            <input
              aria-label="Filter operations"
              placeholder="Filter…"
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              className="h-9 w-48 rounded-md border border-zinc-300 bg-white px-3 text-sm dark:border-zinc-700 dark:bg-zinc-900"
            />
            <Button variant="secondary" onClick={() => toggle(visible.map((o) => o.key), true)}>Select all</Button>
            <Button variant="secondary" onClick={() => toggle(visible.map((o) => o.key), false)}>Clear</Button>
          </div>
        </div>
        <OperationList operations={visible} selected={selected} onToggle={toggle} />
      </section>

      <div className="flex items-center gap-3">
        <Button
          disabled={selected.size === 0 || !nameValid || generate.isPending}
          onClick={() => generate.mutate({ template, projectName: name, operations: [...selected] })}
        >
          {generate.isPending ? "Generating…" : "Generate code"}
        </Button>
        {selected.size === 0 && <span className="text-sm text-zinc-500">Choose at least one operation.</span>}
      </div>
      <ErrorMessage error={generate.error} />

      {generate.data && <Result code={generate.data} />}
    </div>
  );
}

const methodColors: Record<string, string> = {
  get: "text-emerald-700 dark:text-emerald-400",
  post: "text-sky-700 dark:text-sky-400",
  put: "text-amber-700 dark:text-amber-400",
  patch: "text-purple-700 dark:text-purple-400",
  delete: "text-red-700 dark:text-red-400",
};

function OperationList({
  operations,
  selected,
  onToggle,
}: {
  operations: Operation[];
  selected: Set<string>;
  onToggle: (keys: string[], on: boolean) => void;
}) {
  if (operations.length === 0) return <p className="text-sm text-zinc-500">No operations match.</p>;

  return (
    <ul className="max-h-96 divide-y divide-zinc-200 overflow-auto rounded-lg border border-zinc-200 dark:divide-zinc-800 dark:border-zinc-800">
      {operations.map((o) => (
        <li key={o.key}>
          <label className="flex cursor-pointer items-baseline gap-3 px-3 py-2 hover:bg-zinc-50 dark:hover:bg-zinc-900">
            <input
              type="checkbox"
              className="translate-y-0.5"
              checked={selected.has(o.key)}
              onChange={(e) => onToggle([o.key], e.target.checked)}
            />
            <span className={`w-14 shrink-0 font-mono text-xs font-semibold uppercase ${methodColors[o.method] ?? "text-zinc-500"}`}>
              {o.method}
            </span>
            <span className="min-w-0 truncate font-mono text-sm">{o.path}</span>
            {o.summary && <span className="min-w-0 truncate text-sm text-zinc-500">{o.summary}</span>}
          </label>
        </li>
      ))}
    </ul>
  );
}

/** Renders `code` spans in a step. */
function StepText({ text }: { text: string }) {
  return (
    <>
      {text.split(/(`[^`]+`)/).map((part, i) =>
        part.startsWith("`") && part.endsWith("`") ? (
          <code key={i} className="rounded bg-zinc-100 px-1 py-0.5 font-mono text-[0.8rem] dark:bg-zinc-800">{part.slice(1, -1)}</code>
        ) : (
          part
        ),
      )}
    </>
  );
}

function Result({ code }: { code: ClientCode }) {
  return (
    <div className="space-y-8 border-t border-zinc-200 pt-8 dark:border-zinc-800">
      <section>
        <h2 className="mb-3 text-sm font-semibold">Set it up in Visual Studio</h2>
        <ol className="list-decimal space-y-2 pl-5 text-sm">
          {code.steps.map((step) => (
            <li key={step}><StepText text={step} /></li>
          ))}
        </ol>
      </section>

      {code.notes.length > 0 && (
        <section className="rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <p className="font-medium">Good to know</p>
          <ul className="mt-1 list-disc space-y-1 pl-5">
            {code.notes.map((note) => <li key={note}>{note}</li>)}
          </ul>
        </section>
      )}

      {code.files.map((file) => <FileBlock key={file.fileName} fileName={file.fileName} where={file.where} content={file.content} />)}
    </div>
  );
}

function FileBlock({ fileName, where, content }: { fileName: string; where: string; content: string }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    await navigator.clipboard.writeText(content);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  };

  return (
    <section>
      <div className="mb-2 flex items-end justify-between gap-4">
        <div>
          <h3 className="font-mono text-sm font-semibold">{fileName}</h3>
          <p className="text-sm text-zinc-500">{where}</p>
        </div>
        <Button variant="secondary" onClick={copy}>{copied ? "Copied" : "Copy"}</Button>
      </div>
      <CodeView text={content} />
    </section>
  );
}
