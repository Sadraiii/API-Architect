import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type { Specification } from "./specifications";
import type { OpenApiDocument } from "../openapi/model";

// Mirrors Features/Designs/DesignDocument.cs. The server validates the whole document on save.

export const simpleTypes = ["string", "int", "long", "decimal", "double", "bool", "guid", "datetime", "date"] as const;
export const methods = ["GET", "POST", "PUT", "PATCH", "DELETE"] as const;
export const successStatuses = [200, 201, 202, 204] as const;
export const errorStatuses = [400, 401, 403, 404, 409] as const;

export const statusText: Record<number, string> = {
  200: "OK", 201: "Created", 202: "Accepted", 204: "No Content",
  400: "Bad Request", 401: "Unauthorized", 403: "Forbidden", 404: "Not Found", 409: "Conflict",
};

export interface PropertyDefinition {
  name: string;
  /** A simple type or the name of a model. */
  type: string;
  isList: boolean;
  required: boolean;
  description?: string | null;
  minLength?: number | null;
  maxLength?: number | null;
  minimum?: number | null;
  maximum?: number | null;
  pattern?: string | null;
  format?: "email" | "url" | null;
}

export interface ModelDefinition {
  name: string;
  description?: string | null;
  properties: PropertyDefinition[];
}

export interface ParameterDefinition {
  name: string;
  in: "path" | "query";
  type: string;
  required: boolean;
  description?: string | null;
}

export interface EndpointDefinition {
  resource: string;
  method: string;
  route: string;
  name: string;
  summary?: string | null;
  parameters: ParameterDefinition[];
  requestModel?: string | null;
  responseModel?: string | null;
  responseIsList: boolean;
  successStatus: number;
  errorStatuses: number[];
  requiresAuth: boolean;
}

export interface DesignDocument {
  title: string;
  version: string;
  description?: string | null;
  namespace: string;
  models: ModelDefinition[];
  endpoints: EndpointDefinition[];
}

export interface DesignSummary {
  id: string;
  projectId: string;
  name: string;
  specificationId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface Design extends DesignSummary {
  document: DesignDocument;
}

export interface GeneratedFile {
  path: string;
  content: string;
}

const designsKey = ["designs"] as const;
const projectListKey = (projectId: string) => [...designsKey, "project", projectId] as const;
const designKey = (id: string) => [...designsKey, id] as const;

export const openApiUrl = (id: string) => `/api/designs/${id}/openapi`;
export const zipUrl = (id: string) => `/api/designs/${id}/code/zip`;

export function useProjectDesigns(projectId: string) {
  return useQuery({
    queryKey: projectListKey(projectId),
    queryFn: () => api<DesignSummary[]>(`/api/projects/${projectId}/designs`),
  });
}

export function useDesign(id: string) {
  return useQuery({ queryKey: designKey(id), queryFn: () => api<Design>(`/api/designs/${id}`) });
}

/** Keyed on updatedAt, so a save refetches the preview and the generated code. */
export function useDesignOpenApi(design: Design) {
  return useQuery({
    queryKey: [...designKey(design.id), "openapi", design.updatedAt],
    queryFn: () => api<OpenApiDocument>(openApiUrl(design.id)),
    staleTime: Infinity,
  });
}

export function useDesignCode(design: Design) {
  return useQuery({
    queryKey: [...designKey(design.id), "code", design.updatedAt],
    queryFn: () => api<GeneratedFile[]>(`/api/designs/${design.id}/code`),
    staleTime: Infinity,
  });
}

export function useCreateDesign(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { name: string; startFrom: "blank" | "example" }) =>
      api<Design>(`/api/projects/${projectId}/designs`, { method: "POST", body: input }),
    onSuccess: (design) => {
      queryClient.setQueryData(designKey(design.id), design);
      return queryClient.invalidateQueries({ queryKey: projectListKey(projectId) });
    },
  });
}

export function useSaveDesign(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { name: string; document: DesignDocument }) =>
      api<Design>(`/api/designs/${id}`, { method: "PUT", body: input }),
    onSuccess: (design) => {
      queryClient.setQueryData(designKey(id), design);
      return queryClient.invalidateQueries({ queryKey: projectListKey(design.projectId) });
    },
  });
}

export function useSaveAsSpecification(design: Design) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api<Specification>(`/api/designs/${design.id}/specification`, { method: "POST" }),
    onSuccess: (specification) => {
      queryClient.setQueryData(designKey(design.id), { ...design, specificationId: specification.id });
      return queryClient.invalidateQueries({ queryKey: ["specifications"] });
    },
  });
}

export function useDeleteDesign() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (design: DesignSummary) => api<void>(`/api/designs/${design.id}`, { method: "DELETE" }),
    onSuccess: (_, design) => {
      queryClient.removeQueries({ queryKey: designKey(design.id) });
      return queryClient.invalidateQueries({ queryKey: projectListKey(design.projectId) });
    },
  });
}
