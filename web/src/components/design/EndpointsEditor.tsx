import { useState } from "react";
import {
  errorStatuses,
  methods,
  simpleTypes,
  statusText,
  successStatuses,
  type DesignDocument,
  type EndpointDefinition,
  type ParameterDefinition,
} from "../../api/designs";
import { Button } from "../ui";
import { Check, LabeledField, ListButton, MethodBadge, SmallInput, SmallSelect, TextButton } from "./controls";
import { addCrudEndpoints, uniqueName, withRoute } from "./designEdits";

/** The design's endpoints, grouped by resource on the left, the selected one's details on the right. */
export function EndpointsEditor({ document, onChange }: { document: DesignDocument; onChange: (document: DesignDocument) => void }) {
  const [selected, setSelected] = useState(0);
  const [crudModel, setCrudModel] = useState("");
  const endpoints = document.endpoints;
  const endpoint = endpoints[Math.min(selected, endpoints.length - 1)];
  const index = endpoint ? endpoints.indexOf(endpoint) : -1;
  const modelNames = document.models.map((m) => m.name);

  const update = (next: EndpointDefinition) => onChange({ ...document, endpoints: endpoints.map((e, i) => (i === index ? next : e)) });

  const addEndpoint = () => {
    const resource = endpoint?.resource ?? "Items";
    const name = uniqueName("NewEndpoint", endpoints.map((e) => e.name));
    const route = endpoint?.route.split("/{")[0] ?? "/api/items";
    onChange({
      ...document,
      endpoints: [...endpoints, { resource, method: "GET", route, name, parameters: [], responseIsList: false, successStatus: 200, errorStatuses: [], requiresAuth: false }],
    });
    setSelected(endpoints.length);
  };

  const addCrud = () => {
    const model = crudModel || modelNames[0];
    if (!model) return;
    onChange(addCrudEndpoints(document, model));
    setSelected(endpoints.length);
  };

  const resources = [...new Set(endpoints.map((e) => e.resource))];

  return (
    <div className="grid grid-cols-[18rem_1fr] gap-6">
      <aside className="space-y-3">
        {resources.map((resource) => (
          <div key={resource}>
            <p className="px-2 pb-1 text-xs font-semibold uppercase tracking-wide text-zinc-500">{resource || "(no resource)"}</p>
            {endpoints.map((e, i) =>
              e.resource === resource ? (
                <ListButton key={i} selected={i === index} onClick={() => setSelected(i)}>
                  <MethodBadge method={e.method} />
                  <span className="truncate font-mono text-xs">{e.route}</span>
                  {e.requiresAuth && <span title="Requires auth" className="ml-auto text-xs text-zinc-400">🔒</span>}
                </ListButton>
              ) : null,
            )}
          </div>
        ))}
        <div className="space-y-2 border-t border-zinc-200 pt-3 dark:border-zinc-800">
          <Button variant="secondary" className="w-full" onClick={addEndpoint}>Add endpoint</Button>
          {modelNames.length > 0 && (
            <div className="flex gap-2">
              <SmallSelect value={crudModel || modelNames[0]} onChange={(e) => setCrudModel(e.target.value)} aria-label="Model for CRUD endpoints">
                {modelNames.map((m) => <option key={m} value={m}>{m}</option>)}
              </SmallSelect>
              <Button variant="secondary" className="h-8 shrink-0" onClick={addCrud} title="Adds list, get, create, update and delete endpoints for the model">
                Add CRUD
              </Button>
            </div>
          )}
        </div>
      </aside>

      {endpoint ? (
        <EndpointForm
          key={index}
          endpoint={endpoint}
          modelNames={modelNames}
          onChange={update}
          onRemove={() => {
            onChange({ ...document, endpoints: endpoints.filter((_, i) => i !== index) });
            setSelected(Math.max(0, index - 1));
          }}
        />
      ) : (
        <p className="text-sm text-zinc-500">
          No endpoints yet. Add one, or add a model first and use Add CRUD to create the usual five endpoints for it.
        </p>
      )}
    </div>
  );
}

