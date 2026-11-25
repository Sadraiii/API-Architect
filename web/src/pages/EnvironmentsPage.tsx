import { useState } from "react";
import { Link, useParams } from "react-router";
import type { KeyLocation } from "../api/collections";
import {
  useActiveEnvironmentId,
  useDeleteEnvironment,
  useEnvironments,
  useSaveEnvironment,
  type Environment,
  type EnvironmentInput,
} from "../api/environments";
import { useProject } from "../api/projects";
import { KeyValueEditor } from "../components/KeyValueEditor";
import { Button, ErrorMessage, TextField } from "../components/ui";

const cellStyles =
  "w-full min-w-0 rounded border border-zinc-300 bg-white px-2 py-1 font-mono text-xs focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 dark:border-zinc-700 dark:bg-zinc-900";

export function EnvironmentsPage() {
  const { id: projectId = "" } = useParams();
  const project = useProject(projectId);
  const environments = useEnvironments(projectId);
  const [activeId, setActiveId] = useActiveEnvironmentId(projectId);
  // null: nothing open; "new": creating; otherwise the id being edited.
  const [openId, setOpenId] = useState<string | null>(null);
  const open = environments.data?.find((e) => e.id === openId);

  return (
    <div className="max-w-5xl">
      <div className="text-sm">
        <Link to={`/projects/${projectId}`} className="text-zinc-500 hover:underline">{project.data?.name ?? "Project"}</Link>
        <span className="mx-1.5 text-zinc-400">/</span>
        <span className="font-semibold">Environments</span>
        <Link to={`/projects/${projectId}/collections`} className="ml-4 text-indigo-600 hover:underline dark:text-indigo-400">Collections</Link>
      </div>
      <p className="mt-2 max-w-2xl text-sm text-zinc-600 dark:text-zinc-400">
        An environment holds the base URL, variables, headers and auth for one target, like Local or Staging. Requests use
        its variables as {"{{name}}"}. Secret values are encrypted and never shown again after you save them.
      </p>

      <div className="mt-6 grid gap-6 md:grid-cols-[14rem_1fr]">
        <aside>
          <ErrorMessage error={environments.error} />
          <ul className="space-y-0.5">
            {environments.data?.map((environment) => (
              <li key={environment.id}>
                <button
                  type="button"
                  onClick={() => setOpenId(environment.id)}
                  className={`flex w-full items-center justify-between rounded-md px-2.5 py-1.5 text-left text-sm ${openId === environment.id ? "bg-zinc-200 font-medium dark:bg-zinc-800" : "hover:bg-zinc-100 dark:hover:bg-zinc-800/60"}`}
                >
                  <span className="truncate">{environment.name}</span>
                  {environment.id === activeId && <span className="text-xs text-emerald-600 dark:text-emerald-400">active</span>}
                </button>
              </li>
            ))}
          </ul>
          {environments.data?.length === 0 && <p className="text-sm text-zinc-500">No environments yet.</p>}
          <Button className="mt-3" variant="secondary" onClick={() => setOpenId("new")}>New environment</Button>
        </aside>

        <section>
          {(openId === "new" || open) && (
            <EnvironmentForm
              key={openId}
              projectId={projectId}
              environment={open}
              isActive={open !== undefined && open.id === activeId}
              onActivate={() => open && setActiveId(open.id)}
              onSaved={(saved) => setOpenId(saved.id)}
              onDeleted={() => {
                if (open?.id === activeId) setActiveId(null);
                setOpenId(null);
              }}
            />
          )}
        </section>
      </div>
    </div>
  );
}

interface VariableRow {
  name: string;
  /** For a stored secret, null until the user types a replacement. */
  value: string | null;
  isSecret: boolean;
  /** A secret the server already has. */
  stored: boolean;
}

interface FormProps {
  projectId: string;
  environment: Environment | undefined;
  isActive: boolean;
  onActivate: () => void;
  onSaved: (environment: Environment) => void;
  onDeleted: () => void;
}

