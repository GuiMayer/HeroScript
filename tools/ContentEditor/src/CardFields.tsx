import { useDeferredValue, useEffect, useId, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Braces, CircleHelp, Layers3, Plus, Search, Tag, X } from 'lucide-react'
import { api, settingPath, type Card, type Definition, type Schemas } from './api/client'
import type { CardCopy } from './card-copy'
import { array, object, string } from './editor-state'

export const rarities = ['Common', 'Uncommon', 'Rare', 'Legendary'] as const
export function rarityLabel(value: string, copy: CardCopy) {
  const index = rarities.findIndex((rarity) => rarity.toLowerCase() === value.toLowerCase())
  return [copy.common, copy.uncommon, copy.rare, copy.legendary][index] ?? value
}

export function titleOf(definition: Definition, fallback: string) {
  return string(object(definition.metadata).displayName) || fallback
}

export function TextField({ label, value, disabled, multiline = false, onCommit, onTyping }: {
  label: string; value: string; disabled: boolean; multiline?: boolean; onCommit: (value: string) => void
  onTyping: (dirty: boolean) => void
}) {
  const [text, setText] = useState(value)
  useEffect(() => setText(value), [value])
  const props = {
    value: text, disabled,
    onBlur: () => { onTyping(false); if (text !== value) onCommit(text) },
    onChange: (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
      setText(event.target.value)
      onTyping(event.target.value !== value)
    },
  }
  return <label className="field"><span>{label}</span>{multiline
    ? <textarea {...props} rows={3} />
    : <input {...props}
      onKeyDown={(event) => { if (event.key === 'Enter') event.currentTarget.blur() }} />}</label>
}

function TagPicker({ tags, add, remove, disabled, apiKey, setting, copy }: {
  tags: string[]; add: (tag: string) => void; remove: (tag: string) => void; disabled: boolean
  apiKey: string; setting: string; copy: CardCopy
}) {
  const [text, setText] = useState('')
  const [open, setOpen] = useState(false)
  const target = useRef<HTMLInputElement>(null)
  const id = useId()
  const query = useDeferredValue(text.trim())
  const suggestions = useQuery({
    queryKey: ['authoring', setting, 'tags', query],
    enabled: open && !disabled,
    queryFn: ({ signal }) => api<Schemas['CardAuthoringTagAuthoringPage']>(apiKey,
      `${settingPath(setting)}/references/tags?limit=20&q=${encodeURIComponent(query)}`, { signal }),
  })
  const known = (suggestions.data?.data.items ?? []).flatMap((tag) => tag.id ? [tag.id] : [])
  const available = known.filter((tag) => !tags.includes(tag))
  const typed = text.trim()
  function choose(tag: string) {
    if (!tag || tags.includes(tag)) return
    add(tag)
    setOpen(false)
    setText('')
  }

  return <>
    <div className="tag-row">{tags.length ? tags.map((tag, index) => <span className="tag-chip" key={`${tag}-${index}`}>
      <Tag size={12} />{tag}{!disabled && <button type="button" onClick={() => remove(tag)} aria-label={`${copy.removeTag}: ${tag}`}><X size={13} /></button>}
    </span>) : <span className="muted">{copy.noTags}</span>}</div>
    {!disabled && <div className="tag-picker" onBlur={(event) => {
      if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false)
    }}>
      <div className="with-icon"><Search size={15} />
        <input ref={target} aria-label={copy.tagSearch} aria-expanded={open} aria-controls={open ? id : undefined}
          placeholder={copy.tagSearch} value={text} onFocus={() => setOpen(true)}
          onChange={(event) => { setOpen(true); setText(event.target.value) }}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && typed) { event.preventDefault(); choose(typed) }
            if (event.key === 'Escape') setOpen(false)
          }} />
        <button className="icon-button" aria-label={copy.addTag} type="button" onClick={() => typed ? choose(typed) : target.current?.focus()}>
          <Plus size={17} />
        </button>
      </div>
      {open && <div className="tag-menu" id={id} aria-busy={suggestions.isFetching}>
        {available.map((tag) => <button type="button" key={tag} onClick={() => choose(tag)}><Tag size={14} />{tag}<small>{copy.existingTag}</small></button>)}
        {typed && !known.includes(typed) && !tags.includes(typed) &&
          <button type="button" onClick={() => choose(typed)}><Plus size={15} /><span>{copy.createTag} <strong>{typed}</strong></span></button>}
        {!available.length && !typed && <span className="muted tag-hint">{copy.tagHint}</span>}
      </div>}
    </div>}
  </>
}

