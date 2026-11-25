import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useSyncExternalStore } from "react";
import { api } from "./client";
import type { KeyLocation, KeyValue } from "./collections";

export interface EnvironmentVariable {
  name: string;
  /** Null for secrets: the API never returns them. */
  value: string | null;
  isSecret: boolean;
}

export interface EnvironmentAuth {
  type: "none" | "bearer" | "apiKey";
  keyName: string | null;
  keyLocation: KeyLocation | null;
  /** Whether a token or key is stored. The value itself is never returned. */
  hasValue: boolean;
}

export interface Environment {
  id: string;
  projectId: string;
  name: string;
  baseUrl: string | null;
  variables: EnvironmentVariable[];
  headers: KeyValue[];
  auth: EnvironmentAuth;
  createdAt: string;
  updatedAt: string;
}

/** For secrets, a null value means "keep what's stored". */
export interface EnvironmentInput {
  name: string;
  baseUrl: string | null;
  variables: EnvironmentVariable[];
  headers: KeyValue[];
  auth: { type: EnvironmentAuth["type"]; value: string | null; keyName: string | null; keyLocation: KeyLocation | null };
}

const environmentsKey = (projectId: string) => ["projects", projectId, "environments"] as const;

export function useEnvironments(projectId: string) {
  return useQuery({
    queryKey: environmentsKey(projectId),
    queryFn: () => api<Environment[]>(`/api/projects/${projectId}/environments`),
  });
}

export function useSaveEnvironment(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: string | null; input: EnvironmentInput }) =>
      id === null
        ? api<Environment>(`/api/projects/${projectId}/environments`, { method: "POST", body: input })
        : api<Environment>(`/api/environments/${id}`, { method: "PUT", body: input }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: environmentsKey(projectId) }),
  });
}

export function useDeleteEnvironment(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<void>(`/api/environments/${id}`, { method: "DELETE" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: environmentsKey(projectId) }),
  });
}

/*
 * The active environment is a per-browser UI choice, sent along when a request runs, so it lives in
 * localStorage rather than on the server. Storage can be unavailable (private windows), so every
 * access is guarded and the app works without it.
 */
const storageKey = (projectId: string) => `api-architect.environment.${projectId}`;
const listeners = new Set<() => void>();
/** This tab's choices, which also covers browsers where storage throws. */
const chosen = new Map<string, string | null>();

function readActive(projectId: string): string | null {
  if (chosen.has(projectId)) return chosen.get(projectId) ?? null;
  try {
    return localStorage.getItem(storageKey(projectId));
  } catch {
    return null;
  }
}

export function useActiveEnvironmentId(projectId: string): [string | null, (id: string | null) => void] {
  const subscribe = useCallback((listener: () => void) => {
    listeners.add(listener);
    return () => listeners.delete(listener);
  }, []);
  const active = useSyncExternalStore(subscribe, () => readActive(projectId));

  const setActive = useCallback(
    (id: string | null) => {
      chosen.set(projectId, id);
      try {
        if (id === null) localStorage.removeItem(storageKey(projectId));
        else localStorage.setItem(storageKey(projectId), id);
      } catch {
        // Not persisted; the in-memory choice still applies until the page reloads.
      }
      listeners.forEach((listener) => listener());
    },
    [projectId],
  );

  return [active, setActive];
}

const variablePattern = /\{\{\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*\}\}/g;

/**
 * Roughly what URL a request will call in an environment, for display only. The server does the real
 * substitution; secrets aren't available here, so they show as •••.
 */
export function previewUrl(url: string, query: KeyValue[], environment: Environment | undefined): string {
  const values = new Map<string, string>();
  if (environment?.baseUrl) values.set("baseUrl", environment.baseUrl);
  for (const variable of environment?.variables ?? []) {
    values.set(variable.name, variable.isSecret ? "•••" : (variable.value ?? ""));
  }
  const fill = (text: string) => text.replace(variablePattern, (match, name: string) => values.get(name) ?? match);

  let result = fill(url).trim();
  if (!/^https?:\/\//i.test(result) && environment?.baseUrl) {
    result = result ? `${environment.baseUrl.replace(/\/+$/, "")}/${result.replace(/^\/+/, "")}` : environment.baseUrl;
  }
  const params = query
    .filter((q) => q.enabled && q.name)
    .map((q) => `${encodeURIComponent(fill(q.name))}=${encodeURIComponent(fill(q.value))}`);
  return params.length ? `${result}${result.includes("?") ? "&" : "?"}${params.join("&")}` : result;
}
