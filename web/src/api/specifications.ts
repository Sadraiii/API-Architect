import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type { OpenApiDocument } from "../openapi/model";

export interface Specification {
  id: string;
  projectId: string;
  name: string;
  /** The document's info.title. */
  title: string;
  /** The document's info.version (the API's version). */
  version: string;
  /** The document's openapi field, such as 3.1.0. */
  openApiVersion: string;
  createdAt: string;
  updatedAt: string;
}

export interface SpecificationInput {
  name: string;
  /** The OpenAPI document as JSON text. */
  content: string;
}

/** Must match SpecificationRules.ContentMaxLength in the API. */
export const specificationContentMaxLength = 5_000_000;

const specificationsKey = ["specifications"] as const;
const projectListKey = (projectId: string) => [...specificationsKey, "project", projectId] as const;
const specificationKey = (id: string) => [...specificationsKey, id] as const;
const documentKey = (id: string) => [...specificationsKey, id, "document"] as const;

/** Where the original document can be downloaded from. */
export const documentUrl = (id: string) => `/api/specifications/${id}/document`;

export function useProjectSpecifications(projectId: string) {
  return useQuery({
    queryKey: projectListKey(projectId),
    queryFn: () => api<Specification[]>(`/api/projects/${projectId}/specifications`),
    enabled: projectId !== "",
  });
}

export function useSpecification(id: string) {
  return useQuery({
    queryKey: specificationKey(id),
    queryFn: () => api<Specification>(`/api/specifications/${id}`),
    enabled: id !== "",
  });
}

/** The parsed OpenAPI document. Imported documents only change through a replace, so it never goes stale on its own. */
export function useSpecificationDocument(id: string) {
  return useQuery({
    queryKey: documentKey(id),
    queryFn: () => api<OpenApiDocument>(documentUrl(id)),
    staleTime: Infinity,
  });
}

export function useImportSpecification(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: SpecificationInput) =>
      api<Specification>(`/api/projects/${projectId}/specifications`, { method: "POST", body: input }),
    onSuccess: (specification) => {
      queryClient.setQueryData(specificationKey(specification.id), specification);
      return Promise.all([
        queryClient.invalidateQueries({ queryKey: projectListKey(projectId) }),
        // Importing bumps the project's UpdatedAt.
        queryClient.invalidateQueries({ queryKey: ["projects"] }),
      ]);
    },
  });
}

export function useReplaceSpecification(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: SpecificationInput) => api<Specification>(`/api/specifications/${id}`, { method: "PUT", body: input }),
    onSuccess: (specification) => {
      queryClient.setQueryData(specificationKey(id), specification);
      return Promise.all([
        queryClient.invalidateQueries({ queryKey: documentKey(id) }),
        queryClient.invalidateQueries({ queryKey: projectListKey(specification.projectId) }),
        queryClient.invalidateQueries({ queryKey: ["projects"] }),
      ]);
    },
  });
}

export function useDeleteSpecification() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (specification: Specification) => api<void>(`/api/specifications/${specification.id}`, { method: "DELETE" }),
    onSuccess: (_, specification) => {
      queryClient.removeQueries({ queryKey: specificationKey(specification.id) });
      return queryClient.invalidateQueries({ queryKey: projectListKey(specification.projectId) });
    },
  });
}
