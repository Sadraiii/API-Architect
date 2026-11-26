import type { DesignDocument, EndpointDefinition, ParameterDefinition } from "../../api/designs";

// Pure helpers for editing a design in the browser. The server is still the one that validates.

/** `base`, or `base2`, `base3`… whichever isn't taken. */
export function uniqueName(base: string, taken: string[]): string {
  const used = new Set(taken.map((t) => t.toLowerCase()));
  if (!used.has(base.toLowerCase())) return base;
  for (let i = 2; ; i++) if (!used.has(`${base}${i}`.toLowerCase())) return `${base}${i}`;
}

/**
 * Renames the model at `index` and every property type and endpoint that refers to it. References are only
 * moved when no other model shares the old name; otherwise they can't be told apart, so they stay put.
 */
export function renameModel(document: DesignDocument, index: number, to: string): DesignDocument {
  const from = document.models[index]?.name ?? "";
  const shared = document.models.some((m, i) => i !== index && m.name === from);
  const swap = (name: string | null | undefined) => (!shared && name === from ? to : name);
  return {
    ...document,
    models: document.models.map((m, i) => ({
      ...m,
      name: i === index ? to : m.name,
      properties: m.properties.map((p) => ({ ...p, type: swap(p.type)! })),
    })),
    endpoints: document.endpoints.map((e) => ({ ...e, requestModel: swap(e.requestModel), responseModel: swap(e.responseModel) })),
  };
}

/** Removes a model, clearing endpoint references to it (properties that use it are left for the server to flag). */
export function removeModel(document: DesignDocument, index: number): DesignDocument {
  const name = document.models[index]?.name ?? "";
  const shared = document.models.some((m, i) => i !== index && m.name === name);
  const clear = (model: string | null | undefined) => (!shared && model === name ? null : model);
  return {
    ...document,
    models: document.models.filter((_, i) => i !== index),
    endpoints: document.endpoints.map((e) => ({
      ...e,
      requestModel: clear(e.requestModel),
      responseModel: clear(e.responseModel),
      responseIsList: clear(e.responseModel) == null ? false : e.responseIsList,
    })),
  };
}

const routeParameter = /\{([a-z][A-Za-z0-9]*)\}/g;

/**
 * Keeps path parameters in step with the route: adds one for each new {name} (a guid when it's called id
 * or ends in Id, otherwise a string) and drops ones the route no longer has. Query parameters are untouched.
 */
export function withRoute(endpoint: EndpointDefinition, route: string): EndpointDefinition {
  const names = [...route.matchAll(routeParameter)].map((m) => m[1] ?? "");
  const existing = new Map(endpoint.parameters.filter((p) => p.in === "path").map((p) => [p.name, p]));
  const path: ParameterDefinition[] = names.map(
    (name) => existing.get(name) ?? { name, in: "path", type: name === "id" || name.endsWith("Id") ? "guid" : "string", required: true },
  );
  return { ...endpoint, route, parameters: [...path, ...endpoint.parameters.filter((p) => p.in === "query")] };
}

/** "OrderItem" -> "order-items". */
function routeSegment(model: string): string {
  return plural(model).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}

function plural(word: string): string {
  if (/[^aeiou]y$/.test(word)) return `${word.slice(0, -1)}ies`;
  if (/(s|x|z|ch|sh)$/.test(word)) return `${word}es`;
  return `${word}s`;
}

/**
 * The five usual endpoints for a model: list, get, create, update and delete under /api/{plural}.
 * Create and update read `{Model}Input` when the design has one, otherwise the model itself.
 * Endpoints whose names already exist are skipped, so running it twice adds nothing.
 */
export function addCrudEndpoints(document: DesignDocument, model: string): DesignDocument {
  const resource = plural(model);
  const base = `/api/${routeSegment(model)}`;
  const input = document.models.some((m) => m.name === `${model}Input`) ? `${model}Input` : model;
  const id: ParameterDefinition = { name: "id", in: "path", type: "guid", required: true };
  const common = { resource, summary: null, requestModel: null, responseModel: model, responseIsList: false, requiresAuth: false };

  const crud: EndpointDefinition[] = [
    { ...common, method: "GET", route: base, name: `List${resource}`, summary: `List ${resource.toLowerCase()}.`, parameters: [], responseIsList: true, successStatus: 200, errorStatuses: [] },
    { ...common, method: "GET", route: `${base}/{id}`, name: `Get${model}`, summary: `Get one ${model}.`, parameters: [id], successStatus: 200, errorStatuses: [404] },
    { ...common, method: "POST", route: base, name: `Create${model}`, summary: `Create a ${model}.`, parameters: [], requestModel: input, successStatus: 201, errorStatuses: [400] },
    { ...common, method: "PUT", route: `${base}/{id}`, name: `Update${model}`, summary: `Replace a ${model}.`, parameters: [id], requestModel: input, successStatus: 200, errorStatuses: [400, 404] },
    { ...common, method: "DELETE", route: `${base}/{id}`, name: `Delete${model}`, summary: `Delete a ${model}.`, parameters: [id], responseModel: null, successStatus: 204, errorStatuses: [404] },
  ];

  const taken = new Set(document.endpoints.map((e) => e.name.toLowerCase()));
  return { ...document, endpoints: [...document.endpoints, ...crud.filter((e) => !taken.has(e.name.toLowerCase()))] };
}