function EndpointForm({ endpoint, modelNames, onChange, onRemove }: {
  endpoint: EndpointDefinition;
  modelNames: string[];
  onChange: (endpoint: EndpointDefinition) => void;
  onRemove: () => void;
}) {
  const hasBody = endpoint.method !== "GET" && endpoint.method !== "DELETE";
  const setParameter = (i: number, next: ParameterDefinition) =>
    onChange({ ...endpoint, parameters: endpoint.parameters.map((p, j) => (j === i ? next : p)) });

  return (
    <section className="min-w-0 space-y-5">
      <div className="grid grid-cols-[7rem_1fr] gap-3">
        <LabeledField label="Method">
          <SmallSelect
            value={endpoint.method}
            onChange={(e) => {
              const method = e.target.value;
              const body = method !== "GET" && method !== "DELETE";
              onChange({ ...endpoint, method, requestModel: body ? endpoint.requestModel : null });
            }}
          >
            {methods.map((m) => <option key={m}>{m}</option>)}
          </SmallSelect>
        </LabeledField>
        <LabeledField label="Route (use {name} for route parameters)">
          <SmallInput className="font-mono" value={endpoint.route} onChange={(e) => onChange(withRoute(endpoint, e.target.value))} />
        </LabeledField>
      </div>

      <div className="grid grid-cols-3 gap-3">
        <LabeledField label="Name (handler and operationId)">
          <SmallInput className="font-mono" value={endpoint.name} onChange={(e) => onChange({ ...endpoint, name: e.target.value })} />
        </LabeledField>
        <LabeledField label="Resource (groups endpoints)">
          <SmallInput className="font-mono" value={endpoint.resource} onChange={(e) => onChange({ ...endpoint, resource: e.target.value })} />
        </LabeledField>
        <LabeledField label="Summary">
          <SmallInput value={endpoint.summary ?? ""} onChange={(e) => onChange({ ...endpoint, summary: e.target.value || null })} />
        </LabeledField>
      </div>

      <div>
        <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-zinc-500">Parameters</h3>
        {endpoint.parameters.length === 0 ? (
          <p className="text-sm text-zinc-500">None. Route parameters appear here when you add {"{name}"} to the route.</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-zinc-500">
                <th className="pb-1 font-medium">Name</th>
                <th className="pb-1 font-medium">In</th>
                <th className="pb-1 font-medium">Type</th>
                <th className="pb-1 font-medium">Required</th>
                <th className="pb-1 font-medium">Description</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {endpoint.parameters.map((p, i) => {
                const isPath = p.in === "path";
                return (
                  <tr key={i} className="border-t border-zinc-100 dark:border-zinc-800/60">
                    <td className="py-1.5 pr-2">
                      <SmallInput className="font-mono" value={p.name} disabled={isPath} title={isPath ? "Rename it in the route" : undefined} onChange={(e) => setParameter(i, { ...p, name: e.target.value })} />
                    </td>
                    <td className="py-1.5 pr-2 text-zinc-500">{p.in}</td>
                    <td className="py-1.5 pr-2">
                      <SmallSelect value={p.type} onChange={(e) => setParameter(i, { ...p, type: e.target.value })}>
                        {simpleTypes.map((t) => <option key={t}>{t}</option>)}
                      </SmallSelect>
                    </td>
                    <td className="py-1.5 pr-2">
                      <input type="checkbox" className="accent-indigo-600" checked={isPath || p.required} disabled={isPath} onChange={(e) => setParameter(i, { ...p, required: e.target.checked })} aria-label="Required" />
                    </td>
                    <td className="py-1.5 pr-2">
                      <SmallInput value={p.description ?? ""} onChange={(e) => setParameter(i, { ...p, description: e.target.value || null })} />
                    </td>
                    <td className="py-1.5 text-right">
                      {!isPath && (
                        <button type="button" className="text-xs text-zinc-400 hover:text-red-600" aria-label={`Remove ${p.name}`}
                          onClick={() => onChange({ ...endpoint, parameters: endpoint.parameters.filter((_, j) => j !== i) })}>✕</button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
        <div className="mt-1">
          <TextButton
            onClick={() => onChange({
              ...endpoint,
              parameters: [...endpoint.parameters, { name: uniqueName("query", endpoint.parameters.map((p) => p.name)), in: "query", type: "string", required: false }],
            })}
          >
            + Add query parameter
          </TextButton>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-6">
        <div className="space-y-2">
          <h3 className="text-xs font-semibold uppercase tracking-wide text-zinc-500">Request body</h3>
          <SmallSelect value={endpoint.requestModel ?? ""} disabled={!hasBody} onChange={(e) => onChange({ ...endpoint, requestModel: e.target.value || null })}>
            <option value="">{hasBody ? "No body" : `No body (${endpoint.method} doesn't take one)`}</option>
            {modelNames.map((m) => <option key={m}>{m}</option>)}
          </SmallSelect>
        </div>
        <div className="space-y-2">
          <h3 className="text-xs font-semibold uppercase tracking-wide text-zinc-500">Success response</h3>
          <div className="flex gap-2">
            <SmallSelect
              className="w-40"
              value={endpoint.successStatus}
              onChange={(e) => {
                const status = Number(e.target.value);
                onChange({ ...endpoint, successStatus: status, ...(status === 204 ? { responseModel: null, responseIsList: false } : {}) });
              }}
            >
              {successStatuses.map((s) => <option key={s} value={s}>{s} {statusText[s]}</option>)}
            </SmallSelect>
            <SmallSelect
              value={endpoint.responseModel ?? ""}
              disabled={endpoint.successStatus === 204}
              onChange={(e) => onChange({ ...endpoint, responseModel: e.target.value || null, responseIsList: e.target.value ? endpoint.responseIsList : false })}
            >
              <option value="">No body</option>
              {modelNames.map((m) => <option key={m}>{m}</option>)}
            </SmallSelect>
          </div>
          <Check label="Returns a list" checked={endpoint.responseIsList} disabled={!endpoint.responseModel} onChange={(v) => onChange({ ...endpoint, responseIsList: v })} />
        </div>
      </div>

      <div className="space-y-2">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-zinc-500">Error responses (ProblemDetails)</h3>
        <div className="flex flex-wrap gap-4">
          {errorStatuses.map((s) => (
            <Check
              key={s}
              label={`${s} ${statusText[s]}`}
              checked={endpoint.errorStatuses.includes(s)}
              onChange={(on) => onChange({ ...endpoint, errorStatuses: on ? [...endpoint.errorStatuses, s].sort() : endpoint.errorStatuses.filter((x) => x !== s) })}
            />
          ))}
        </div>
      </div>

      <div className="flex items-center justify-between border-t border-zinc-200 pt-3 dark:border-zinc-800">
        <Check
          label="Requires authentication (JWT bearer)"
          checked={endpoint.requiresAuth}
          onChange={(on) => onChange({
            ...endpoint,
            requiresAuth: on,
            errorStatuses: on && !endpoint.errorStatuses.includes(401) ? [...endpoint.errorStatuses, 401].sort() : endpoint.errorStatuses,
          })}
        />
        <TextButton danger onClick={onRemove}>Remove endpoint</TextButton>
      </div>
    </section>
  );
}
