import { useDeferredValue, useId, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Plus, X } from 'lucide-react'
import { api, settingPath, type Definition, type Schemas } from './api/client'
import { TextField } from './CardFields'
import { array, object, string, type Frame } from './editor-state'
import { fieldLabel, type RuleCopy } from './rule-copy'

interface Schema {
  type?: string | string[]
  properties?: Record<string, Schema>
  items?: Schema
  enum?: unknown[]
  anyOf?: Schema[]
  oneOf?: Schema[]
}

interface Context {
  key: string
  setting: string
  disabled: boolean
  locale: string
  copy: RuleCopy
  refs: Record<string, string>
  root: Definition
  draft: Frame
  onTyping: (dirty: boolean) => void
  onCreateReference: (kind: string) => void
  canCreate: (kind: string) => boolean
}

export function RuleFields({ schema, identity, value, update, context }: {
  schema: unknown; identity: string; value: Definition; update: (value: Definition) => void; context: Context
}) {
  const properties = object(schema).properties as Record<string, Schema> | undefined
  if (!properties) return <p className="muted">{context.copy.editAdvanced}</p>
  return <div className="rule-fields">
    <section className="form-section"><h3>{context.copy.definition}</h3>
      <ObjectFields properties={properties} value={value} update={update} context={context} path="" skip={identity} />
    </section>
  </div>
}

function ObjectFields({ properties, value, update, context, path, skip }: {
  properties: Record<string, Schema>; value: Definition; update: (value: Definition) => void
  context: Context; path: string; skip?: string
}) {
  return <div className="rule-field-group">{Object.entries(properties).filter(([name]) => name !== skip).map(([name, schema]) =>
    <Field key={name} label={fieldLabel(name, context.locale)} path={`${path}/${name}`}
      schema={schema} value={value[name]} update={(next) => update({ ...value, [name]: next })} context={context} />,
  )}</div>
}

function Fold({ label, count, children }: { label: string; count?: number; children: ReactNode }) {
  const [open, setOpen] = useState(false)
  return <details className="component rule-fold" onToggle={(event) => setOpen(event.currentTarget.open)}>
    <summary>{label}{count !== undefined && <code>{count}</code>}</summary>{open && children}
  </details>
}

