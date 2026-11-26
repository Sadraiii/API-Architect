import { useMutation } from "@tanstack/react-query";
import { api } from "./client";

export type ClientTemplate = "console" | "worker";

export interface ClientCodeRequest {
  template: ClientTemplate;
  /** The Visual Studio project name, which is also the namespace. */
  projectName: string;
  /** Operations as "METHOD /path". */
  operations: string[];
}

export interface ClientCodeFile {
  fileName: string;
  /** Where the code goes in the Visual Studio project. */
  where: string;
  content: string;
}

export interface ClientCode {
  template: ClientTemplate;
  projectName: string;
  /** Visual Studio steps. Menu names and code are in `backticks`. */
  steps: string[];
  files: ClientCodeFile[];
  notes: string[];
}

/** Must match ClientCodeRules.ProjectNamePattern in the API. */
export const projectNamePattern = /^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$/;

export function useGenerateClientCode(specificationId: string) {
  return useMutation({
    mutationFn: (request: ClientCodeRequest) =>
      api<ClientCode>(`/api/specifications/${specificationId}/client-code`, { method: "POST", body: request }),
  });
}
