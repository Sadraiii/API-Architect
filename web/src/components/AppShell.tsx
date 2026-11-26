import { NavLink, Outlet, useNavigate } from "react-router";
import { useCurrentUser, useLogout } from "../api/auth";

const navItems = [
  { to: "/projects", label: "Projects" },
  { to: "/client-code", label: "Client code" },
];

export function AppShell() {
  const { data: user } = useCurrentUser();
  const logout = useLogout();
  const navigate = useNavigate();

  return (
    <div className="flex min-h-screen">
      <aside className="flex w-56 shrink-0 flex-col border-r border-zinc-200 bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900/50">
        <div className="px-4 py-4 font-mono text-sm font-semibold tracking-tight">api-architect</div>
        <nav className="flex-1 space-y-0.5 px-2">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) =>
                `block rounded-md px-2.5 py-1.5 text-sm ${
                  isActive
                    ? "bg-zinc-200 font-medium dark:bg-zinc-800"
                    : "text-zinc-600 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800/60"
                }`
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="border-t border-zinc-200 px-4 py-3 text-xs dark:border-zinc-800">
          <p className="truncate text-zinc-500" title={user?.email}>
            {user?.email}
          </p>
          <button
            className="mt-1 text-zinc-700 hover:underline dark:text-zinc-300"
            onClick={() => logout.mutate(undefined, { onSuccess: () => navigate("/login") })}
          >
            Sign out
          </button>
        </div>
      </aside>
      <main className="min-w-0 flex-1 px-8 py-6">
        <Outlet />
      </main>
    </div>
  );
}
