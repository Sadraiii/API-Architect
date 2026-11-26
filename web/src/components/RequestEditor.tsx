import { useEffect, useState } from "react";
import {
  useDeleteRequest,
  useDuplicateRequest,
  useSavedRequest,
  useUpdateRequest,
  type CollectionDetail,
  type RequestAuth,
  type SavedRequest,
  type SavedRequestInput,
} from "../api/collections";
import { previewUrl, useActiveEnvironmentId, useEnvironments } from "../api/environments";
import { httpMethods, useSendSavedRequest, type HttpMethod } from "../api/execution";
import { ResponseViewer } from "./ResponseViewer";
import { folderPaths } from "./CollectionTree";
import { KeyValueTable } from "./KeyValueTable";
import { Button, ErrorMessage } from "./ui";

const tabs = ["Params", "Headers", "Body", "Auth"] as const;
type Tab = (typeof tabs)[number];

const contentTypes = ["application/json", "text/plain", "application/xml", "application/x-www-form-urlencoded"];

const inputStyles =
  "rounded-md border border-zinc-300 bg-white px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 dark:border-zinc-700 dark:bg-zinc-900";

interface Props {
  projectId: string;
  requestId: string;
  collection: CollectionDetail;
  onDuplicated: (id: string) => void;
  onDeleted: () => void;
}

