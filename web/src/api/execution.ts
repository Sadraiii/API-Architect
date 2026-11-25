import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";

/** Must match ExecutionRules.Methods in the API. */
export const httpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"] as const;
export type HttpMethod = (typeof httpMethods)[number];

export interface HeaderPair {
  name: string;
  value: string;
}

export type AuthKind = "Bearer" | "ApiKeyHeader" | "ApiKeyQuery";

export interface RequestAuth {
  kind: AuthKind;
  value: string;
  name?: string | null;
}

export interface ExecuteInput {
  method: HttpMethod;
  url: string;
  headers: HeaderPair[];
  body: string | null;
  contentType: string | null;
  auth: RequestAuth | null;
}

export interface ExecutionError {
  code: string;
  message: string;
}

export interface Execution {
  id: string;
  requestId: string | null;
  method: string;
  url: string;
  requestHeaders: HeaderPair[];
  requestContentType: string | null;
  requestBody: string | null;
  requestBodyTruncated: boolean;
  auth: { kind: AuthKind; name: string | null } | null;
  statusCode: number | null;
  reasonPhrase: string | null;
  responseHeaders: HeaderPair[];
  responseBody: string;
  responseBodyTruncated: boolean;
  responseBodyIsBinary: boolean;
  responseSizeBytes: number;
  durationMs: number;
  error: ExecutionError | null;
  executedAt: string;
}

export interface ExecutionSummary {
  id: string;
  requestId: string | null;
  method: string;
  url: string;
  statusCode: number | null;
  durationMs: number;
  errorCode: string | null;
  executedAt: string;
}

const historyKey = (projectId: string) => ["projects", projectId, "executions"] as const;

export function useExecute(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: ExecuteInput) =>
      api<Execution>(`/api/projects/${projectId}/execute`, { method: "POST", body: input }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: historyKey(projectId) }),
  });
}

export function useExecutionHistory(projectId: string) {
  return useQuery({
    queryKey: historyKey(projectId),
    queryFn: () => api<ExecutionSummary[]>(`/api/projects/${projectId}/executions`),
  });
}

/** A stored execution. Its bodies are the capped copies kept in history. */
export function useExecution(id: string | null) {
  return useQuery({
    queryKey: ["executions", id],
    queryFn: () => api<Execution>(`/api/executions/${id}`),
    enabled: id !== null,
  });
}

export function useClearHistory(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api<void>(`/api/projects/${projectId}/executions`, { method: "DELETE" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: historyKey(projectId) }),
  });
}
