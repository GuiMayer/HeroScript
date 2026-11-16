import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Plus, X } from 'lucide-react'
import { api, settingPath, type Definition, type Schemas } from './api/client'
import { cardComponentSchemas } from './authoring-schema'
import type { CardCopy } from './card-copy'
import { array, object, propertyKey, same, string, type Frame } from './editor-state'
import type { Locale } from './i18n'
import { ruleCopy } from './rule-copy'
import { SchemaFields } from './RuleFields'

const effectKinds = ['effect', 'trigger', 'effect_rule_override'] as const
const ruleKinds = ['cost', 'condition', 'targeting', 'disposition', 'influence'] as const
type ComponentKind = (typeof effectKinds)[number] | (typeof ruleKinds)[number]

type Props = {
  value: Definition; disabled: boolean; copy: CardCopy; apiKey: string; setting: string; locale: Locale; draft: Frame
  update: (value: Definition) => void
  group: 'effects' | 'rules'
}

type Edit = { index: number | null; source: Definition; value: Definition }

export function CardComponents({ value, disabled, copy, apiKey, setting, update, locale, draft, group }: Props) {
  const kinds: readonly ComponentKind[] = group === 'effects' ? effectKinds : ruleKinds
  const [edit, setEdit] = useState<Edit | null>(null)
  const [typing, setTyping] = useState(false)
  const [kind, setKind] = useState<ComponentKind>(kinds[0])
  const schema = useQuery({
    queryKey: ['authoring', setting, 'schema', 'cards'],
    queryFn: ({ signal }) => api<Schemas['AuthoringKindSchema']>(apiKey, `${settingPath(setting)}/kinds/cards/schema`, { signal }),
  })
  const schemaRoot = schema.data?.data.schema
  const definitions = cardComponentSchemas(schemaRoot)
  const componentsKey = propertyKey(value, 'components')
  const components = array(value[componentsKey])
  const labels = { effect: copy.cardEffect, trigger: copy.cardTrigger, effect_rule_override: copy.cardEffectOverride,
    cost: copy.cost, condition: copy.condition, targeting: copy.targeting, disposition: copy.disposition, influence: copy.influence }
  const updateComponents = (next: unknown[]) => update({ ...value, [componentsKey]: next })
  const sourceChanged = !!edit && edit.index !== null && !same(components[edit.index], edit.source)
  const editSchema = edit && definitions.get(string(edit.value.type))
  const id = edit ? string(edit.value[propertyKey(edit.value, 'componentId')]) : ''
  const isDuplicate = !!edit && !!id && components.some((item, index) => {
    const component = object(item)
    return index !== edit.index && component[propertyKey(component, 'componentId')] === id
  })
  const rows = components.flatMap((entry, index) => {
    const item = object(entry)
    return kinds.some((type) => type === item.type) ? [{ item, index }] : []
  })
  function apply() {
    if (!edit || sourceChanged || disabled || !id.trim() || isDuplicate) return
    // Keep the polymorphic discriminator first, as required by the source serializer.
    const candidate = { type: edit.value.type, ...edit.value }
    updateComponents(edit.index === null ? [...components, candidate]
      : components.map((component, index) => index === edit.index ? candidate : component))
    setEdit(null)
  }

  return <section className="form-section component-section">
    <h3>{group === 'effects' ? copy.cardEffects : copy.components}</h3>
    {!rows.length && <p className="muted">{group === 'effects' ? copy.noEffects : copy.noComponents}</p>}
    {schema.isPending && <div className="skeleton" role="status" aria-label={copy.loadingEffects} />}
    {(schema.isError || (schemaRoot && !definitions.size)) && <p className="error" role="alert">{copy.effectsUnavailable}
      {schema.isError && <button type="button" className="text-button" onClick={() => void schema.refetch()}>{copy.retryEffects}</button>}
    </p>}
    {!!rows.length && <div className="effect-list">{rows.map(({ item, index }) => {
      const type = item.type as ComponentKind
      const componentId = string(item[propertyKey(item, 'componentId')])
      const effectType = string(object(item[propertyKey(item, 'effect')]).type)
      return <button type="button" className="secondary effect-row" key={index} aria-pressed={edit?.index === index}
        disabled={!definitions.has(type) || (edit !== null && edit.index !== index)}
        onClick={() => { if (!edit) { setTyping(false); setEdit({ index, source: item, value: item }) } }}>
        <span>{labels[type]}{effectType ? ` · ${effectType}` : ''}</span><code>{componentId}</code>
      </button>
    })}</div>}
    {edit && editSchema ? <div className="component card-effect-form">
      <h4>{labels[edit.value.type as ComponentKind]}</h4>
      <SchemaFields schema={editSchema} path="/components/*" skip="type" value={edit.value}
        update={(next) => setEdit((current) => current && { ...current, value: next })}
        context={{ key: apiKey, setting, disabled: disabled || sourceChanged, locale, copy: ruleCopy[locale], schemaRoot,
          full: true, refs: schema.data?.data.references ?? {}, root: edit.value, draft, onTyping: setTyping }} />
      {sourceChanged && <p className="error" role="alert">{copy.effectChanged}</p>}
      {isDuplicate && <p className="error" role="alert">{copy.duplicateComponent}</p>}
      <div className="effect-actions">
        <button type="button" className="secondary" onClick={() => setEdit(null)}>{disabled ? copy.closeEffect : copy.cancel}</button>
        {!disabled && edit.index !== null && !sourceChanged && <button type="button" className="text-button" onClick={() => {
          updateComponents(components.filter((_, index) => index !== edit.index))
          setEdit(null)
        }}><X size={15} />{copy.removeEffect}</button>}
        {!disabled && <button type="button" className="primary" disabled={sourceChanged ||
          !typing && (!id.trim() || isDuplicate || same(edit.source, edit.value) && edit.index !== null)}
          onClick={apply}>{copy.applyEffect}</button>}
      </div>
    </div> : !disabled && !!definitions.size && <div className="rule-reference-row">
      <label className="field"><span>{copy.effectComponentType}</span><select value={kind}
        onChange={(event) => setKind(event.target.value as ComponentKind)}>
        {kinds.filter((type) => definitions.has(type)).map((type) => <option key={type} value={type}>{labels[type]}</option>)}
      </select></label>
      <button type="button" className="secondary" disabled={!definitions.has(kind)} onClick={() => {
        const source = { type: kind }
        setTyping(false)
        setEdit({ index: null, source, value: source })
      }}><Plus size={15} />{copy.addEffect}</button>
    </div>}
  </section>
}
