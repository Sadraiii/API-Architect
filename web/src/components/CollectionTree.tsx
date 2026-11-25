import { useState, type ReactNode } from "react";
import {
  collectionExportUrl,
  emptyRequest,
  useCollection,
  useCreateFolder,
  useCreateRequest,
  useDeleteCollection,
  useDeleteFolder,
  useUpdateCollection,
  useUpdateFolder,
  type CollectionSummary,
  type Folder,
  type RequestSummary,
} from "../api/collections";
import { ErrorMessage } from "./ui";

/** Every folder with its full path ("Orders / Lines"), in tree order, for pickers. */
export function folderPaths(folders: Folder[], parentId: string | null = null, prefix = ""): { id: string; path: string }[] {
  return folders
    .filter((f) => f.parentFolderId === parentId)
    .flatMap((f) => {
      const path = prefix ? `${prefix} / ${f.name}` : f.name;
      return [{ id: f.id, path }, ...folderPaths(folders, f.id, path)];
    });
}

const methodColors: Record<string, string> = {
  GET: "text-emerald-600 dark:text-emerald-400",
  POST: "text-amber-600 dark:text-amber-400",
  PUT: "text-sky-600 dark:text-sky-400",
  PATCH: "text-violet-600 dark:text-violet-400",
  DELETE: "text-red-600 dark:text-red-400",
};

export function MethodBadge({ method }: { method: string }) {
  return (
    <span className={`w-12 shrink-0 font-mono text-[10px] font-semibold ${methodColors[method] ?? "text-zinc-500"}`}>
      {method === "DELETE" ? "DEL" : method === "OPTIONS" ? "OPT" : method}
    </span>
  );
}

const rowStyles = "group flex items-center gap-1 rounded px-1.5 py-1 text-sm";
const actionStyles =
  "rounded px-1 text-xs text-zinc-500 opacity-0 hover:bg-zinc-200 hover:text-zinc-900 focus:opacity-100 group-hover:opacity-100 group-focus-within:opacity-100 dark:hover:bg-zinc-700 dark:hover:text-zinc-100";

interface TreeProps {
  projectId: string;
  summary: CollectionSummary;
  selectedRequestId: string | null;
  onSelect: (requestId: string) => void;
}

type Editing =
  | { kind: "collection" }
  | { kind: "newFolder"; parentId: string | null }
  | { kind: "folder"; folder: Folder }
  | { kind: "deleteCollection" }
  | { kind: "deleteFolder"; folder: Folder };

