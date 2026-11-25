import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "./client";

export interface CurrentUser {
  email: string;
}

const meKey = ["auth", "me"] as const;

/** The signed-in user, or null when signed out. */
export function useCurrentUser() {
  return useQuery({
    queryKey: meKey,
    queryFn: async () => {
      try {
        return await api<CurrentUser>("/api/auth/me");
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null;
        throw error;
      }
    },
    staleTime: Infinity,
  });
}

export function useLogin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { email: string; password: string; rememberMe: boolean }) =>
      api<CurrentUser>("/api/auth/login", { method: "POST", body }),
    onSuccess: (user) => queryClient.setQueryData(meKey, user),
  });
}

export function useRegister() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { email: string; password: string }) =>
      api<CurrentUser>("/api/auth/register", { method: "POST", body }),
    onSuccess: (user) => queryClient.setQueryData(meKey, user),
  });
}

export function useLogout() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api<void>("/api/auth/logout", { method: "POST" }),
    onSuccess: () => {
      // Drop every cached response so nothing from this account survives into the next one.
      queryClient.clear();
      queryClient.setQueryData(meKey, null);
    },
  });
}
