import { useCallback } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "./client";

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

/** Sends a saved request, filling {{variables}} from the given environment, and records it in history. */
export function useSendSavedRequest(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ requestId, environmentId }: { requestId: string; environmentId: string | null }) =>
      api<Execution>(
        `/api/requests/${requestId}/execute${environmentId ? `?environmentId=${encodeURIComponent(environmentId)}` : ""}`,
        { method: "POST" },
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: historyKey(projectId) }),
  });
}

/** Statuses whose Response must not have a body. */
const nullBodyStatuses = new Set([204, 205, 304]);

function textResponse(status: number, text: string) {
  return new Response(text, { status, headers: { "Content-Type": "text/plain; charset=utf-8" } });
}

/**
 * Sends a browser-built Request through the server-side runner and rebuilds the target's response, so tools such
 * as Scalar's "Try it out" get the same timeouts, size limits and history as the request page.
 * Runner failures come back as a 502 (no response from the target) or a 400 (invalid request), with the reason as text.
 */
export function useServerSend(projectId: string) {
  const queryClient = useQueryClient();
  return useCallback(
    async (request: Request): Promise<Response> => {
      const method = request.method.toUpperCase();
      if (!httpMethods.includes(method as HttpMethod)) {
        return textResponse(405, `API Architect can send ${httpMethods.join(", ")} requests, not ${method}.`);
      }

      const headers: HeaderPair[] = [];
      let contentType: string | null = null;
      request.headers.forEach((value, name) => {
        if (name === "content-type") contentType = value;
        else headers.push({ name, value });
      });
      const body = method === "GET" ? "" : await request.text();

      let execution: Execution;
      try {
        execution = await api<Execution>(`/api/projects/${projectId}/execute`, {
          method: "POST",
          body: {
            method: method as HttpMethod,
            url: request.url,
            headers,
            body: body === "" ? null : body,
            contentType: body === "" ? null : contentType,
            auth: null,
          } satisfies ExecuteInput,
        });
      } catch (error) {
        const reason = error instanceof ApiError ? error.messages.join("\n") : "The request couldn't be sent.";
        return textResponse(400, `API Architect didn't send this request:\n${reason}`);
      } finally {
        void queryClient.invalidateQueries({ queryKey: historyKey(projectId) });
      }

      if (execution.error || execution.statusCode === null) {
        return textResponse(502, `No response (${execution.error?.code}): ${execution.error?.message}`);
      }

      const responseHeaders = new Headers();
      for (const header of execution.responseHeaders) {
        try {
          responseHeaders.append(header.name, header.value);
        } catch {
          // A header the browser won't accept on a constructed Response; it's still in history.
        }
      }

      const status = Math.min(Math.max(execution.statusCode, 200), 599);
      const responseBody = nullBodyStatuses.has(status)
        ? null
        : execution.responseBodyIsBinary
          ? `(binary content, ${execution.responseSizeBytes} bytes, not shown)`
          : execution.responseBody;
      return new Response(responseBody, { status, statusText: execution.reasonPhrase ?? "", headers: responseHeaders });
    },
    [projectId, queryClient],
  );
}