function EnvironmentForm({ projectId, environment, isActive, onActivate, onSaved, onDeleted }: FormProps) {
  const save = useSaveEnvironment(projectId);
  const remove = useDeleteEnvironment(projectId);
  const [name, setName] = useState(environment?.name ?? "");
  const [baseUrl, setBaseUrl] = useState(environment?.baseUrl ?? "");
  const [variables, setVariables] = useState<VariableRow[]>(
    environment?.variables.map((v) => ({ ...v, stored: v.isSecret })) ?? [],
  );
  const [headers, setHeaders] = useState(environment?.headers ?? []);
  const [authType, setAuthType] = useState(environment?.auth.type ?? "none");
  const [authValue, setAuthValue] = useState<string | null>(null);
  const [keyName, setKeyName] = useState(environment?.auth.keyName ?? "");
  const [keyLocation, setKeyLocation] = useState<KeyLocation>(environment?.auth.keyLocation ?? "header");
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const authStored = (environment?.auth.hasValue ?? false) && environment?.auth.type !== "none";

  const updateVariable = (index: number, patch: Partial<VariableRow>) =>
    setVariables(variables.map((v, i) => (i === index ? { ...v, ...patch } : v)));

  function submit() {
    const input: EnvironmentInput = {
      name,
      baseUrl: baseUrl.trim() || null,
      // Stored secrets go back as null, which tells the server to keep them.
      variables: variables.map(({ name, value, isSecret, stored }) => ({
        name,
        isSecret,
        value: isSecret && stored ? value : (value ?? ""),
      })),
      headers,
      auth: {
        type: authType,
        value: authType === "none" ? null : authValue,
        keyName: authType === "apiKey" ? keyName : null,
        keyLocation: authType === "apiKey" ? keyLocation : null,
      },
    };
    save.mutate({ id: environment?.id ?? null, input }, { onSuccess: onSaved });
  }

  return (
    <form className="space-y-6" onSubmit={(e) => { e.preventDefault(); submit(); }}>
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField label="Name" value={name} onChange={(e) => setName(e.target.value)} required maxLength={100} placeholder="Staging" />
        <TextField
          label="Base URL"
          value={baseUrl}
          onChange={(e) => setBaseUrl(e.target.value)}
          maxLength={2000}
          placeholder="https://staging.example.com"
          hint="Request URLs that start with / are appended to it. Also available as {{baseUrl}}."
        />
      </div>

      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">Variables</legend>
        {variables.map((variable, index) => (
          <div key={index} className="flex items-center gap-2">
            <input
              aria-label="Variable name"
              className={cellStyles}
              placeholder="name"
              value={variable.name}
              maxLength={100}
              onChange={(e) => updateVariable(index, { name: e.target.value, stored: false, value: variable.value ?? "" })}
            />
            <input
              aria-label={`Value of ${variable.name || "variable"}`}
              type={variable.isSecret ? "password" : "text"}
              autoComplete="off"
              className={cellStyles}
              placeholder={variable.stored && variable.value === null ? "Stored secret. Type to replace." : "value"}
              value={variable.value ?? ""}
              maxLength={8000}
              onChange={(e) => updateVariable(index, { value: e.target.value })}
            />
            <label className="flex shrink-0 items-center gap-1 text-xs">
              <input
                type="checkbox"
                className="size-4 accent-indigo-600"
                checked={variable.isSecret}
                onChange={(e) => updateVariable(index, { isSecret: e.target.checked, stored: false, value: variable.value ?? "" })}
              />
              Secret
            </label>
            <button
              type="button"
              aria-label={`Remove ${variable.name || "variable"}`}
              className="rounded px-1.5 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800"
              onClick={() => setVariables(variables.filter((_, i) => i !== index))}
            >
              ×
            </button>
          </div>
        ))}
        <button
          type="button"
          className="text-xs font-medium text-indigo-600 hover:underline dark:text-indigo-400"
          onClick={() => setVariables([...variables, { name: "", value: "", isSecret: false, stored: false }])}
        >
          Add variable
        </button>
        <p className="text-xs text-zinc-500">Renaming a stored secret clears it, so enter its value again.</p>
      </fieldset>

      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">Headers sent with every request</legend>
        <KeyValueEditor label="Headers" rows={headers} onChange={setHeaders} namePlaceholder="Header name" />
      </fieldset>

      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">Auth for requests set to "Use the environment's auth"</legend>
        <div className="flex flex-wrap gap-2">
          <select
            aria-label="Auth type"
            className={`${cellStyles} w-auto text-sm`}
            value={authType}
            onChange={(e) => setAuthType(e.target.value as typeof authType)}
          >
            <option value="none">No auth</option>
            <option value="bearer">Bearer token</option>
            <option value="apiKey">API key</option>
          </select>
          {authType === "apiKey" && (
            <>
              <input aria-label="API key name" className={`${cellStyles} w-48`} placeholder="X-Api-Key" value={keyName} maxLength={500} onChange={(e) => setKeyName(e.target.value)} />
              <select aria-label="Send API key in" className={`${cellStyles} w-auto`} value={keyLocation} onChange={(e) => setKeyLocation(e.target.value as KeyLocation)}>
                <option value="header">Header</option>
                <option value="query">Query parameter</option>
              </select>
            </>
          )}
        </div>
        {authType !== "none" && (
          <input
            aria-label={authType === "bearer" ? "Token" : "API key value"}
            type="password"
            autoComplete="off"
            className={cellStyles}
            placeholder={authStored && authValue === null ? "Stored. Type to replace." : authType === "bearer" ? "Token" : "Key value"}
            value={authValue ?? ""}
            maxLength={8000}
            onChange={(e) => setAuthValue(e.target.value)}
          />
        )}
      </fieldset>

      <ErrorMessage error={save.error ?? remove.error} />
      <div className="flex flex-wrap gap-2 border-t border-zinc-200 pt-4 dark:border-zinc-800">
        <Button type="submit" disabled={save.isPending}>{save.isPending ? "Saving…" : environment ? "Save changes" : "Create environment"}</Button>
        {environment && !isActive && <Button type="button" variant="secondary" onClick={onActivate}>Use this environment</Button>}
        {environment &&
          (confirmingDelete ? (
            <>
              <Button type="button" variant="danger" disabled={remove.isPending} onClick={() => remove.mutate(environment.id, { onSuccess: onDeleted })}>
                Yes, delete {environment.name}
              </Button>
              <Button type="button" variant="secondary" onClick={() => setConfirmingDelete(false)}>Cancel</Button>
            </>
          ) : (
            <Button type="button" variant="secondary" onClick={() => setConfirmingDelete(true)}>Delete…</Button>
          ))}
      </div>
    </form>
  );
}
