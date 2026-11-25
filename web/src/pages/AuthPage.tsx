import { useState, type FormEvent } from "react";
import { Link, useLocation, useNavigate } from "react-router";
import { useLogin, useRegister } from "../api/auth";
import { Button, ErrorMessage, TextField } from "../components/ui";

export function AuthPage({ mode }: { mode: "login" | "register" }) {
  const login = useLogin();
  const register = useRegister();
  const mutation = mode === "login" ? login : register;
  const navigate = useNavigate();
  const location = useLocation();
  const returnTo = (location.state as { from?: string } | null)?.from ?? "/projects";

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [rememberMe, setRememberMe] = useState(false);

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const onSuccess = () => navigate(returnTo, { replace: true });
    if (mode === "login") login.mutate({ email, password, rememberMe }, { onSuccess });
    else register.mutate({ email, password }, { onSuccess });
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4">
      <div className="w-full max-w-sm">
        <p className="mb-6 font-mono text-sm font-semibold">api-architect</p>
        <h1 className="mb-6 text-xl font-semibold">{mode === "login" ? "Sign in" : "Create your account"}</h1>
        <form onSubmit={handleSubmit} className="space-y-4">
          <TextField label="Email" type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
          <TextField
            label="Password"
            type="password"
            autoComplete={mode === "login" ? "current-password" : "new-password"}
            hint={mode === "register" ? "At least 12 characters. A passphrase works well." : undefined}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            minLength={mode === "register" ? 12 : undefined}
          />
          {mode === "login" && (
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} />
              Keep me signed in
            </label>
          )}
          <ErrorMessage error={mutation.error} />
          <Button type="submit" className="w-full" disabled={mutation.isPending}>
            {mode === "login" ? "Sign in" : "Create account"}
          </Button>
        </form>
        <p className="mt-6 text-sm text-zinc-500">
          {mode === "login" ? (
            <>No account yet? <Link className="text-indigo-600 hover:underline dark:text-indigo-400" to="/register">Create one</Link></>
          ) : (
            <>Already have an account? <Link className="text-indigo-600 hover:underline dark:text-indigo-400" to="/login">Sign in</Link></>
          )}
        </p>
      </div>
    </div>
  );
}