function Field({ label, path, schema, value, update, context }: {
  label: string; path: string; schema: Schema; value: unknown; update: (value: unknown) => void; context: Context
}) {
  const variant = schema.anyOf ?? schema.oneOf
  const nonNull = variant?.filter((item) => item.type !== 'null') ?? []
  const shape = nonNull.length === 1 ? nonNull[0] : schema
  const types = Array.isArray(shape.type) ? shape.type : [shape.type]
  const reference = context.refs[path]
  const common = { label, disabled: context.disabled, onTyping: context.onTyping }
  if (reference) return <Reference context={context} label={label} kind={reference} value={string(value)} commit={update} />
  if (shape.enum?.length && shape.enum.every((item) => typeof item === 'string' || item === null)) {
    const choices = shape.enum.filter((item): item is string => typeof item === 'string')
    const current = string(value)
    return <label className="field"><span>{label}</span><select value={current} disabled={context.disabled}
      onChange={(event) => update(event.target.value || null)}>
      <option value="" disabled={!types.includes('null') && !variant?.some((item) => item.type === 'null')}>{context.copy.unset}</option>
      {current && !choices.includes(current) && <option value={current}>{current}</option>}
      {choices.map((item) => <option key={item} value={item}>{item}</option>)}
    </select></label>
  }
  if (types.includes('boolean'))
    return typeof value === 'boolean'
      ? <label className="rule-check"><input type="checkbox" disabled={context.disabled} checked={value}
        onChange={(event) => update(event.target.checked)} /><span>{label}</span></label>
      : <label className="field"><span>{label}</span><select value="" disabled={context.disabled}
        onChange={(event) => update(event.target.value === 'true')}>
        <option value="" disabled>{context.copy.unset}</option><option value="true">{context.copy.yes}</option>
        <option value="false">{context.copy.no}</option>
      </select></label>
  if (types.includes('integer') || types.includes('number'))
    return <TextField {...common} value={typeof value === 'number' ? String(value) : ''}
      onCommit={(next) => { if (next.trim() && Number.isFinite(Number(next))) update(Number(next)) }} />
  if (types.includes('string'))
    return <TextField {...common} value={string(value)} onCommit={update} />
  if (shape.properties && shape.type !== 'array')
    return <Fold label={label}><ObjectFields properties={shape.properties} value={object(value)}
      update={update} context={context} path={path} /></Fold>
  if (types.includes('array') && shape.items && !Array.isArray(shape.items)) {
    const values = array(value)
    const itemSchema = shape.items
    if (context.refs[`${path}/*`]) return <Fold label={label} count={values.length}>
      <div className="rule-reference-list">
        {values.map((item, index) => <div className="rule-reference-row" key={index}>
          <Reference label={`${label} ${index + 1}`} kind={context.refs[`${path}/*`]} context={context} value={string(item)}
            commit={(id) => update(values.map((value, position) => position === index ? id : value))} />
          {!context.disabled && <button className="icon-button" aria-label={`${context.locale === 'pt-BR' ? 'Remover' : 'Remove'} ${string(item)}`}
            onClick={() => update(values.filter((_, position) => position !== index))}><X size={15} /></button>}
        </div>)}
        {!context.disabled && <Reference label={`${label} +`} kind={context.refs[`${path}/*`]} context={context}
          value="" commit={(id) => { if (id && !values.includes(id)) update([...values, id]) }} />}
      </div>
    </Fold>
    if (itemSchema.properties) return <Fold label={label} count={values.length}>
      {values.map((item, index) => <Fold key={index} label={string(object(item).name) ||
        string(object(item).phaseId) || string(object(item).edgeId) || `${label} ${index + 1}`}>
        <ObjectFields properties={itemSchema.properties!} value={object(item)} context={context} path={`${path}/*`}
          update={(next) => update(values.map((current, position) => position === index ? next : current))} />
      </Fold>)}
      {!values.length && <p className="muted">{context.copy.editAdvanced}</p>}
    </Fold>
  }
  return <Fold label={label} count={Array.isArray(value) ? value.length : undefined}>
    <p className="muted">{context.copy.editAdvanced}</p>
    {value !== undefined && <pre className="code-view">{JSON.stringify(value, null, 2)}</pre>}
  </Fold>
}

function Reference({ context, kind, label, value, commit }: {
  context: Context; kind: string; label: string; value: string; commit: (value: string) => void
}) {
  const id = useId()
  const [input, setInput] = useState<string | null>(null)
  const search = useDeferredValue(input === value ? '' : input ?? '')
  const local = kind.startsWith('local:')
  const query = useQuery({
    queryKey: ['authoring', context.setting, 'references', kind, search],
    enabled: input !== null && !local && !context.disabled,
    queryFn: ({ signal }) => api<Schemas['AuthoringReferenceAuthoringPage']>(context.key,
      `${settingPath(context.setting)}/references/${encodeURIComponent(kind)}?limit=50&q=${encodeURIComponent(search)}`, { signal }),
  })
  const localPath = kind.slice('local:'.length).split('/')
  const source = local ? array(context.root[localPath[0]]).map((item) => string(object(item)[localPath[1]]))
    : (query.data?.data.items ?? []).map((item) => string(item.id))
  const ids = new Set(source)
  for (const { operation } of context.draft) {
    if (operation.kind !== kind) continue
    if (operation.operation === 'create') ids.add(operation.definitionId)
    if (operation.operation === 'delete') ids.delete(operation.definitionId)
  }
  return <div className="rule-reference">
    <label className="field"><span>{label}</span><input value={input ?? value} list={id}
      disabled={context.disabled} onFocus={() => setInput(value)}
      onChange={(event) => { setInput(event.target.value); context.onTyping(event.target.value !== value) }}
      onBlur={() => {
        if (input !== null && input !== value) commit(input.trim())
        setInput(null)
        context.onTyping(false)
      }} onKeyDown={(event) => { if (event.key === 'Enter') event.currentTarget.blur() }} />
    </label>
    <datalist id={id}>{[...ids].filter(Boolean).map((id) => <option key={id} value={id} />)}</datalist>
    {context.canCreate(kind) && !context.disabled && <button type="button" className="icon-button" title={context.copy.createReference}
      aria-label={`${context.copy.createReference}: ${label}`} onClick={() => context.onCreateReference(kind)}><Plus size={16} /></button>}
  </div>
}
