import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";

export interface Project {
  id: string;
  name: string;
  description: string | null;
  targetFramework: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ProjectInput {
  name: string;
  description: string | null;
  targetFramework: string | null;
}

/** Must match the AllowedValues list on ProjectRequest in the API. */
export const targetFrameworks = ["net11.0", "net10.0", "net9.0", "net8.0"] as const;

const projectsKey = ["projects"] as const;

export function useProjects() {
  return useQuery({ queryKey: projectsKey, queryFn: () => api<Project[]>("/api/projects") });
}

export function useProject(id: string) {
  return useQuery({ queryKey: [...projectsKey, id], queryFn: () => api<Project>(`/api/projects/${id}`) });
}

export function useCreateProject() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: ProjectInput) => api<Project>("/api/projects", { method: "POST", body: input }),
    onSuccess: (project) => {
      queryClient.setQueryData([...projectsKey, project.id], project);
      return queryClient.invalidateQueries({ queryKey: projectsKey, exact: true });
    },
  });
}

export function useUpdateProject(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: ProjectInput) => api<Project>(`/api/projects/${id}`, { method: "PUT", body: input }),
    onSuccess: (project) => {
      queryClient.setQueryData([...projectsKey, id], project);
      return queryClient.invalidateQueries({ queryKey: projectsKey, exact: true });
    },
  });
}

export function useDeleteProject() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<void>(`/api/projects/${id}`, { method: "DELETE" }),
    onSuccess: (_, id) => {
      queryClient.removeQueries({ queryKey: [...projectsKey, id] });
      return queryClient.invalidateQueries({ queryKey: projectsKey, exact: true });
    },
  });
}