/** Edits one saved request. Changes stay local until Save. */
export function RequestEditor({ projectId, requestId, collection, onDuplicated, onDeleted }: Props) {
  const saved = useSavedRequest(requestId);
  const update = useUpdateRequest(projectId);
  const duplicate = useDuplicateRequest(projectId);
  const remove = useDeleteRequest(projectId);
  const send = useSendSavedRequest(projectId);
  const environments = useEnvironments(projectId);
  const [activeEnvironmentId] = useActiveEnvironmentId(projectId);
  const [draft, setDraft] = useState<SavedRequestInput | null>(null);
  const [tab, setTab] = useState<Tab>("Params");
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  // Start from the saved copy whenever another request is opened or a save comes back.
  useEffect(() => {
    if (saved.data) {
      setDraft(toInput(saved.data));
    }
  }, [saved.data]);

  useEffect(() => {
    setConfirmingDelete(false);
    update.reset();
    send.reset();
  }, [requestId]);

  if (saved.error) return <ErrorMessage error={saved.error} />;
  if (!draft || !saved.data || saved.data.id !== requestId) return <p className="text-sm text-zinc-500">Loading…</p>;

  const set = (patch: Partial<SavedRequestInput>) => setDraft({ ...draft, ...patch });
  const setAuth = (patch: Partial<RequestAuth>) => set({ auth: { ...draft.auth, ...patch } });
  const dirty = JSON.stringify(toInput(saved.data)) !== JSON.stringify(draft);
  const environment = environments.data?.find((e) => e.id === activeEnvironmentId);
  const preview = previewUrl(draft.url, draft.queryParameters, environment);
  const counts: Partial<Record<Tab, number>> = {
    Params: draft.queryParameters.length,
    Headers: draft.headers.length,
  };

  // Send runs what is on screen: unsaved edits are saved first, because the server sends the saved request.
  const sendRequest = async () => {
    if (dirty) await update.mutateAsync({ id: requestId, input: draft });
    send.mutate({ requestId, environmentId: environment?.id ?? null });
  };

  return (
    <div className="min-w-0 space-y-6">
      <form
        className="min-w-0 space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          void sendRequest();
        }}
      >
        <div className="flex items-center gap-2">
          <input
            aria-label="Request name"
            className={`${inputStyles} min-w-0 flex-1 font-medium`}
            value={draft.name}
            maxLength={200}
            required
            onChange={(e) => set({ name: e.target.value })}
          />
          <select
            aria-label="Folder"
            className={`${inputStyles} max-w-56`}
            value={draft.folderId ?? ""}
            onChange={(e) => set({ folderId: e.target.value || null })}
          >
            <option value="">{collection.name} (top level)</option>
            {folderPaths(collection.folders).map(({ id, path }) => (
              <option key={id} value={id}>{path}</option>
            ))}
          </select>
        </div>

        <div className="flex gap-2">
          <select
            aria-label="Method"
            className={`${inputStyles} font-mono font-semibold`}
            value={draft.method}
            onChange={(e) => set({ method: e.target.value as HttpMethod })}
          >
            {httpMethods.map((m) => <option key={m}>{m}</option>)}
          </select>
          <input
            aria-label="URL"
            className={`${inputStyles} min-w-0 flex-1 font-mono`}
            placeholder="https://api.example.com/orders or /orders with an environment base URL"
            value={draft.url}
            maxLength={8000}
            required
            onChange={(e) => set({ url: e.target.value })}
          />
          <Button type="submit" disabled={send.isPending || update.isPending}>
            {send.isPending ? "Sending…" : "Send"}
          </Button>
          <Button type="button" variant="secondary" disabled={!dirty || update.isPending} onClick={() => update.mutate({ id: requestId, input: draft })}>
            {update.isPending ? "Saving…" : dirty ? "Save" : "Saved"}
          </Button>
        </div>
        {preview && preview !== draft.url && (
          <p className="truncate font-mono text-xs text-zinc-500" title={preview}>
            {environment ? `${environment.name}: ` : ""}{preview}
          </p>
        )}
        <ErrorMessage error={update.error ?? send.error} />

        <div role="tablist" className="flex gap-4 border-b border-zinc-200 text-sm dark:border-zinc-800">
          {tabs.map((t) => (
            <button
              key={t}
              type="button"
              role="tab"
              aria-selected={tab === t}
              onClick={() => setTab(t)}
              className={`-mb-px border-b-2 pb-2 ${tab === t ? "border-indigo-600 font-medium" : "border-transparent text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200"}`}
            >
              {t}
              {counts[t] ? <span className="ml-1 text-xs text-zinc-400">{counts[t]}</span> : null}
            </button>
          ))}
        </div>

        <div role="tabpanel">
          {tab === "Params" && (
            <KeyValueTable label="Query parameters" rows={draft.queryParameters} onChange={(queryParameters) => set({ queryParameters })} />
          )}
          {tab === "Headers" && (
            <KeyValueTable label="Headers" rows={draft.headers} onChange={(headers) => set({ headers })} namePlaceholder="Header name" />
          )}
          {tab === "Body" && <BodyEditor draft={draft} set={set} />}
          {tab === "Auth" && <AuthEditor auth={draft.auth} setAuth={setAuth} />}
        </div>

        <div className="flex flex-wrap items-center gap-2 border-t border-zinc-200 pt-4 dark:border-zinc-800">
          <Button type="button" variant="secondary" disabled={duplicate.isPending} onClick={() => duplicate.mutate(requestId, { onSuccess: (copy) => onDuplicated(copy.id) })}>
            Duplicate
          </Button>
          {confirmingDelete ? (
            <>
              <Button type="button" variant="danger" disabled={remove.isPending} onClick={() => remove.mutate(requestId, { onSuccess: onDeleted })}>
                Yes, delete
              </Button>
              <Button type="button" variant="secondary" onClick={() => setConfirmingDelete(false)}>Cancel</Button>
            </>
          ) : (
            <Button type="button" variant="secondary" onClick={() => setConfirmingDelete(true)}>Delete…</Button>
          )}
          <ErrorMessage error={duplicate.error ?? remove.error} />
        </div>
      </form>
      {send.data && <ResponseViewer key={send.data.id} execution={send.data} fromHistory={false} />}
    </div>
  );
}

function toInput(request: SavedRequest): SavedRequestInput {
  const { name, method, url, folderId, headers, queryParameters, body, contentType, auth } = request;
  return { name, method, url, folderId, headers, queryParameters, body, contentType, auth };
}