export function Overview({ value, disabled, apiKey, setting, copy, update, onTyping }: EditorFields) {
  const metadata = object(value.metadata)
  const tags = array(value.tags).filter((tag): tag is string => typeof tag === 'string')
  const changeMeta = (key: string, next: string) => update({ ...value, metadata: { ...metadata, [key]: next } })
  return <div className="field-sections">
    <section className="form-section">
      <h3>{copy.basics}</h3>
      <div className="field-grid">
        <TextField label={copy.name} value={string(metadata.displayName)} disabled={disabled} onTyping={onTyping} onCommit={(v) => changeMeta('displayName', v)} />
        <label className="field"><span>{copy.rarity}</span>
          <select value={rarities.find((rarity) => rarity.toLowerCase() === string(value.rarity).toLowerCase()) ?? 'Common'}
            disabled={disabled} onChange={(event) => update({ ...value, rarity: event.target.value })}>
            {rarities.map((rarity) => <option key={rarity} value={rarity}>{rarityLabel(rarity, copy)}</option>)}
          </select>
        </label>
      </div>
      <TextField label={copy.description} value={string(metadata.description)} disabled={disabled} multiline onTyping={onTyping}
        onCommit={(v) => changeMeta('description', v)} />
    </section>
    <section className="form-section">
      <h3>{copy.tags}</h3><p className="muted">{copy.tagHint}</p>
      <TagPicker tags={tags} add={(tag) => update({ ...value, tags: [...array(value.tags), tag] })}
        remove={(tag) => update({ ...value, tags: array(value.tags).filter((item) => item !== tag) })}
        {...{ disabled, apiKey, setting, copy }} />
    </section>
  </div>
}

export function Rules({ value, disabled, copy, update, onTyping }: EditorFields) {
  const components = array(value.components)
  const replace = (index: number, element: Definition) =>
    update({ ...value, components: components.map((item, key) => key === index ? element : item) })
  return <section className="form-section component-section">
    <h3>{copy.components}</h3><p className="muted">{copy.componentHint}</p>
    {!components.length && <p className="muted">{copy.noComponents}</p>}
    {components.map((entry, index) => {
      const item = object(entry)
      const kind = string(item.type)
      const name = kind === 'condition' ? copy.condition : kind === 'cost' ? copy.cost : kind
      const costs = object(item.costs)
      const amounts = array(costs.costs)
      return <details className="component" key={`${string(item.componentId)}-${index}`} open={index === 0}>
        <summary><span>{name || copy.rules}</span><code>{string(item.componentId)}</code></summary>
        {kind === 'condition' ? <TextField label={copy.expression} disabled={disabled} multiline onTyping={onTyping}
          value={string(item.expression)} onCommit={(expression) => replace(index, { ...item, expression })} />
          : kind === 'cost' && amounts.length ? amounts.map((cost, costIndex) => {
            const amount = object(cost)
            return <div className="cost-row" key={costIndex}><span>{copy.resource} <code>{string(amount.resourceId)}</code></span>
              <TextField label={copy.amount} value={String(amount.amount ?? '')} disabled={disabled} onTyping={onTyping}
                onCommit={(newAmount) => {
                  const numeric = Number(newAmount)
                  if (!newAmount.trim() || !Number.isFinite(numeric)) return
                  replace(index, { ...item, costs: {
                    ...costs, costs: amounts.map((row, rowIndex) => rowIndex === costIndex ? { ...amount, amount: numeric } : row),
                  } })
                }} />
            </div>
          }) : <pre className="code-view">{JSON.stringify(item, null, 2)}</pre>}
      </details>
    })}
    {!disabled && <details className="component">
      <summary>{copy.addCondition}</summary>
      <form className="condition-form" onSubmit={(event) => {
        event.preventDefault()
        const data = new FormData(event.currentTarget)
        const componentId = String(data.get('id')).trim()
        const expression = String(data.get('expression')).trim()
        if (!componentId || !expression) return
        update({ ...value, components: [...components, { type: 'condition', componentId, expression }] })
        event.currentTarget.reset()
      }}>
        <label className="field"><span>{copy.componentId}</span><input name="id" required /></label>
        <label className="field"><span>{copy.expression}</span><input name="expression" required /></label>
        <button type="submit" className="secondary"><Plus size={15} />{copy.addCondition}</button>
      </form>
    </details>}
  </section>
}

