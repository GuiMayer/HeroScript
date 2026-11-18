import { useDeferredValue, useEffect, useId, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ChevronDown, ChevronUp, Plus, X } from 'lucide-react'
import { api, settingPath, type Definition, type Schemas } from './api/client'
import { TextField } from './TextField'
import { array, object, propertyKey, string, type Frame } from './editor-state'
import { fieldLabel, type RuleCopy } from './rule-copy'
import { discriminatedSchemas, resolveSchema, type Schema } from './authoring-schema'
import { stringifyAuthoringJson } from './authoring-json'
import { FreeField, PropertyFields } from './FreeField'
import { DiagnosticField } from './DiagnosticField'
import { isWithin, joinPointer, type DiagnosticFocus } from './diagnostic-target'
import { InheritedField, type InheritedValue } from './InheritedField'

interface Context {
  key: string
  setting: string
  referenceSetting?: string
  focus?: DiagnosticFocus
  inherited?: InheritedValue & { path: string }
  disabled: boolean
  locale: string
  copy: RuleCopy
  refs: Record<string, string>
  schemaRoot?: unknown
  full?: boolean
  reorder?: boolean
  root: Definition
  draft: Frame
  onTyping: (dirty: boolean) => void
  onCreateReference?: (kind: string) => void
  onCreateWritingPackage?: () => void
  canCreate?: (kind: string) => boolean
}

export function SchemaFields({ schema, value, update, context, path, skip }: {
  schema: Schema; value: Definition; update: (value: Definition) => void; context: Context; path: string; skip?: string
}) {
  return <ObjectFields properties={schema.properties ?? {}} value={value} update={update} context={context} path={path} skip={skip} />
}

