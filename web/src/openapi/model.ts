/**
 * Read-only helpers over an OpenAPI 3.x document, for the endpoint explorer and schema viewer.
 *
 * Scalar renders the full reference; these helpers only build the indexes Scalar doesn't have:
 * a filterable endpoint list and the "which schemas and endpoints use this schema" relationships.
 * Only local references (#/...) are followed. External references are shown as their URL.
 */

// The document is user-supplied JSON, so every field is optional and loosely typed.
export type JsonObject = Record<string, unknown>;

export interface OpenApiDocument extends JsonObject {
  openapi?: string;
  info?: { title?: string; version?: string; description?: string };
  paths?: Record<string, JsonObject>;
  components?: { schemas?: Record<string, Schema>; securitySchemes?: Record<string, JsonObject> } & JsonObject;
  security?: SecurityRequirement[];
  tags?: { name: string; description?: string }[];
}

export interface Schema extends JsonObject {
  $ref?: string;
  type?: string | string[];
  format?: string;
  description?: string;
  properties?: Record<string, Schema>;
  required?: string[];
  items?: Schema;
  additionalProperties?: boolean | Schema;
  allOf?: Schema[];
  oneOf?: Schema[];
  anyOf?: Schema[];
  enum?: unknown[];
  nullable?: boolean;
}

type SecurityRequirement = Record<string, string[]>;

export const httpMethods = ["get", "post", "put", "patch", "delete", "head", "options", "trace"] as const;
export type HttpMethod = (typeof httpMethods)[number];

export interface Parameter {
  name: string;
  in: string;
  required: boolean;
  description?: string;
  schema?: Schema;
  deprecated: boolean;
}

export interface MediaTypeEntry {
  contentType: string;
  schema?: Schema;
}

export interface ResponseEntry {
  status: string;
  description?: string;
  content: MediaTypeEntry[];
}

export interface Operation {
  /** Unique within the document: "get /pets/{id}". */
  key: string;
  method: HttpMethod;
  path: string;
  operationId?: string;
  summary?: string;
  description?: string;
  tags: string[];
  deprecated: boolean;
  parameters: Parameter[];
  requestBody?: { required: boolean; description?: string; content: MediaTypeEntry[] };
  responses: ResponseEntry[];
  /**
   * Each entry is one way to authenticate (OpenAPI ORs the entries and ANDs the schemes inside one).
   * Empty means no authentication. An entry with no schemes means authentication is optional.
   */
  security: string[][];
}

const isObject = (value: unknown): value is JsonObject =>
  typeof value === "object" && value !== null && !Array.isArray(value);

/** Follows a local "#/a/b" reference, decoding ~1 and ~0 as JSON Pointer requires. */
export function resolvePointer(document: OpenApiDocument, ref: string): unknown {
  if (!ref.startsWith("#/")) return undefined;
  let node: unknown = document;
  for (const raw of ref.slice(2).split("/")) {
    const key = decodeURIComponent(raw).replace(/~1/g, "/").replace(/~0/g, "~");
    if (!isObject(node) && !Array.isArray(node)) return undefined;
    node = (node as JsonObject)[key];
  }
  return node;
}

/** Resolves a $ref object (parameter, response, request body) to its target. Gives up after a few hops on cycles. */
function deref(document: OpenApiDocument, value: unknown): JsonObject | undefined {
  let node = value;
  for (let hop = 0; hop < 10 && isObject(node) && typeof node.$ref === "string"; hop++) {
    node = resolvePointer(document, node.$ref);
  }
  return isObject(node) ? node : undefined;
}

const schemaPrefix = "#/components/schemas/";

/** The component name a $ref points at, such as "Pet" for "#/components/schemas/Pet". */
export function schemaRefName(ref: string | undefined): string | undefined {
  return ref?.startsWith(schemaPrefix) ? decodeURIComponent(ref.slice(schemaPrefix.length)).replace(/~1/g, "/").replace(/~0/g, "~") : undefined;
}

function readContent(content: unknown): MediaTypeEntry[] {
  if (!isObject(content)) return [];
  return Object.entries(content).map(([contentType, media]) => ({
    contentType,
    schema: isObject(media) && isObject(media.schema) ? (media.schema as Schema) : undefined,
  }));
}

