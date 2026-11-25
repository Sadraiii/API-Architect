/** RFC 9457 ProblemDetails, the shape every API error uses. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`);
    this.status = status;
    this.problem = problem;
  }

  /** Every message worth showing: the validation errors when there are any, otherwise the main message. */
  get messages(): string[] {
    const fieldErrors = Object.values(this.problem.errors ?? {}).flat();
    return fieldErrors.length > 0 ? fieldErrors : [this.message];
  }
}

/**
 * Calls the API on the same origin. The auth cookie travels automatically, so there is no token handling here.
 */
export async function api<T>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const response = await fetch(path, {
    method: init.method ?? "GET",
    headers: init.body === undefined ? undefined : { "Content-Type": "application/json" },
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
  });

  if (!response.ok) {
    const isProblem = response.headers.get("Content-Type")?.includes("json") ?? false;
    const problem: ProblemDetails = isProblem ? await response.json() : { status: response.status };
    throw new ApiError(response.status, problem);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}