export function Advanced({ value, disabled, copy, update, apiKey, setting, detail }: EditorFields & { detail?: Card }) {
  const serialized = JSON.stringify(value, null, 2)
  const [text, setText] = useState(serialized)
  const [invalid, setInvalid] = useState(false)
  const [schemaOpen, setSchemaOpen] = useState(false)
  useEffect(() => { setText(serialized); setInvalid(false) }, [serialized])
  const schema = useQuery({
    queryKey: ['authoring', setting, 'schema', 'cards'], enabled: schemaOpen,
    queryFn: ({ signal }) => api<Schemas['AuthoringKindSchema']>(apiKey, `${settingPath(setting)}/kinds/cards/schema`, { signal }),
  })
  function apply() {
    try {
      const parsed = object(JSON.parse(text))
      if (parsed.cardId !== value.cardId) throw new Error()
      update(parsed)
      setInvalid(false)
    } catch { setInvalid(true) }
  }
  return <div className="field-sections">
    <section className="form-section">
      <h3>{copy.fullDefinition}</h3><p className="muted">{copy.jsonHelp}</p>
      <textarea className="json-input" aria-label={copy.fullDefinition} spellCheck={false} readOnly={disabled}
        value={text} onChange={(event) => setText(event.target.value)} />
      {invalid && <p className="error" role="alert">{copy.invalidJson}</p>}
      <button className="secondary" disabled={disabled || text === serialized} onClick={apply}><Braces size={15} />{copy.applyJson}</button>
    </section>
    {detail?.source !== undefined && <details className="form-section disclosure">
      <summary>{copy.compare}</summary><pre className="code-view">{JSON.stringify(detail.source, null, 2)}</pre>
    </details>}
    {detail?.effective !== undefined && <details className="form-section disclosure">
      <summary>{copy.effective}</summary><pre className="code-view">{JSON.stringify(detail.effective, null, 2)}</pre>
    </details>}
    {!!detail?.provenance?.length && <details className="form-section disclosure">
      <summary>{copy.provenance}</summary><pre className="code-view">{JSON.stringify(detail.provenance, null, 2)}</pre>
    </details>}
    <details className="form-section disclosure" onToggle={(event) => setSchemaOpen(event.currentTarget.open)}>
      <summary>{copy.schema}</summary>
      {schema.data?.data.schema ? <pre className="code-view">{JSON.stringify(schema.data.data.schema, null, 2)}</pre>
        : <p className="muted">{schema.isError ? copy.sourceUnavailable : '…'}</p>}
    </details>
  </div>
}

export function Preview({ value, copy }: { value: Definition; copy: CardCopy }) {
  return <section className="preview-pane"><div className="card-preview">
    <div className="preview-card-icon"><Layers3 size={31} /></div>
    <strong>{titleOf(value, string(value.cardId))}</strong>
    <span className="preview-rarity">{rarityLabel(string(value.rarity) || 'Common', copy)}</span>
    <p>{string(object(value.metadata).description)}</p>
    <div className="tag-row">{array(value.tags).map((item, index) => <span className="tag-chip" key={index}>{string(item)}</span>)}</div>
  </div><p className="muted"><CircleHelp size={14} />{copy.previewHint}</p></section>
}

interface EditorFields {
  value: Definition; disabled: boolean; copy: CardCopy; apiKey: string; setting: string
  update: (value: Definition) => void
  onTyping: (dirty: boolean) => void
}
