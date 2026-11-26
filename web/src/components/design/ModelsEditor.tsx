import { useState } from "react";
import { simpleTypes, type DesignDocument, type ModelDefinition, type PropertyDefinition } from "../../api/designs";
import { Button } from "../ui";
import { LabeledField, ListButton, NumberInput, SmallInput, SmallSelect, TextButton } from "./controls";
import { removeModel as removeModelFrom, renameModel, uniqueName } from "./designEdits";

/** The design's models (DTOs): a list on the left, the selected model's properties on the right. */
export function ModelsEditor({ document, onChange }: { document: DesignDocument; onChange: (document: DesignDocument) => void }) {
  const [selected, setSelected] = useState(0);
  const models = document.models;
  const model = models[Math.min(selected, models.length - 1)];
  const index = model ? models.indexOf(model) : -1;

  const updateModel = (next: ModelDefinition) =>
    onChange({ ...document, models: models.map((m, i) => (i === index ? next : m)) });

  const addModel = () => {
    const name = uniqueName("NewModel", models.map((m) => m.name));
    onChange({ ...document, models: [...models, { name, properties: [{ name: "Id", type: "guid", isList: false, required: true }] }] });
    setSelected(models.length);
  };

  const removeModel = () => {
    onChange(removeModelFrom(document, index));
    setSelected(Math.max(0, index - 1));
  };

  return (
    <div className="grid grid-cols-[14rem_1fr] gap-6">
      <aside className="space-y-1">
        {models.map((m, i) => (
          <ListButton key={i} selected={i === index} onClick={() => setSelected(i)}>
            <span className="truncate font-mono">{m.name || "(unnamed)"}</span>
            <span className="ml-auto text-xs text-zinc-500">{m.properties.length}</span>
          </ListButton>
        ))}
        <div className="pt-2">
          <Button variant="secondary" className="w-full" onClick={addModel}>Add model</Button>
        </div>
      </aside>

      {model ? (
        <section className="min-w-0 space-y-4">
          <div className="grid grid-cols-[16rem_1fr] gap-3">
            <LabeledField label="Name (PascalCase)">
              <SmallInput className="font-mono" value={model.name} onChange={(e) => onChange(renameModel(document, index, e.target.value))} />
            </LabeledField>
            <LabeledField label="Description">
              <SmallInput value={model.description ?? ""} onChange={(e) => updateModel({ ...model, description: e.target.value || null })} />
            </LabeledField>
          </div>

          <PropertiesTable model={model} modelNames={models.map((m) => m.name)} onChange={updateModel} />

          <div className="flex justify-between border-t border-zinc-200 pt-3 dark:border-zinc-800">
            <TextButton onClick={() => updateModel({ ...model, properties: [...model.properties, newProperty(model)] })}>+ Add property</TextButton>
            <TextButton danger onClick={removeModel}>Remove model</TextButton>
          </div>
        </section>
      ) : (
        <p className="text-sm text-zinc-500">No models yet. Models are the JSON shapes your endpoints receive and return, such as CreateOrderRequest.</p>
      )}
    </div>
  );
}

function newProperty(model: ModelDefinition): PropertyDefinition {
  return { name: uniqueName("NewProperty", model.properties.map((p) => p.name)), type: "string", isList: false, required: false };
}

function PropertiesTable({ model, modelNames, onChange }: { model: ModelDefinition; modelNames: string[]; onChange: (model: ModelDefinition) => void }) {
  const [expanded, setExpanded] = useState<number | null>(null);
  const update = (i: number, next: PropertyDefinition) =>
    onChange({ ...model, properties: model.properties.map((p, j) => (j === i ? next : p)) });

  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="text-left text-xs text-zinc-500">
          <th className="pb-1 font-medium">Property</th>
          <th className="pb-1 font-medium">Type</th>
          <th className="pb-1 font-medium">List</th>
          <th className="pb-1 font-medium">Required</th>
          <th className="pb-1 font-medium">Description</th>
          <th />
        </tr>
      </thead>
      <tbody>
        {model.properties.map((property, i) => (
          <PropertyRow
            key={i}
            property={property}
            modelNames={modelNames}
            expanded={expanded === i}
            onToggle={() => setExpanded(expanded === i ? null : i)}
            onChange={(next) => update(i, next)}
            onRemove={() => onChange({ ...model, properties: model.properties.filter((_, j) => j !== i) })}
          />
        ))}
      </tbody>
    </table>
  );
}