function readParameters(document: OpenApiDocument, list: unknown): Parameter[] {
  if (!Array.isArray(list)) return [];
  return list.flatMap((item) => {
    const p = deref(document, item);
    if (!p || typeof p.name !== "string" || typeof p.in !== "string") return [];
    return [{
      name: p.name,
      in: p.in,
      required: p.required === true || p.in === "path",
      description: typeof p.description === "string" ? p.description : undefined,
      schema: isObject(p.schema) ? (p.schema as Schema) : undefined,
      deprecated: p.deprecated === true,
    }];
  });
}

function readSecurity(value: unknown): string[][] | undefined {
  if (!Array.isArray(value)) return undefined;
  return value.filter(isObject).map((requirement) => Object.keys(requirement));
}

/** Every operation in the document, in document order. */
export function listOperations(document: OpenApiDocument): Operation[] {
  const rootSecurity = readSecurity(document.security) ?? [];
  const operations: Operation[] = [];

  for (const [path, rawItem] of Object.entries(document.paths ?? {})) {
    const pathItem = deref(document, rawItem);
    if (!pathItem) continue;
    const pathParameters = readParameters(document, pathItem.parameters);

    for (const method of httpMethods) {
      const op = pathItem[method];
      if (!isObject(op)) continue;

      // Operation-level parameters override path-level ones with the same name and location.
      const own = readParameters(document, op.parameters);
      const inherited = pathParameters.filter((p) => !own.some((o) => o.name === p.name && o.in === p.in));
      const body = deref(document, op.requestBody);
      const responses = isObject(op.responses) ? op.responses : {};

      operations.push({
        key: `${method} ${path}`,
        method,
        path,
        operationId: typeof op.operationId === "string" ? op.operationId : undefined,
        summary: typeof op.summary === "string" ? op.summary : undefined,
        description: typeof op.description === "string" ? op.description : undefined,
        tags: Array.isArray(op.tags) ? op.tags.filter((t): t is string => typeof t === "string") : [],
        deprecated: op.deprecated === true,
        parameters: [...inherited, ...own],
        requestBody: body && {
          required: body.required === true,
          description: typeof body.description === "string" ? body.description : undefined,
          content: readContent(body.content),
        },
        responses: Object.entries(responses).map(([status, raw]) => {
          const response = deref(document, raw);
          return {
            status,
            description: typeof response?.description === "string" ? response.description : undefined,
            content: readContent(response?.content),
          };
        }),
        security: readSecurity(op.security) ?? rootSecurity,
      });
    }
  }
  return operations;
}

/** A short, human label for a security scheme: "Bearer", "API key (header X-Api-Key)", "OAuth 2". */
export function describeSecurityScheme(document: OpenApiDocument, name: string): string {
  const scheme = deref(document, document.components?.securitySchemes?.[name]);
  if (!scheme) return name;
  switch (scheme.type) {
    case "http":
      return typeof scheme.scheme === "string" && scheme.scheme.toLowerCase() === "bearer"
        ? "Bearer token"
        : `HTTP ${String(scheme.scheme ?? "")}`.trim();
    case "apiKey":
      return `API key (${String(scheme.in)} ${String(scheme.name)})`;
    case "oauth2":
      return "OAuth 2";
    case "openIdConnect":
      return "OpenID Connect";
    case "mutualTLS":
      return "Mutual TLS";
    default:
      return name;
  }
}

/** "Bearer token", "None", "Optional", or alternatives joined with "or". */
export function describeSecurity(document: OpenApiDocument, security: string[][]): string {
  if (security.length === 0) return "None";
  if (security.some((requirement) => requirement.length === 0)) return "Optional";
  return security
    .map((requirement) => requirement.map((name) => describeSecurityScheme(document, name)).join(" + "))
    .join(" or ");
}

/** A compact type label: "string (uuid)", "Pet[]", "integer | null", "oneOf Cat, Dog". */
export function typeLabel(schema: Schema | undefined): string {
  if (!schema) return "any";
  const refName = schemaRefName(schema.$ref);
  if (refName) return refName;
  if (schema.$ref) return schema.$ref;

  const variants = schema.oneOf ? ["oneOf", schema.oneOf] as const : schema.anyOf ? ["anyOf", schema.anyOf] as const : undefined;
  if (variants) return `${variants[0]} ${variants[1].map(typeLabel).join(", ")}`;
  if (schema.allOf) return schema.allOf.map(typeLabel).join(" & ");

  const types = (Array.isArray(schema.type) ? schema.type : schema.type ? [schema.type] : []).map((type) =>
    type === "array" ? `${typeLabel(schema.items)}[]` : schema.format && type !== "null" ? `${type} (${schema.format})` : type,
  );
  if (schema.nullable) types.push("null"); // OpenAPI 3.0 style
  if (types.length === 0) return schema.properties ? "object" : schema.enum ? "enum" : "any";
  return types.join(" | ");
}