/** One collection in the sidebar, with its folders and requests. Actions appear on hover or keyboard focus. */
export function CollectionTree({ projectId, summary, selectedRequestId, onSelect }: TreeProps) {
  const [open, setOpen] = useState(true);
  const [editing, setEditing] = useState<Editing | null>(null);
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const collection = useCollection(summary.id);
  const createRequest = useCreateRequest(projectId);
  const createFolder = useCreateFolder(projectId);
  const updateFolder = useUpdateFolder(projectId);
  const deleteFolder = useDeleteFolder(projectId);
  const updateCollection = useUpdateCollection(projectId);
  const deleteCollection = useDeleteCollection(projectId);
  const error = createRequest.error ?? createFolder.error ?? updateFolder.error ?? deleteFolder.error ?? updateCollection.error ?? deleteCollection.error;

  const folders = collection.data?.folders ?? [];
  const requests = collection.data?.requests ?? [];
  const done = () => setEditing(null);

  const addRequest = (folderId: string | null) =>
    createRequest.mutate({ collectionId: summary.id, input: emptyRequest(folderId) }, { onSuccess: (r) => onSelect(r.id) });

  const toggle = (id: string) =>
    setCollapsed((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  function renderLevel(parentId: string | null, depth: number): ReactNode {
    const indent = { paddingLeft: `${depth * 12 + 6}px` };
    return (
      <>
        {folders.filter((f) => f.parentFolderId === parentId).map((folder) => (
          <li key={folder.id}>
            {editing?.kind === "folder" && editing.folder.id === folder.id ? (
              <FolderForm
                folders={folders}
                initial={folder}
                onCancel={done}
                onSave={(name, parentFolderId) => updateFolder.mutate({ id: folder.id, name, parentFolderId }, { onSuccess: done })}
              />
            ) : editing?.kind === "deleteFolder" && editing.folder.id === folder.id ? (
              <Confirm
                message={`Delete "${folder.name}" and everything in it?`}
                onConfirm={() => deleteFolder.mutate(folder.id, { onSuccess: done })}
                onCancel={done}
              />
            ) : (
              <div className={rowStyles} style={indent}>
                <button type="button" className="flex min-w-0 flex-1 items-center gap-1 text-left" onClick={() => toggle(folder.id)} aria-expanded={!collapsed.has(folder.id)}>
                  <span className="w-3 text-xs text-zinc-400">{collapsed.has(folder.id) ? "▸" : "▾"}</span>
                  <span className="truncate">{folder.name}</span>
                </button>
                <button type="button" className={actionStyles} title="New request in this folder" onClick={() => addRequest(folder.id)}>+ req</button>
                <button type="button" className={actionStyles} title="New subfolder" onClick={() => setEditing({ kind: "newFolder", parentId: folder.id })}>+ dir</button>
                <button type="button" className={actionStyles} title="Rename or move" onClick={() => setEditing({ kind: "folder", folder })}>Edit</button>
                <button type="button" className={actionStyles} title="Delete folder" onClick={() => setEditing({ kind: "deleteFolder", folder })}>Del</button>
              </div>
            )}
            {editing?.kind === "newFolder" && editing.parentId === folder.id && (
              <FolderForm
                folders={folders}
                initial={{ id: "", name: "", parentFolderId: folder.id }}
                onCancel={done}
                onSave={(name, parentFolderId) => createFolder.mutate({ collectionId: summary.id, name, parentFolderId }, { onSuccess: done })}
              />
            )}
            {!collapsed.has(folder.id) && <ul>{renderLevel(folder.id, depth + 1)}</ul>}
          </li>
        ))}
        {requests.filter((r) => r.folderId === parentId).map((request) => (
          <RequestRow key={request.id} request={request} depth={depth} selected={request.id === selectedRequestId} onSelect={onSelect} />
        ))}
      </>
    );
  }

  return (
    <section className="py-1">
      {editing?.kind === "collection" ? (
        <NameForm
          initial={summary.name}
          onCancel={done}
          onSave={(name) => updateCollection.mutate({ id: summary.id, name, description: summary.description }, { onSuccess: done })}
        />
      ) : editing?.kind === "deleteCollection" ? (
        <Confirm
          message={`Delete "${summary.name}" with all ${summary.requestCount} requests?`}
          onConfirm={() => deleteCollection.mutate(summary.id, { onSuccess: done })}
          onCancel={done}
        />
      ) : (
        <div className={`${rowStyles} font-medium`}>
          <button type="button" className="flex min-w-0 flex-1 items-center gap-1 text-left" onClick={() => setOpen(!open)} aria-expanded={open}>
            <span className="w-3 text-xs text-zinc-400">{open ? "▾" : "▸"}</span>
            <span className="truncate">{summary.name}</span>
          </button>
          <button type="button" className={actionStyles} title="New request" onClick={() => addRequest(null)}>+ req</button>
          <button type="button" className={actionStyles} title="New folder" onClick={() => setEditing({ kind: "newFolder", parentId: null })}>+ dir</button>
          <button type="button" className={actionStyles} title="Rename" onClick={() => setEditing({ kind: "collection" })}>Edit</button>
          <a className={actionStyles} title="Download as a Postman collection" href={collectionExportUrl(summary.id)} download>Export</a>
          <button type="button" className={actionStyles} title="Delete collection" onClick={() => setEditing({ kind: "deleteCollection" })}>Del</button>
        </div>
      )}
      <ErrorMessage error={error ?? collection.error} />
      {open && (
        <ul>
          {editing?.kind === "newFolder" && editing.parentId === null && (
            <li>
              <FolderForm
                folders={folders}
                initial={{ id: "", name: "", parentFolderId: null }}
                onCancel={done}
                onSave={(name, parentFolderId) => createFolder.mutate({ collectionId: summary.id, name, parentFolderId }, { onSuccess: done })}
              />
            </li>
          )}
          {renderLevel(null, 1)}
          {collection.data && folders.length === 0 && requests.length === 0 && (
            <li className="px-6 py-1 text-xs text-zinc-500">Empty. Hover the collection to add a request.</li>
          )}
        </ul>
      )}
    </section>
  );
}

function RequestRow({ request, depth, selected, onSelect }: { request: RequestSummary; depth: number; selected: boolean; onSelect: (id: string) => void }) {
  return (
    <li>
      <button
        type="button"
        onClick={() => onSelect(request.id)}
        aria-current={selected ? "true" : undefined}
        className={`${rowStyles} w-full text-left ${selected ? "bg-zinc-200 dark:bg-zinc-800" : "hover:bg-zinc-100 dark:hover:bg-zinc-800/60"}`}
        style={{ paddingLeft: `${depth * 12 + 18}px` }}
      >
        <MethodBadge method={request.method} />
        <span className="truncate">{request.name}</span>
      </button>
    </li>
  );
}

const smallInput =
  "min-w-0 flex-1 rounded border border-zinc-300 bg-white px-2 py-1 text-sm focus:border-indigo-500 focus:outline-none dark:border-zinc-700 dark:bg-zinc-900";

function NameForm({ initial, onSave, onCancel }: { initial: string; onSave: (name: string) => void; onCancel: () => void }) {
  const [name, setName] = useState(initial);
  return (
    <form className="flex gap-1 px-1 py-1" onSubmit={(e) => { e.preventDefault(); if (name.trim()) onSave(name); }}>
      <input aria-label="Name" className={smallInput} value={name} maxLength={200} autoFocus onChange={(e) => setName(e.target.value)} onKeyDown={(e) => e.key === "Escape" && onCancel()} />
      <button type="submit" className="text-xs font-medium text-indigo-600 dark:text-indigo-400">Save</button>
      <button type="button" className="text-xs text-zinc-500" onClick={onCancel}>Cancel</button>
    </form>
  );
}

/** Name plus parent picker. The server rejects moving a folder into itself; the picker hides those options too. */
function FolderForm({ folders, initial, onSave, onCancel }: { folders: Folder[]; initial: Folder; onSave: (name: string, parentId: string | null) => void; onCancel: () => void }) {
  const [name, setName] = useState(initial.name);
  const [parentId, setParentId] = useState(initial.parentFolderId);
  const ownSubtree = new Set(initial.id ? [initial.id, ...folderPaths(folders, initial.id).map((f) => f.id)] : []);
  const choices = folderPaths(folders).filter((f) => !ownSubtree.has(f.id));

  return (
    <form className="space-y-1 rounded border border-zinc-200 p-1.5 dark:border-zinc-800" onSubmit={(e) => { e.preventDefault(); if (name.trim()) onSave(name, parentId); }}>
      <input aria-label="Folder name" placeholder="Folder name" className={`${smallInput} w-full`} value={name} maxLength={200} autoFocus onChange={(e) => setName(e.target.value)} onKeyDown={(e) => e.key === "Escape" && onCancel()} />
      <div className="flex gap-1">
        <select aria-label="Parent folder" className={smallInput} value={parentId ?? ""} onChange={(e) => setParentId(e.target.value || null)}>
          <option value="">Top level</option>
          {choices.map((f) => <option key={f.id} value={f.id}>{f.path}</option>)}
        </select>
        <button type="submit" className="text-xs font-medium text-indigo-600 dark:text-indigo-400">Save</button>
        <button type="button" className="text-xs text-zinc-500" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}

function Confirm({ message, onConfirm, onCancel }: { message: string; onConfirm: () => void; onCancel: () => void }) {
  return (
    <div className="rounded border border-red-200 px-2 py-1.5 text-xs dark:border-red-900">
      <p>{message}</p>
      <div className="mt-1 flex gap-2">
        <button type="button" className="font-medium text-red-600 dark:text-red-400" onClick={onConfirm}>Delete</button>
        <button type="button" className="text-zinc-500" onClick={onCancel}>Cancel</button>
      </div>
    </div>
  );
}
