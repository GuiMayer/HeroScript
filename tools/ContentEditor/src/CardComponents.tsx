import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Plus, X } from 'lucide-react'
import { api, settingPath, type Definition, type Schemas } from './api/client'
import { cardComponentSchemas } from './authoring-schema'
import type { CardCopy } from './card-copy'
import { array, object, propertyKey, string, type Frame } from './editor-state'
import type { Locale } from './i18n'
import { ruleCopy } from './rule-copy'
import { SchemaFields } from './RuleFields'
import { DiagnosticField } from './DiagnosticField'
import { isWithin, type DiagnosticFocus } from './diagnostic-target'

const effectKinds = ['effect', 'trigger', 'effect_rule_override'] as const
const ruleKinds = ['cost', 'condition', 'targeting', 'disposition', 'influence'] as const
type ComponentKind = (typeof effectKinds)[number] | (typeof ruleKinds)[number]

type Props = {
  value: Definition; disabled: boolean; copy: CardCopy; apiKey: string; setting: string; locale: Locale; draft: Frame
  update: (value: Definition) => void
  onTyping: (typing: boolean) => void
  focus?: DiagnosticFocus
  group: 'effects' | 'rules'
}

export function CardComponents({ value, disabled, copy, apiKey, setting, update, onTyping, focus, locale, draft, group }: Props) {
  const kinds: readonly ComponentKind[] = group === 'effects' ? effectKinds : ruleKinds
  const [selected, setSelected] = useState<number | null>(null)
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
  const edit = selected === null ? undefined : object(components[selected])
  const editSchema = edit && kinds.some((type) => type === edit.type) && definitions.get(string(edit.type))
  const rows = components.flatMap((entry, index) => {
    const item = object(entry)
    return kinds.some((type) => type === item.type) ? [{ item, index }] : []
  })
  const targetIndex = Number(/^\/components\/(\d+)(?:\/|$)/i.exec(focus?.path ?? '')?.[1])
  const target = kinds.some((type) => type === object(components[targetIndex]).type) ? targetIndex : null
  useEffect(() => { if (target !== null) setSelected(target) }, [focus, target])
  const address = `/components/${selected}`
  function add() {
    const ids = new Set(components.map((component) => {
      const item = object(component)
      return string(item[propertyKey(item, 'componentId')])
    }))
    let suffix = 1
    while (ids.has(`${kind}.${suffix}`)) suffix++
    updateComponents([...components, { type: kind, componentId: `${kind}.${suffix}` }])
    setSelected(components.length)
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
      return <button type="button" className="secondary effect-row" key={index} aria-pressed={selected === index}
        disabled={!definitions.has(type)} onClick={() => setSelected(index)}>
        <span>{labels[type]}{effectType ? ` · ${effectType}` : ''}</span><code>{componentId}</code>
      </button>
    })}</div>}
    {edit && editSchema ? <DiagnosticField focus={focus && isWithin(address, focus.path) ? focus : undefined}><div className="component card-effect-form">
      <h4>{labels[edit.type as ComponentKind]}</h4>
      <SchemaFields key={selected} schema={editSchema} path={address} skip="type" value={edit}
        update={(next) => updateComponents(components.map((item, index) => index === selected ? { type: edit.type, ...next } : item))}
        context={{ key: apiKey, setting, disabled, focus, locale, copy: ruleCopy[locale], schemaRoot,
          full: true, refs: schema.data?.data.references ?? {}, root: edit, draft, onTyping }} />
      <div className="effect-actions">
        <button type="button" className="secondary" onClick={() => setSelected(null)}>{copy.closeEffect}</button>
        {!disabled && <button type="button" className="text-button" onClick={() => {
          updateComponents(components.filter((_, index) => index !== selected))
          setSelected(null)
        }}><X size={15} />{copy.removeEffect}</button>}
      </div>
    </div></DiagnosticField> : null}
    {!disabled && !!definitions.size && <div className="rule-reference-row">
      <label className="field"><span>{copy.effectComponentType}</span><select value={kind}
        onChange={(event) => setKind(event.target.value as ComponentKind)}>
        {kinds.filter((type) => definitions.has(type)).map((type) => <option key={type} value={type}>{labels[type]}</option>)}
      </select></label>
      <button type="button" className="secondary" disabled={!definitions.has(kind)} onClick={add}><Plus size={15} />{copy.addEffect}</button>
    </div>}
  </section>
}