function BodyEditor({ draft, set }: { draft: SavedRequestInput; set: (patch: Partial<SavedRequestInput>) => void }) {
  const [formatError, setFormatError] = useState<string | null>(null);
  const isJson = draft.contentType?.startsWith("application/json") ?? false;
  const known = draft.contentType === null || contentTypes.includes(draft.contentType);

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        <select
          aria-label="Content type"
          className={inputStyles}
          value={known ? (draft.contentType ?? "") : "custom"}
          onChange={(e) => set({ contentType: e.target.value === "custom" ? "application/" : e.target.value || null })}
        >
          <option value="">No content type</option>
          {contentTypes.map((type) => <option key={type}>{type}</option>)}
          <option value="custom">Other…</option>
        </select>
        {!known && (
          <input
            aria-label="Custom content type"
            className={`${inputStyles} font-mono`}
            value={draft.contentType ?? ""}
            maxLength={200}
            onChange={(e) => set({ contentType: e.target.value || null })}
          />
        )}
        {isJson && (
          <Button
            type="button"
            variant="secondary"
            onClick={() => {
              try {
                set({ body: JSON.stringify(JSON.parse(draft.body ?? ""), null, 2) });
                setFormatError(null);
              } catch {
                setFormatError("The body isn't valid JSON yet. {{variables}} outside of strings also stop it parsing.");
              }
            }}
          >
            Format JSON
          </Button>
        )}
      </div>
      <textarea
        aria-label="Body"
        spellCheck={false}
        className={`${inputStyles} min-h-64 w-full font-mono text-xs`}
        placeholder={draft.method === "GET" ? "GET requests usually have no body." : "Request body. {{variables}} are filled in when it runs."}
        value={draft.body ?? ""}
        onChange={(e) => set({ body: e.target.value || null })}
      />
      {formatError && <p className="text-xs text-red-600 dark:text-red-400">{formatError}</p>}
    </div>
  );
}

function AuthEditor({ auth, setAuth }: { auth: RequestAuth; setAuth: (patch: Partial<RequestAuth>) => void }) {
  return (
    <div className="max-w-xl space-y-3 text-sm">
      <select
        aria-label="Auth type"
        className={inputStyles}
        value={auth.type}
        onChange={(e) => {
          const type = e.target.value as RequestAuth["type"];
          setAuth({ type, keyLocation: type === "apiKey" ? (auth.keyLocation ?? "header") : null });
        }}
      >
        <option value="inherit">Use the environment's auth</option>
        <option value="none">No auth</option>
        <option value="bearer">Bearer token</option>
        <option value="apiKey">API key</option>
      </select>

      {auth.type === "apiKey" && (
        <div className="flex gap-2">
          <input
            aria-label="API key name"
            className={`${inputStyles} flex-1 font-mono`}
            placeholder="X-Api-Key"
            value={auth.keyName ?? ""}
            maxLength={500}
            onChange={(e) => setAuth({ keyName: e.target.value })}
          />
          <select aria-label="Send API key in" className={inputStyles} value={auth.keyLocation ?? "header"} onChange={(e) => setAuth({ keyLocation: e.target.value as "header" | "query" })}>
            <option value="header">Header</option>
            <option value="query">Query parameter</option>
          </select>
        </div>
      )}
      {(auth.type === "bearer" || auth.type === "apiKey") && (
        <>
          <input
            aria-label={auth.type === "bearer" ? "Token" : "API key value"}
            className={`${inputStyles} w-full font-mono`}
            placeholder="{{token}}"
            value={auth.value ?? ""}
            maxLength={8000}
            onChange={(e) => setAuth({ value: e.target.value })}
          />
          <p className="text-xs text-zinc-500">
            This is stored as plain text and included in exports. For a real credential, add it to an environment as a
            secret variable and enter its name here, like {"{{token}}"}.
          </p>
        </>
      )}
      {auth.type === "inherit" && (
        <p className="text-xs text-zinc-500">The selected environment's auth is used, if it has any.</p>
      )}
    </div>
  );
}
