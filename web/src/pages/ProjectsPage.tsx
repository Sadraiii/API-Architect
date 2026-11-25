import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { useCreateProject, useProjects } from "../api/projects";
import { ProjectForm } from "../components/ProjectForm";
import { Button, ErrorMessage } from "../components/ui";

export function ProjectsPage() {
  const projects = useProjects();
  const createProject = useCreateProject();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);

  return (
    <div className="max-w-4xl">
      <header className="mb-6 flex items-center justify-between">
        <h1 className="text-lg font-semibold">Projects</h1>
        {!creating && <Button onClick={() => setCreating(true)}>New project</Button>}
      </header>

      {creating && (
        <section className="mb-8 rounded-lg border border-zinc-200 p-5 dark:border-zinc-800">
          <h2 className="mb-4 text-sm font-semibold">New project</h2>
          <ProjectForm
            submitLabel="Create project"
            pending={createProject.isPending}
            error={createProject.error}
            onSubmit={(input) => createProject.mutate(input, { onSuccess: (project) => navigate(`/projects/${project.id}`) })}
            onCancel={() => {
              setCreating(false);
              createProject.reset();
            }}
          />
        </section>
      )}

      <ErrorMessage error={projects.error} />
      {projects.isPending && <p className="text-sm text-zinc-500">Loading projects…</p>}
      {projects.data?.length === 0 && !creating && (
        <div className="rounded-lg border border-dashed border-zinc-300 p-10 text-center dark:border-zinc-700">
          <p className="text-sm text-zinc-600 dark:text-zinc-400">You have no projects yet.</p>
          <p className="mt-1 text-sm text-zinc-500">A project holds one API's specifications, requests and environments.</p>
        </div>
      )}
      {projects.data && projects.data.length > 0 && (
        <ul className="divide-y divide-zinc-200 rounded-lg border border-zinc-200 dark:divide-zinc-800 dark:border-zinc-800">
          {projects.data.map((project) => (
            <li key={project.id}>
              <Link to={`/projects/${project.id}`} className="flex items-baseline justify-between gap-4 px-4 py-3 hover:bg-zinc-50 dark:hover:bg-zinc-900">
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium">{project.name}</p>
                  {project.description && <p className="truncate text-sm text-zinc-500">{project.description}</p>}
                </div>
                <div className="flex shrink-0 items-center gap-3 text-xs text-zinc-500">
                  {project.targetFramework && <span className="rounded bg-zinc-100 px-1.5 py-0.5 font-mono dark:bg-zinc-800">{project.targetFramework}</span>}
                  <span>Updated {new Date(project.updatedAt).toLocaleDateString()}</span>
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
