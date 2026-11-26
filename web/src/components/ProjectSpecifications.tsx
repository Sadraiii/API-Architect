import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { useImportSpecification, useProjectSpecifications } from "../api/specifications";
import { SpecificationForm } from "./SpecificationForm";
import { Button, ErrorMessage } from "./ui";

/** The project's imported OpenAPI documents, with the import form. */
export function ProjectSpecifications({ projectId }: { projectId: string }) {
  const specifications = useProjectSpecifications(projectId);
  const importSpecification = useImportSpecification(projectId);
  const navigate = useNavigate();
  const [importing, setImporting] = useState(false);

  return (
    <section data-tour="specifications" className="mt-10">
      <header className="mb-3 flex items-center justify-between">
        <h2 className="text-sm font-semibold">API specifications</h2>
        {!importing && <Button data-tour="import-spec" variant="secondary" onClick={() => setImporting(true)}>Import OpenAPI</Button>}
      </header>

      {importing && (
        <div className="mb-4 rounded-lg border border-zinc-200 p-5 dark:border-zinc-800">
          <SpecificationForm
            submitLabel="Import"
            pending={importSpecification.isPending}
            error={importSpecification.error}
            onSubmit={(input) =>
              importSpecification.mutate(input, { onSuccess: (specification) => navigate(`/specifications/${specification.id}`) })
            }
            onCancel={() => {
              setImporting(false);
              importSpecification.reset();
            }}
          />
        </div>
      )}

      <ErrorMessage error={specifications.error} />
      {specifications.data?.length === 0 && !importing && (
        <p className="rounded-lg border border-dashed border-zinc-300 p-6 text-center text-sm text-zinc-500 dark:border-zinc-700">
          No specifications yet. Import an OpenAPI JSON document to browse its endpoints and schemas.
        </p>
      )}
      {specifications.data && specifications.data.length > 0 && (
        <ul className="divide-y divide-zinc-200 rounded-lg border border-zinc-200 dark:divide-zinc-800 dark:border-zinc-800">
          {specifications.data.map((specification) => (
            <li key={specification.id}>
              <Link
                to={`/specifications/${specification.id}`}
                className="flex items-baseline justify-between gap-4 px-4 py-3 hover:bg-zinc-50 dark:hover:bg-zinc-900"
              >
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium">{specification.name}</p>
                  <p className="truncate text-sm text-zinc-500">{specification.title} · v{specification.version}</p>
                </div>
                <div className="flex shrink-0 items-center gap-3 text-xs text-zinc-500">
                  <span className="rounded bg-zinc-100 px-1.5 py-0.5 font-mono dark:bg-zinc-800">OpenAPI {specification.openApiVersion}</span>
                  <span>Updated {new Date(specification.updatedAt).toLocaleDateString()}</span>
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
