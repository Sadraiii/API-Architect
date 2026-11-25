import { Link } from "react-router";
import { useActiveEnvironmentId, useEnvironments } from "../api/environments";

/** Picks the environment requests in this project run against. Remembered per project in this browser. */
export function EnvironmentSwitcher({ projectId }: { projectId: string }) {
  const environments = useEnvironments(projectId);
  const [activeId, setActiveId] = useActiveEnvironmentId(projectId);
  const known = environments.data?.some((e) => e.id === activeId) ?? false;

  return (
    <div className="flex items-center gap-2 text-sm">
      <label htmlFor="environment" className="text-zinc-500">Environment</label>
      <select
        id="environment"
        className="h-8 rounded-md border border-zinc-300 bg-white px-2 text-sm dark:border-zinc-700 dark:bg-zinc-900"
        value={known ? (activeId ?? "") : ""}
        onChange={(e) => setActiveId(e.target.value || null)}
      >
        <option value="">No environment</option>
        {environments.data?.map((environment) => (
          <option key={environment.id} value={environment.id}>{environment.name}</option>
        ))}
      </select>
      <Link to={`/projects/${projectId}/environments`} className="text-xs text-indigo-600 hover:underline dark:text-indigo-400">
        Manage
      </Link>
    </div>
  );
}