function PropertyRow({ property, modelNames, expanded, onToggle, onChange, onRemove }: {
  property: PropertyDefinition;
  modelNames: string[];
  expanded: boolean;
  onToggle: () => void;
  onChange: (property: PropertyDefinition) => void;
  onRemove: () => void;
}) {
  const isString = property.type === "string" && !property.isList;
  const isNumber = ["int", "long", "decimal", "double"].includes(property.type) && !property.isList;
  const rules = describeRules(property);

  // Switching type drops rules that no longer apply, so the server doesn't reject them.
  const changeType = (type: string, isList: boolean) => {
    const stringOk = type === "string" && !isList;
    const numberOk = ["int", "long", "decimal", "double"].includes(type) && !isList;
    onChange({
      ...property, type, isList,
      minLength: stringOk ? property.minLength : null, maxLength: stringOk ? property.maxLength : null,
      pattern: stringOk ? property.pattern : null, format: stringOk ? property.format : null,
      minimum: numberOk ? property.minimum : null, maximum: numberOk ? property.maximum : null,
    });
  };

  return (
    <>
      <tr className="border-t border-zinc-100 align-middle dark:border-zinc-800/60">
        <td className="py-1.5 pr-2"><SmallInput className="font-mono" value={property.name} onChange={(e) => onChange({ ...property, name: e.target.value })} /></td>
        <td className="py-1.5 pr-2">
          <SmallSelect value={property.type} onChange={(e) => changeType(e.target.value, property.isList)}>
            <optgroup label="Types">{simpleTypes.map((t) => <option key={t} value={t}>{t}</option>)}</optgroup>
            {modelNames.length > 0 && <optgroup label="Models">{modelNames.map((m) => <option key={m} value={m}>{m}</option>)}</optgroup>}
          </SmallSelect>
        </td>
        <td className="py-1.5 pr-2"><input type="checkbox" className="accent-indigo-600" checked={property.isList} onChange={(e) => changeType(property.type, e.target.checked)} aria-label="List" /></td>
        <td className="py-1.5 pr-2"><input type="checkbox" className="accent-indigo-600" checked={property.required} onChange={(e) => onChange({ ...property, required: e.target.checked })} aria-label="Required" /></td>
        <td className="py-1.5 pr-2"><SmallInput value={property.description ?? ""} onChange={(e) => onChange({ ...property, description: e.target.value || null })} /></td>
        <td className="whitespace-nowrap py-1.5 text-right">
          {(isString || isNumber) && (
            <button type="button" onClick={onToggle} className="mr-3 text-xs text-indigo-600 hover:underline dark:text-indigo-400">
              {rules || "Rules"}
            </button>
          )}
          <button type="button" onClick={onRemove} className="text-xs text-zinc-400 hover:text-red-600" aria-label={`Remove ${property.name}`}>✕</button>
        </td>
      </tr>
      {expanded && (isString || isNumber) && (
        <tr>
          <td colSpan={6} className="pb-3">
            <div className="flex flex-wrap gap-3 rounded-md bg-zinc-50 p-3 dark:bg-zinc-900">
              {isString && (
                <>
                  <LabeledField label="Min length" className="w-24"><NumberInput min={0} value={property.minLength} onChange={(v) => onChange({ ...property, minLength: v })} /></LabeledField>
                  <LabeledField label="Max length" className="w-24"><NumberInput min={1} value={property.maxLength} onChange={(v) => onChange({ ...property, maxLength: v })} /></LabeledField>
                  <LabeledField label="Format" className="w-28">
                    <SmallSelect value={property.format ?? ""} onChange={(e) => onChange({ ...property, format: (e.target.value || null) as PropertyDefinition["format"] })}>
                      <option value="">Any</option>
                      <option value="email">email</option>
                      <option value="url">url</option>
                    </SmallSelect>
                  </LabeledField>
                  <LabeledField label="Pattern (regex, whole value)" className="min-w-64 flex-1">
                    <SmallInput className="font-mono" value={property.pattern ?? ""} onChange={(e) => onChange({ ...property, pattern: e.target.value || null })} placeholder="^[A-Z]{3}$" />
                  </LabeledField>
                </>
              )}
              {isNumber && (
                <>
                  <LabeledField label="Minimum" className="w-32"><NumberInput value={property.minimum} onChange={(v) => onChange({ ...property, minimum: v })} /></LabeledField>
                  <LabeledField label="Maximum" className="w-32"><NumberInput value={property.maximum} onChange={(v) => onChange({ ...property, maximum: v })} /></LabeledField>
                </>
              )}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

/** A short summary of a property's rules for the table, such as "1–200 chars, email". */
function describeRules(p: PropertyDefinition): string {
  const parts: string[] = [];
  if (p.minLength != null || p.maxLength != null) parts.push(`${p.minLength ?? 0}–${p.maxLength ?? "∞"} chars`);
  if (p.minimum != null || p.maximum != null) parts.push(`${p.minimum ?? "−∞"}..${p.maximum ?? "∞"}`);
  if (p.format) parts.push(p.format);
  if (p.pattern) parts.push("pattern");
  return parts.join(", ");
}

