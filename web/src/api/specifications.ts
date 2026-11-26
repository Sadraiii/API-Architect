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
  /** Where the document was imported from, when it came from a URL. Re-import uses it. */
  sourceUrl: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface SpecificationInput {
  name: string;
  /** The OpenAPI document as JSON text. */
  content: string;
}

/** A document to import: pasted or uploaded text, or a URL for the server to download it from. */
export type SpecificationSource = { name: string; content: string } | { name: string; url: string };

/** What a re-import changed. Endpoints read like "GET /pets/{id}". */
export interface SpecificationChanges {
  oldVersion: string;
  newVersion: string;
  addedEndpoints: string[];
  removedEndpoints: string[];
  changedEndpoints: string[];
  addedSchemas: string[];
  removedSchemas: string[];
  changedSchemas: string[];
  /** The document changed in other ways too, such as its description, servers or tags. */
  otherChanges: boolean;
  hasChanges: boolean;
}

export interface ReimportResult {
  specification: Specification;
  changes: SpecificationChanges;
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
  });
}

export function useSpecification(id: string) {
  return useQuery({ queryKey: specificationKey(id), queryFn: () => api<Specification>(`/api/specifications/${id}`) });
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
    mutationFn: (input: SpecificationSource) =>
      "url" in input
        ? api<Specification>(`/api/projects/${projectId}/specifications/from-url`, {
            method: "POST",
            body: { url: input.url, name: input.name || null },
          })
        : api<Specification>(`/api/projects/${projectId}/specifications`, { method: "POST", body: input }),
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

/** Creates a new project for the API, named after the document's info.title, with the document in it. */
export function useImportProject() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: SpecificationSource) =>
      api<Specification>("/api/projects/import", {
        method: "POST",
        body: "url" in input ? { url: input.url } : { content: input.content },
      }),
    onSuccess: (specification) => {
      queryClient.setQueryData(specificationKey(specification.id), specification);
      return queryClient.invalidateQueries({ queryKey: ["projects"] });
    },
  });
}

/** Downloads the document again from its source URL and saves it. */
export function useReimportSpecification(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api<ReimportResult>(`/api/specifications/${id}/reimport`, { method: "POST" }),
    onSuccess: ({ specification, changes }) => {
      queryClient.setQueryData(specificationKey(id), specification);
      if (!changes.hasChanges) return;
      return Promise.all([
        queryClient.invalidateQueries({ queryKey: documentKey(id) }),
        queryClient.invalidateQueries({ queryKey: projectListKey(specification.projectId) }),
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