/** Validation keywords worth showing next to a property, e.g. "maxLength 50", "pattern ^a+$". */
export function constraints(schema: Schema | undefined): string[] {
  if (!schema) return [];
  const result: string[] = [];
  const keywords = [
    "minLength", "maxLength", "pattern", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum",
    "multipleOf", "minItems", "maxItems", "minProperties", "maxProperties",
  ] as const;
  for (const keyword of keywords) {
    const value = schema[keyword];
    // In OpenAPI 3.0 exclusiveMinimum/Maximum are booleans that modify minimum/maximum.
    if (value === true) result.push(keyword);
    else if (value !== undefined && value !== false) result.push(`${keyword} ${String(value)}`);
  }
  if (schema.uniqueItems === true) result.push("unique items");
  if (Array.isArray(schema.enum)) result.push(`one of ${schema.enum.map((v) => JSON.stringify(v)).join(", ")}`);
  if (schema.default !== undefined) result.push(`default ${JSON.stringify(schema.default)}`);
  if (schema.readOnly === true) result.push("read-only");
  if (schema.writeOnly === true) result.push("write-only");
  if (schema.deprecated === true) result.push("deprecated");
  return result;
}

/** Names of the component schemas referenced anywhere inside a schema (not following those references). */
export function referencedSchemas(schema: unknown, found = new Set<string>()): Set<string> {
  if (Array.isArray(schema)) {
    schema.forEach((item) => referencedSchemas(item, found));
  } else if (isObject(schema)) {
    const name = schemaRefName(typeof schema.$ref === "string" ? schema.$ref : undefined);
    if (name) found.add(name);
    Object.values(schema).forEach((value) => referencedSchemas(value, found));
  }
  return found;
}

export interface SchemaEntry {
  name: string;
  schema: Schema;
  /** Schemas this one references. */
  references: string[];
  /** Schemas that reference this one. */
  usedBy: string[];
  /** Operations whose parameters, request body or responses reference this schema directly. */
  usedByOperations: Operation[];
}

/** Every component schema, sorted by name, with its relationships to other schemas and to operations. */
export function listSchemas(document: OpenApiDocument, operations: Operation[]): SchemaEntry[] {
  const schemas = Object.entries(document.components?.schemas ?? {}).filter(([, schema]) => isObject(schema));
  const references = new Map(schemas.map(([name, schema]) => [name, [...referencedSchemas(schema)].filter((r) => r !== name)]));

  const operationRefs = operations.map((operation) => ({
    operation,
    refs: referencedSchemas([
      operation.parameters.map((p) => p.schema),
      operation.requestBody?.content.map((c) => c.schema),
      operation.responses.flatMap((r) => r.content.map((c) => c.schema)),
    ]),
  }));

  return schemas
    .map(([name, schema]) => ({
      name,
      schema,
      references: references.get(name) ?? [],
      usedBy: schemas.filter(([other]) => references.get(other)?.includes(name)).map(([other]) => other),
      usedByOperations: operationRefs.filter((o) => o.refs.has(name)).map((o) => o.operation),
    }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

/**
 * The properties of an object schema, including those it gets through allOf (the usual way C# inheritance
 * shows up in generated documents), with whether each is required.
 */
export function objectProperties(document: OpenApiDocument, schema: Schema): { name: string; schema: Schema; required: boolean }[] {
  const seen = new Set<Schema>();
  const properties = new Map<string, { schema: Schema; required: boolean }>();

  const collect = (node: Schema | undefined) => {
    if (!node || seen.has(node)) return;
    seen.add(node);
    if (node.$ref) {
      collect(resolvePointer(document, node.$ref) as Schema | undefined);
      return;
    }
    node.allOf?.forEach(collect);
    const required = new Set(node.required ?? []);
    for (const [name, property] of Object.entries(node.properties ?? {})) {
      properties.set(name, { schema: property, required: required.has(name) || (properties.get(name)?.required ?? false) });
    }
    for (const name of required) {
      const existing = properties.get(name);
      if (existing) existing.required = true;
    }
  };

  collect(schema);
  return [...properties].map(([name, value]) => ({ name, ...value }));
}