export function RuleFields({ schema, identity, value, update, context }: {
  schema: unknown; identity?: string; value: Definition; update: (value: Definition) => void; context: Context
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
  const fields = Object.entries(properties).filter(([name]) => name !== skip)
  const basicEffectFields = ['type', 'target', 'effectId', 'statusId', 'targetResource', 'selectionResourceId',
    'operation', 'flatValue', 'formulaValue', 'statusStacks', 'statusDuration', 'cardZoneFlowId', 'cardCount', 'condition', 'chainedEffects']
  const isEffect = context.full && !!properties.effectId
  const primary = isEffect ? basicEffectFields.flatMap((name) => fields.filter(([key]) => name === key)) : fields
  const extra = isEffect ? fields.filter(([name]) => !basicEffectFields.includes(name)) : []
  const dependentReferences = object(object(context.schemaRoot)['x-authoring-dependent-references'])
  const render = ([name, schema]: [string, Schema]) => {
    const key = propertyKey(value, name)
    const fieldPath = joinPointer(path, name)
    const lookupPath = schemaPath(fieldPath)
    const dependent = object(dependentReferences[lookupPath.replaceAll('/children/*', '')])
    const selector = string(dependent.selector)
    const selected = selector ? string(value[propertyKey(value, selector)]) : ''
    const choices = object(dependent.kinds)
    const choice = Object.keys(choices).find((option) => option.toLowerCase() === selected.toLowerCase())
    const reference = choice ? string(choices[choice]) : undefined
    const label = context.full && lookupPath === '/components/*/costs' ? context.copy.costSettings
      : context.full && name === 'costs' && lookupPath.startsWith('/components/*/costs') ? context.copy.resourceCosts
      : fieldLabel(name, context.locale)
    return <Field key={name} label={label} path={fieldPath} referenceKind={reference}
      schema={schema} value={value[key]} update={(next) => {
        const changed = { ...value, [key]: next }
        if (next === undefined) delete changed[key]
        update(changed)
      }} context={context} />
  }
  return <div className="rule-field-group">{primary.map(render)}
    {!!extra.length && <Fold label={context.copy.moreFields} reveal={extra.some(([name]) =>
      context.focus && isWithin(context.focus.path, joinPointer(path, name))) ? context.focus : undefined}>
      <div className="rule-field-group">{extra.map(render)}</div></Fold>}
  </div>
}

function Fold({ label, count, children, initiallyOpen = false, reveal }: {
  label: string; count?: number; children: ReactNode; initiallyOpen?: boolean; reveal?: DiagnosticFocus
}) {
  const [open, setOpen] = useState(initiallyOpen || !!reveal)
  useEffect(() => { if (reveal) setOpen(true) }, [reveal])
  return <details className="component rule-fold" open={open} onToggle={(event) => setOpen(event.currentTarget.open)}>
    <summary>{label}{count !== undefined && <code>{count}</code>}</summary>{open && children}
  </details>
}

function schemaPath(path: string) {
  return path.replace(/\/\d+(?=\/|$)/g, '/*')
}

function schemaShape(schema: Schema, root: unknown) {
  const resolved = resolveSchema(schema, root)
  const variant = resolved.anyOf ?? resolved.oneOf
  const nonNull = variant?.filter((item) => item.type !== 'null') ?? []
  return { shape: nonNull.length === 1 ? resolveSchema(nonNull[0], root) : resolved, variant }
}

function initialValue(schema: Schema, root: unknown): unknown {
  const { shape } = schemaShape(schema, root)
  const types = Array.isArray(shape.type) ? shape.type : [shape.type]
  if (shape.properties || types.includes('object')) return {}
  if (types.includes('array')) return []
  if (types.includes('integer') || types.includes('number')) return 0
  if (types.includes('boolean')) return false
  if (types.includes('string')) return ''
  return shape.enum?.[0] ?? null
}

function Field({ label, path, schema, value, update, context, referenceKind }: {
  label: string; path: string; schema: Schema; value: unknown; update: (value: unknown) => void; context: Context
  referenceKind?: string
}) {
  const focus = context.focus?.path.toLowerCase() === path.toLowerCase() ? context.focus : undefined
  const baseline = context.inherited
  // Array entries have positional paths. Restore their list as a unit after insertions or removals.
  const segments = baseline && path.slice(baseline.path.length + 1).split('/')
  const inherited = baseline && segments && !segments.some((segment) => /^\d+$/.test(segment))
    ? { ...baseline, value: segments.reduce<unknown>((parent, segment) => {
      const key = segment.replaceAll('~1', '/').replaceAll('~0', '~')
      const source = object(parent)
      return source[propertyKey(source, key)]
    }, baseline.value) } : undefined
  return <DiagnosticField focus={focus}>
    <InheritedField {...{ label, value, inherited }} restore={() => update(inherited?.value)}>
      <FieldControl {...{ label, path, schema, value, update, context, referenceKind }} />
    </InheritedField>
  </DiagnosticField>
}

function FieldControl({ label, path, schema, value, update, context, referenceKind }: Parameters<typeof Field>[0]) {
  const { shape, variant } = schemaShape(schema, context.schemaRoot)
  const types = Array.isArray(shape.type) ? shape.type : [shape.type]
  const lookup = schemaPath(path)
  const referencePath = lookup.replaceAll('/chainedEffects/*', '').replaceAll('/conditions/*', '')
  const reference = referenceKind ?? context.refs[lookup] ?? context.refs[referencePath]
  const reveal = context.focus && isWithin(context.focus.path, path) ? context.focus : undefined
  const elementReference = context.refs[`${referencePath}/*`]
  const common = { label, disabled: context.disabled, onTyping: context.onTyping }
  if (reference) return <Reference context={context} label={label} kind={reference} value={string(value)}
    selectionOnly={lookup === '/authoringPackageId'} commit={(id) => update(id || (context.full ? undefined : ''))} />
  if (shape.enum?.length && shape.enum.every((item) => typeof item === 'string' || item === null)) {
    const choices = shape.enum.filter((item): item is string => typeof item === 'string')
    const current = string(value)
    return <label className="field"><span>{label}</span><select value={current} disabled={context.disabled}
      onChange={(event) => update(event.target.value || (context.full ? undefined : null))}>
      <option value="" disabled={!context.full && !types.includes('null') && !variant?.some((item) => item.type === 'null')}>{context.copy.unset}</option>
      {current && !choices.includes(current) && <option value={current}>{current}</option>}
      {choices.map((item) => <option key={item} value={item}>{item}</option>)}
    </select></label>
  }
  if (types.includes('boolean') && context.full)
    return <label className="field"><span>{label}</span><select value={typeof value === 'boolean' ? String(value) : ''}
      disabled={context.disabled} onChange={(event) => update(event.target.value ? event.target.value === 'true' : undefined)}>
      <option value="">{context.copy.unset}</option><option value="true">{context.copy.yes}</option><option value="false">{context.copy.no}</option>
    </select></label>
  if (types.includes('boolean'))
    return typeof value === 'boolean'
      ? <label className="rule-check"><input type="checkbox" disabled={context.disabled} checked={value}
        onChange={(event) => update(event.target.checked)} /><span>{label}</span></label>
      : <label className="field"><span>{label}</span><select value="" disabled={context.disabled}
        onChange={(event) => update(event.target.value === 'true')}>
        <option value="" disabled>{context.copy.unset}</option><option value="true">{context.copy.yes}</option>
        <option value="false">{context.copy.no}</option>
      </select></label>
  if (shape.format === 'uint64') return <TextField {...common}
    value={typeof value === 'number' || typeof value === 'bigint' ? String(value) : ''}
    onCommit={(next) => {
      const text = next.trim()
      if (!text) update(undefined)
      else if (/^-?\d+$/.test(text)) {
        const numeric = Number(text)
        update(Number.isSafeInteger(numeric) ? numeric : BigInt(text))
      }
    }} />
  if (types.includes('integer') || types.includes('number'))
    return <TextField {...common} value={typeof value === 'number' || types.includes('string') && typeof value === 'string' ? String(value) : ''}
      onCommit={(next) => {
        if (next.trim() && Number.isFinite(Number(next))) update(Number(next))
        else if (!next.trim() && context.full) update(undefined)
      }} />
  if (types.includes('string'))
    return <TextField {...common} value={string(value)} multiline={context.full && /\/(expression|formula|failureReason)$/.test(path)}
      onCommit={(next) => update(next || (context.full ? undefined : ''))} />
  if (shape.properties && shape.type !== 'array')
    return <Fold label={label} reveal={reveal} initiallyOpen={context.full && lookup === '/components/*/effect'}>
      <ObjectFields properties={shape.properties} value={object(value)} update={update} context={context} path={path} />
      {context.full && value != null && !context.disabled && <button type="button" className="text-button rule-array-remove"
        onClick={() => update(undefined)}>{context.copy.resetField}</button>}
    </Fold>
  if (types.includes('array') && shape.items && !Array.isArray(shape.items)) {
    const values = array(value)
    const { shape: itemSchema } = schemaShape(shape.items, context.schemaRoot)
    const variants = context.full ? discriminatedSchemas(itemSchema, context.schemaRoot) : undefined
    if (variants?.size) return <Fold label={label} count={values.length} reveal={reveal}>
      <VariantItems values={values} update={update} variants={variants} context={context} path={path} />
    </Fold>
    if (elementReference) return <Fold label={label} count={values.length} reveal={reveal}>
      <div className="rule-reference-list">
        {values.map((item, index) => <div className="rule-reference-row" key={index}>
          <DiagnosticField focus={context.focus?.path.toLowerCase() === joinPointer(path, index).toLowerCase() ? context.focus : undefined}>
            <Reference label={`${label} ${index + 1}`} kind={elementReference} context={context} value={string(item)}
              commit={(id) => update(values.map((value, position) => position === index ? id : value))} />
          </DiagnosticField>
          {!context.disabled && <button type="button" className="icon-button" aria-label={`${context.copy.removeItem}: ${string(item)}`}
            onClick={() => update(values.filter((_, position) => position !== index))}><X size={15} /></button>}
        </div>)}
        {!context.disabled && <Reference label={`${label} +`} kind={elementReference} context={context}
          value="" commit={(id) => {
            const repeatable = array(object(context.schemaRoot)['x-authoring-repeatable-references']).includes(referencePath)
            if (id && (repeatable || !values.includes(id))) update([...values, id])
          }} />}
      </div>
    </Fold>
    if (itemSchema.properties || context.full) return <Fold label={label} count={values.length} reveal={reveal}>
      <div className="rule-array">
        {values.map((item, index) => {
          const row = object(item)
          const name = ['name', 'nodeId', 'choiceId', 'unlockId', 'itemId', 'phaseId', 'edgeId', 'effectId', 'triggerId',
            'optionId', 'paymentId', 'resourceId', 'op', 'stageId', 'bucketId']
            .map((key) => string(row[propertyKey(row, key)]))
            .find(Boolean) || `${label} ${index + 1}`
          const replace = (next: unknown) => update(values.map((current, position) =>
            position === index ? next ?? initialValue(itemSchema, context.schemaRoot) : current))
          const move = (offset: number) => {
            const reordered = [...values]
            const target = index + offset
            reordered[index] = values[target]
            reordered[target] = values[index]
            update(reordered)
          }
          const address = joinPointer(path, index)
          return <Fold key={index} label={name} reveal={context.focus && isWithin(context.focus.path, address) ? context.focus : undefined}>
            {itemSchema.properties ? <ObjectFields properties={itemSchema.properties} value={row} context={context}
              path={address} update={replace} /> : <Field label={context.copy.value} schema={itemSchema} value={item}
              context={context} path={address} update={replace} />}
            {!context.disabled && <div className="property-actions rule-array-remove">
              {context.reorder && <>
                <button type="button" className="icon-button" aria-label={`${context.copy.moveEarlier}: ${name}`}
                  disabled={index === 0} onClick={() => move(-1)}><ChevronUp size={15} /></button>
                <button type="button" className="icon-button" aria-label={`${context.copy.moveLater}: ${name}`}
                  disabled={index === values.length - 1} onClick={() => move(1)}><ChevronDown size={15} /></button>
              </>}
              <button type="button" className="text-button"
                onClick={() => update(values.filter((_, position) => position !== index))}><X size={15} />
                {context.copy.removeItem}
              </button>
            </div>}
          </Fold>
        })}
        {!context.disabled && <button type="button" className="secondary" onClick={() => update([
          ...values, initialValue(itemSchema, context.schemaRoot),
        ])}>
          <Plus size={15} />{context.copy.addItem}
        </button>}
      </div>
    </Fold>
  }
  if (context.full && types.includes('object') && typeof shape.additionalProperties === 'object' &&
      Object.keys(shape.additionalProperties).length) {
    const entrySchema = shape.additionalProperties
    const keyReference = context.refs[`${lookup}/@key`]
    const keyLabel = keyReference === 'resources' ? fieldLabel('resourceId', context.locale)
      : keyReference === 'card-rarities' ? fieldLabel('rarity', context.locale) : context.copy.propertyName
    return <Fold label={label} count={Object.keys(object(value)).length} reveal={reveal}>
      <PropertyFields value={object(value)} update={update} copy={context.copy} disabled={context.disabled} onTyping={context.onTyping}
        keyInput={keyReference ? (key, change) => <Reference context={context} label={keyLabel}
          kind={keyReference} value={key} commit={change} immediate /> : undefined}
        typed={{ initial: initialValue(entrySchema, context.schemaRoot), render: (key, entry, replace) =>
          <Field label={key} path={joinPointer(path, key)} schema={entrySchema} value={entry} update={replace} context={context} /> }} />
    </Fold>
  }
  if (context.full) return <FreeField label={label} value={value} update={update} copy={context.copy} disabled={context.disabled}
    onTyping={context.onTyping} dictionary={types.includes('object')} />
  return <Fold label={label} count={Array.isArray(value) ? value.length : undefined}>
    <p className="muted">{context.copy.editAdvanced}</p>
    {value !== undefined && <pre className="code-view">{stringifyAuthoringJson(value, 2)}</pre>}
  </Fold>
}

function VariantItems({ values, update, variants, context, path }: {
  values: unknown[]; update: (value: unknown[]) => void; variants: Map<string, Schema>; context: Context; path: string
}) {
  const [selected, setSelected] = useState(() => variants.keys().next().value ?? '')
  return <div className="rule-array">
    {values.map((value, index) => {
      const item = object(value)
      const kind = string(item.type)
      const properties = variants.get(kind)?.properties
      const identification = string(item[propertyKey(item, 'componentId')])
      const label = kind ? fieldLabel(kind, context.locale) : context.copy.componentType
      const address = joinPointer(path, index)
      return <Fold label={identification ? `${label} · ${identification}` : label} key={index}
        reveal={context.focus && isWithin(context.focus.path, address) ? context.focus : undefined}>
        {properties && <ObjectFields properties={properties} value={item} path={address} skip="type" context={context}
          update={(next) => update(values.map((current, position) => position === index ? { type: kind, ...next } : current))} />}
        {!context.disabled && <button type="button" className="text-button rule-array-remove"
          onClick={() => update(values.filter((_, position) => position !== index))}><X size={15} />{context.copy.removeItem}</button>}
      </Fold>
    })}
    {!context.disabled && <div className="rule-reference-row">
      <label className="field"><span>{context.copy.componentType}</span>
        <select value={selected} onChange={(event) => setSelected(event.target.value)}>
          {[...variants.keys()].map((kind) => <option key={kind} value={kind}>{fieldLabel(kind, context.locale)}</option>)}
        </select>
      </label>
      <button type="button" className="secondary" onClick={() => update([...values, { type: selected }])}>
        <Plus size={15} />{context.copy.addComponent}
      </button>
    </div>}
  </div>
}

function Reference({ context, kind, label, value, commit, immediate = false, selectionOnly = false }: {
  context: Context; kind: string; label: string; value: string; commit: (value: string) => void
  immediate?: boolean
  selectionOnly?: boolean
}) {
  const id = useId()
  const [input, setInput] = useState<string | null>(null)
  const search = useDeferredValue(immediate ? input ?? '' : input === value ? '' : input ?? '')
  const local = kind.startsWith('local:')
  const query = useQuery({
    queryKey: ['authoring', context.setting, 'references', kind, search, context.referenceSetting],
    enabled: input !== null && !local && !context.disabled,
    queryFn: ({ signal }) => api<Schemas['AuthoringReferenceAuthoringPage']>(context.key,
      `${settingPath(context.setting)}/references/${encodeURIComponent(kind)}?${new URLSearchParams({
        limit: '50', q: search, ...(context.referenceSetting ? { settingId: context.referenceSetting } : {}),
      })}`, { signal }),
  })
  const localPath = kind.slice('local:'.length).split('/')
  const localKey = localPath.length > 1 ? localPath.pop() : undefined
  const localValue = local ? localPath.reduce<unknown>((node, name) => {
    const parent = object(node)
    return parent[propertyKey(parent, name)]
  }, context.root) : undefined
  const source = local ? array(localValue).map((item) => {
    if (!localKey) return string(item)
    const row = object(item)
    return string(row[propertyKey(row, localKey)])
  })
    : (query.data?.data.items ?? []).map((item) => string(item.id))
  const ids = new Set(source)
  for (const { operation } of context.draft) {
    if (operation.kind !== kind) continue
    if (operation.operation === 'create') ids.add(operation.definitionId)
    if (operation.operation === 'delete') ids.delete(operation.definitionId)
  }
  if (selectionOnly) return <div className="rule-reference"><label className="field"><span>{label}</span>
    <select value={value} disabled={context.disabled} onChange={(event) => commit(event.target.value)}>
      <option value="">{context.copy.unset}</option>
      {value && !ids.has(value) && <option value={value}>{value}</option>}
      {[...ids].filter(Boolean).map((id) => <option key={id} value={id}>{id}</option>)}
    </select>
  </label>
    {context.onCreateWritingPackage && <button type="button" className="icon-button" disabled={context.disabled}
      title={context.copy.newWritingPackage} aria-label={context.copy.newWritingPackage}
      onClick={context.onCreateWritingPackage}><Plus size={16} /></button>}
  </div>
  return <div className="rule-reference">
    <label className="field"><span>{label}</span><input value={input ?? value} list={id}
      disabled={context.disabled} onFocus={() => setInput(value)}
      onChange={(event) => {
        setInput(event.target.value)
        if (immediate) commit(event.target.value)
        else context.onTyping(event.target.value !== value)
      }}
      onBlur={() => {
        if (!immediate && input !== null && input !== value) commit(input.trim())
        setInput(null)
        context.onTyping(false)
      }} onKeyDown={(event) => { if (event.key === 'Enter') event.currentTarget.blur() }} />
    </label>
    <datalist id={id}>{[...ids].filter(Boolean).map((id) => <option key={id} value={id} />)}</datalist>
    {context.canCreate?.(kind) && !context.disabled && <button type="button" className="icon-button" title={context.copy.createReference}
      aria-label={`${context.copy.createReference}: ${label}`} onClick={() => context.onCreateReference?.(kind)}><Plus size={16} /></button>}
    {query.isError && <button type="button" className="text-button" onMouseDown={(event) => event.preventDefault()}
      onClick={() => void query.refetch()}>{context.copy.retryReferences}</button>}
  </div>
}
