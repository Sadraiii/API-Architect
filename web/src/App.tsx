import { Navigate, Outlet, Route, Routes, useLocation } from "react-router";
import { useCurrentUser } from "./api/auth";
import { AppShell } from "./components/AppShell";
import { ErrorMessage } from "./components/ui";
import { AuthPage } from "./pages/AuthPage";
import { ProjectPage } from "./pages/ProjectPage";
import { ProjectsPage } from "./pages/ProjectsPage";

/** Sends signed-out visitors to the login page, remembering where they were going. */
function RequireSignIn() {
  const { data: user, isPending, error } = useCurrentUser();
  const location = useLocation();

  if (isPending) return null;
  if (error) return <div className="p-8"><ErrorMessage error={error} /></div>;
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  return <Outlet />;
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<AuthPage mode="login" />} />
      <Route path="/register" element={<AuthPage mode="register" />} />
      <Route element={<RequireSignIn />}>
        <Route element={<AppShell />}>
          <Route index element={<Navigate to="/projects" replace />} />
          <Route path="/projects" element={<ProjectsPage />} />
          <Route path="/projects/:id" element={<ProjectPage />} />
          <Route path="*" element={<p className="text-sm">Page not found.</p>} />
        </Route>
      </Route>
    </Routes>
  );
}
