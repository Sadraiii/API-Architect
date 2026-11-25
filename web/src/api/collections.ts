import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";

export interface KeyValue {
  name: string;
  value: string;
  enabled: boolean;
}

export type AuthType = "inherit" | "none" | "bearer" | "apiKey";
export type KeyLocation = "header" | "query";

export interface RequestAuth {
  type: AuthType;
  value: string | null;
  keyName: string | null;
  keyLocation: KeyLocation | null;
}

/** Must match the AllowedValues list on SavedRequestInput in the API. */
export const httpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"] as const;
export type HttpMethod = (typeof httpMethods)[number];

export interface CollectionSummary {
  id: string;
  projectId: string;
  name: string;
  description: string | null;
  requestCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface Folder {
  id: string;
  name: string;
  parentFolderId: string | null;
}

export interface RequestSummary {
  id: string;
  folderId: string | null;
  name: string;
  method: HttpMethod;
  updatedAt: string;
}

export interface CollectionDetail extends Omit<CollectionSummary, "requestCount"> {
  folders: Folder[];
  requests: RequestSummary[];
}

export interface SavedRequestInput {
  name: string;
  method: HttpMethod;
  url: string;
  folderId: string | null;
  headers: KeyValue[];
  queryParameters: KeyValue[];
  body: string | null;
  contentType: string | null;
  auth: RequestAuth;
}

export interface SavedRequest extends SavedRequestInput {
  id: string;
  collectionId: string;
  createdAt: string;
  updatedAt: string;
}

export interface CollectionImportResult {
  collection: CollectionSummary;
  warnings: string[];
}

const collectionsKey = (projectId: string) => ["projects", projectId, "collections"] as const;
const collectionKey = (id: string) => ["collections", id] as const;
const requestKey = (id: string) => ["requests", id] as const;

export function useCollections(projectId: string) {
  return useQuery({
    queryKey: collectionsKey(projectId),
    queryFn: () => api<CollectionSummary[]>(`/api/projects/${projectId}/collections`),
  });
}

export function useCollection(id: string) {
  return useQuery({ queryKey: collectionKey(id), queryFn: () => api<CollectionDetail>(`/api/collections/${id}`) });
}

export function useSavedRequest(id: string | null) {
  return useQuery({
    queryKey: requestKey(id ?? ""),
    queryFn: () => api<SavedRequest>(`/api/requests/${id}`),
    enabled: id !== null,
  });
}

/**
 * Every collection write changes the tree and the summary list, so one invalidation covers them all.
 * Collections are small, so refetching beats patching the cache by hand.
 */
function useCollectionMutation<TInput, TResult>(
  projectId: string,
  mutationFn: (input: TInput) => Promise<TResult>,
  cache?: (queryClient: ReturnType<typeof useQueryClient>, result: TResult) => void,
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: (result) => {
      cache?.(queryClient, result);
      return Promise.all([
        queryClient.invalidateQueries({ queryKey: collectionsKey(projectId) }),
        queryClient.invalidateQueries({ queryKey: ["collections"] }),
      ]);
    },
  });
}

export function useCreateCollection(projectId: string) {
  return useCollectionMutation(projectId, (input: { name: string; description: string | null }) =>
    api<CollectionSummary>(`/api/projects/${projectId}/collections`, { method: "POST", body: input }),
  );
}

export function useUpdateCollection(projectId: string) {
  return useCollectionMutation(projectId, ({ id, ...input }: { id: string; name: string; description: string | null }) =>
    api<CollectionSummary>(`/api/collections/${id}`, { method: "PUT", body: input }),
  );
}

export function useDeleteCollection(projectId: string) {
  return useCollectionMutation(projectId, (id: string) => api<void>(`/api/collections/${id}`, { method: "DELETE" }));
}

export function useImportCollection(projectId: string) {
  return useCollectionMutation(projectId, (file: unknown) =>
    api<CollectionImportResult>(`/api/projects/${projectId}/collections/import`, { method: "POST", body: file }),
  );
}

export function useCreateFolder(projectId: string) {
  return useCollectionMutation(projectId, ({ collectionId, ...input }: { collectionId: string; name: string; parentFolderId: string | null }) =>
    api<Folder>(`/api/collections/${collectionId}/folders`, { method: "POST", body: input }),
  );
}

export function useUpdateFolder(projectId: string) {
  return useCollectionMutation(projectId, ({ id, ...input }: { id: string; name: string; parentFolderId: string | null }) =>
    api<Folder>(`/api/folders/${id}`, { method: "PUT", body: input }),
  );
}

export function useDeleteFolder(projectId: string) {
  return useCollectionMutation(projectId, (id: string) => api<void>(`/api/folders/${id}`, { method: "DELETE" }));
}

export function useCreateRequest(projectId: string) {
  return useCollectionMutation(projectId, ({ collectionId, input }: { collectionId: string; input: SavedRequestInput }) =>
    api<SavedRequest>(`/api/collections/${collectionId}/requests`, { method: "POST", body: input }),
  );
}

export function useUpdateRequest(projectId: string) {
  return useCollectionMutation(
    projectId,
    ({ id, input }: { id: string; input: SavedRequestInput }) =>
      api<SavedRequest>(`/api/requests/${id}`, { method: "PUT", body: input }),
    (queryClient, request) => queryClient.setQueryData(requestKey(request.id), request),
  );
}

export function useDuplicateRequest(projectId: string) {
  return useCollectionMutation(projectId, (id: string) => api<SavedRequest>(`/api/requests/${id}/duplicate`, { method: "POST" }));
}

export function useDeleteRequest(projectId: string) {
  return useCollectionMutation(projectId, (id: string) => api<void>(`/api/requests/${id}`, { method: "DELETE" }));
}

/** The export endpoint answers with a file download, so a plain link is all the UI needs. */
export const collectionExportUrl = (id: string) => `/api/collections/${id}/export`;

export function emptyRequest(folderId: string | null): SavedRequestInput {
  return {
    name: "New request",
    method: "GET",
    url: "",
    folderId,
    headers: [],
    queryParameters: [],
    body: null,
    contentType: null,
    auth: { type: "inherit", value: null, keyName: null, keyLocation: null },
  };
}
