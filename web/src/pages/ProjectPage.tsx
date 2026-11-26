import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { ApiError } from "../api/client";
import { useDeleteProject, useProject, useUpdateProject } from "../api/projects";
import { ProjectForm } from "../components/ProjectForm";
import { ProjectSpecifications } from "../components/ProjectSpecifications";
import { Button, ErrorMessage } from "../components/ui";

export function ProjectPage() {
  const { id = "" } = useParams();
  const project = useProject(id);
  const updateProject = useUpdateProject(id);
  const deleteProject = useDeleteProject();
  const navigate = useNavigate();
  const [editing, setEditing] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  if (project.isPending) return <p className="text-sm text-zinc-500">Loading…</p>;
  if (project.error instanceof ApiError && project.error.status === 404) {
    return (
      <div>
        <p className="text-sm">This project doesn't exist, or it isn't yours.</p>
        <Link className="text-sm text-indigo-600 hover:underline dark:text-indigo-400" to="/projects">Back to projects</Link>
      </div>
    );
  }
  if (project.error) return <ErrorMessage error={project.error} />;

  const p = project.data;
  return (
    <div className="max-w-3xl">
      <Link to="/projects" className="text-sm text-zinc-500 hover:underline">Projects</Link>

      {editing ? (
        <section className="mt-4">
          <h1 className="mb-4 text-lg font-semibold">Edit project</h1>
          <ProjectForm
            initial={p}
            submitLabel="Save changes"
            pending={updateProject.isPending}
            error={updateProject.error}
            onSubmit={(input) => updateProject.mutate(input, { onSuccess: () => setEditing(false) })}
            onCancel={() => {
              setEditing(false);
              updateProject.reset();
            }}
          />
        </section>
      ) : (
        <>
          <header className="mt-2 flex items-start justify-between gap-4">
            <div className="min-w-0">
              <h1 className="text-lg font-semibold">{p.name}</h1>
              {p.description && <p className="mt-1 whitespace-pre-line text-sm text-zinc-600 dark:text-zinc-400">{p.description}</p>}
            </div>
            <div className="flex shrink-0 gap-2">
              <Link
                data-tour="send-request"
                to={`/projects/${p.id}/request`}
                className="inline-flex h-9 items-center rounded-md bg-indigo-600 px-3.5 text-sm font-medium text-white hover:bg-indigo-500"
              >
                Send a request
              </Link>
              <Button variant="secondary" onClick={() => setEditing(true)}>Edit</Button>
            </div>
          </header>

          <dl className="mt-6 grid grid-cols-[max-content_1fr] gap-x-6 gap-y-2 text-sm">
            <dt className="text-zinc-500">Target framework</dt>
            <dd className="font-mono">{p.targetFramework ?? "Not specified"}</dd>
            <dt className="text-zinc-500">Created</dt>
            <dd>{new Date(p.createdAt).toLocaleString()}</dd>
            <dt className="text-zinc-500">Updated</dt>
            <dd>{new Date(p.updatedAt).toLocaleString()}</dd>
          </dl>

          <nav data-tour="project-workspace" className="mt-8 flex gap-3 text-sm" aria-label="Project workspace">
            <Link to={`/projects/${p.id}/collections`} className="rounded-md border border-zinc-200 px-3 py-2 hover:bg-zinc-50 dark:border-zinc-800 dark:hover:bg-zinc-900">
              Collections
            </Link>
            <Link to={`/projects/${p.id}/environments`} className="rounded-md border border-zinc-200 px-3 py-2 hover:bg-zinc-50 dark:border-zinc-800 dark:hover:bg-zinc-900">
              Environments
            </Link>
          </nav>

          <ProjectSpecifications projectId={p.id} />

          <section className="mt-10 rounded-lg border border-red-200 p-4 dark:border-red-900">
            <h2 className="text-sm font-semibold">Delete project</h2>
            <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">This removes the project and everything in it. It can't be undone.</p>
            <ErrorMessage error={deleteProject.error} />
            <div className="mt-3 flex gap-2">
              {confirmingDelete ? (
                <>
                  <Button variant="danger" disabled={deleteProject.isPending} onClick={() => deleteProject.mutate(p.id, { onSuccess: () => navigate("/projects") })}>
                    Yes, delete {p.name}
                  </Button>
                  <Button variant="secondary" onClick={() => setConfirmingDelete(false)}>Cancel</Button>
                </>
              ) : (
                <Button variant="secondary" onClick={() => setConfirmingDelete(true)}>Delete…</Button>
              )}
            </div>
          </section>
        </>
      )}
    </div>
  );
}
