import { useDeferredValue, useState } from 'react'
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { ChevronRight, FileText, Layers3, LockKeyhole, Pencil, Plus, RotateCcw, Search, Trash2 } from 'lucide-react'
import { AuthoringActions, AuthoringReceipt } from './AuthoringActions'
import { api, cardPath, settingPath, type Card, type Definition, type Diagnostic, type Schemas } from './api/client'
import { Advanced, Overview, rarityLabel, Rules, titleOf } from './CardFields'
import { cardCopy, type CardCopy } from './card-copy'
import { object, same, string, type Workspace } from './editor-state'
import { preference, type Copy, type Locale } from './i18n'
import { Modal } from './Modal'
import { useCardDrafts } from './use-card-drafts'
import { cardDiagnosticPath, diagnosticCardId, type DiagnosticFocus } from './diagnostic-target'

export interface CardsProps {
  apiKey: string
  setting: string
  writable: boolean
  locale: Locale
  copy: Copy
  workspace: Workspace
}
type Tab = 'overview' | 'rules' | 'advanced'
const tabs: Tab[] = ['overview', 'rules', 'advanced']
type Summary = Schemas['CardAuthoringSummary']

export default function Cards(props: CardsProps) {
  return <CardWorkspace key={props.setting} {...props} />
}

function CardWorkspace({ apiKey, setting, writable, locale, copy: shell, workspace }: CardsProps) {
  const copy = cardCopy[locale]
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState('')
  const [tab, setTab] = useState<Tab>('overview')
  const [typing, setTyping] = useState(false)
  const [focus, setFocus] = useState<DiagnosticFocus>()
  const [customizingId, setCustomizingId] = useState('')
  const [hot, setHot] = useState(() => preference('mode', ['manual', 'hot'], 'manual') === 'hot')
  const [modal, setModal] = useState<'create' | 'delete' | 'discard' | null>(null)
  const requested = useDeferredValue(search.trim())
  const actions = useCardDrafts(workspace, setting, apiKey, hot)
  const { draft } = actions
  const list = useInfiniteQuery({
    queryKey: ['authoring', setting, 'cards', requested], initialPageParam: '',
    queryFn: ({ signal, pageParam }) => api<Schemas['CardAuthoringSummaryAuthoringPage']>(apiKey,
      `${settingPath(setting)}/definitions/cards?${new URLSearchParams({
        q: requested, limit: '30', ...(pageParam ? { cursor: pageParam } : {}),
      })}`, { signal }),
    getNextPageParam: (last) => last.data.nextCursor ?? undefined,
  })
  const summaries = (list.data?.pages ?? []).flatMap((page) => page.data.items ?? [])
  const rows = new Map<string, Summary>()
  for (const item of summaries) if (item.definitionId) rows.set(item.definitionId, item)
  for (const { operation } of draft.frame) {
    if (operation.kind !== 'cards') continue
    const id = operation.definitionId
    if (!rows.has(id) && (id.toLowerCase().includes(requested.toLowerCase()) ||
      titleOf(object(operation.definition), '').toLowerCase().includes(requested.toLowerCase())))
      rows.set(id, { definitionId: id, canEdit: true, canDelete: true })
  }
  const activeId = selected || rows.keys().next().value || ''
  const activeEntry = draft.frame.find((entry) => entry.operation.kind === 'cards' && entry.operation.definitionId === activeId)
  const created = activeEntry?.operation.operation === 'create'
  const deleted = activeEntry?.operation.operation === 'delete'
  const detail = useQuery({
    queryKey: ['authoring', setting, 'card', activeId], enabled: !!activeId && !created,
    queryFn: ({ signal }) => api<Card>(apiKey, cardPath(setting, activeId), { signal }),
  })
  const current = detail.data?.data
  const customizable = !!current?.card?.canCustomize
  const inherited = customizable ? object(current?.source) : undefined
  const effective = customizable ? object(current?.effective) : current ? object(current.source) : undefined
  const customizing = customizable && (customizingId === activeId || !!current?.card?.hasCustomization ||
    activeEntry?.operation.operation === 'customize')
  const value = activeEntry?.operation.operation === 'resetCustomization' ? inherited
    : activeEntry?.operation.definition ? object(activeEntry.operation.definition) : effective
  const listVersion = list.data?.pages[0]?.version ?? list.data?.pages[0]?.data.sourceVersion ?? undefined
  const version = detail.data?.version ?? listVersion
  const editLocked = !writable || !!draft.busy || !!draft.attempt || !!draft.intent
  const canEdit = !editLocked && !deleted && (created || !!current?.card?.canEdit || customizing)
  const canDelete = !editLocked && !deleted && (created || !!current?.card?.canDelete)
  function choose(id: string) {
    setFocus(undefined)
    setSelected(id)
    setTab('overview')
    setTyping(false)
  }
  function update(next: Definition) {
    setFocus(undefined)
    actions.edit({
      operation: { kind: 'cards', operation: created ? 'create' : customizing ? 'customize' : 'update',
        definitionId: activeId, definition: next },
      original: effective,
    }, version)
  }
  function resetCustomization() {
    if (!inherited) return
    setCustomizingId(activeId)
    if (!current?.card?.hasCustomization) {
      update(inherited)
      return
    }
    actions.edit({ operation: { kind: 'cards', operation: 'resetCustomization', definitionId: activeId },
      original: effective }, version)
  }
  function remove() {
    actions.edit({
      operation: { kind: 'cards', operation: 'delete', definitionId: activeId },
      original: current ? object(current.source) : undefined,
    }, version)
    if (created || hot) setSelected('')
    setModal(null)
  }
  function create(id: string, name: string, useTemplate: boolean) {
    if (rows.has(id)) return false
    const template = useTemplate ? value ?? {} : {}
    const next = {
      ...template, cardId: id,
      metadata: { ...object(template.metadata), displayName: name },
    }
    const accepted = actions.edit({ operation: { kind: 'cards', operation: 'create', definitionId: id, definition: next } }, listVersion)
    if (accepted) { choose(id); setSearch(''); setModal(null) }
    return accepted
  }

  function locateError(item: Diagnostic) {
    const id = diagnosticCardId(item)
    if (!id) return undefined
    const entry = draft.frame.find(({ operation }) => operation.kind === 'cards' && operation.definitionId === id)
    const definition = entry?.operation.definition ?? (id === activeId ? value : undefined)
    if (!definition) return undefined
    const path = cardDiagnosticPath(item, object(definition))
    if (!path) return undefined
    return () => {
      setSelected(id)
      setSearch('')
      setTyping(false)
      setTab(path.toLowerCase().startsWith('/components') ? 'rules' : 'overview')
      setFocus({ path })
    }
  }

  const editorFields = value ? { value, inherited, disabled: !canEdit, typing, copy, apiKey, setting, update, focus, onTyping: setTyping } : undefined
  const personalized = !!inherited && !!value && !same(value, inherited)

  return <div className="cards-page">
    <header className="page-heading">
      <div><h1>{shell.cards}</h1><p>{copy.intro}</p></div>
      <button className="primary" disabled={editLocked || !listVersion} onClick={() => setModal('create')}><Plus size={17} />{copy.newCard}</button>
    </header>
    <AuthoringActions actions={actions} copy={copy} version={listVersion} writable={writable}
      hot={hot} setHot={setHot} typing={typing} modal={!!modal} onDiscard={() => setModal('discard')} locateError={locateError} />
    <div className="cards-layout">
      <section className="card-catalog" aria-label={copy.list}>
        <div className="catalog-header"><h2>{copy.list}</h2>
          <label className="with-icon"><Search size={16} /><input type="search" aria-label={copy.search}
            placeholder={copy.search} value={search} onChange={(event) => setSearch(event.target.value)} /></label>
        </div>
        {list.isPending ? <Loading label={shell.loading} count={5} /> : <>
          {list.isError && <p className="catalog-message error" role="alert">{copy.sourceUnavailable} <button
            className="text-button" onClick={() => void actions.refresh()}>{shell.retry}</button></p>}
          {!rows.size && !list.isError && <p className="catalog-message muted">{copy.noCards}</p>}
          <div className="card-list">
            {Array.from(rows, ([id]) => {
              const entry = draft.frame.find((item) => item.operation.kind === 'cards' && item.operation.definitionId === id)
              const isSelected = activeId === id
              const label = titleOf(object(entry?.operation.definition ?? (isSelected ? value : undefined)), id.replaceAll('_', ' '))
              return <button className={`card-row ${isSelected ? 'active' : ''}`} key={id} onClick={() => choose(id)}
                aria-current={isSelected ? 'true' : undefined}>
                <span className="row-icon"><Layers3 size={17} /></span><span className="row-label">
                  <strong>{label}</strong><code>{id}</code></span>
                {entry ? <span className="row-indicator" title={entry.operation.operation === 'delete' ? copy.deletion : copy.draft} />
                  : <ChevronRight className="row-chevron" size={15} />}
              </button>
            })}
          </div>
          {list.hasNextPage && <button className="secondary load-more" disabled={list.isFetchingNextPage} onClick={() => void list.fetchNextPage()}>
            {list.isFetchingNextPage ? shell.loading : copy.loadMore}</button>}
        </>}
      </section>
      <section className="card-detail" aria-label={copy.details}>
        {activeId && !created && detail.isPending ? <Loading label={shell.loading} count={4} /> : value && editorFields ? <>
          <header className="detail-heading">
            <span className="detail-icon"><Layers3 size={25} /></span>
            <div className="detail-title"><h2>{titleOf(value, activeId)}</h2><code>{activeId}</code></div>
            <span className="rarity-pill">{rarityLabel(string(value.rarity) || 'Common', copy)}</span>
            {customizable && !customizing && <button className="secondary" disabled={editLocked}
              onClick={() => setCustomizingId(activeId)}><Pencil size={15} />{copy.customize}</button>}
            {canDelete && <button className="icon-button danger-button" aria-label={copy.deleteCard} title={copy.deleteCard}
              onClick={() => setModal('delete')}><Trash2 size={17} /></button>}
          </header>
          <div className="detail-meta">
            {deleted ? <span className="meta-label error">{copy.deletion}</span>
              : created ? <span className="meta-label">{copy.draft}</span>
              : customizable ? <span className={`meta-label ${personalized ? 'customization-label' : ''}`}>
                {personalized ? copy.customized : copy.inheritedCard}</span>
              : (!writable || !current?.card?.canEdit) && <span className="meta-label"><LockKeyhole size={13} />{copy.inherited}</span>}
            {current?.card?.packageId && <span className="meta-source"><FileText size={14} />{current.card.packageId}</span>}
            {personalized && <button type="button" className="text-button" disabled={!canEdit || typing}
              onClick={resetCustomization}><RotateCcw size={14} />{copy.restoreAll}</button>}
          </div>
          <nav className="detail-tabs" aria-label={copy.details}>
            {tabs.map((name) => <button key={name} aria-current={tab === name ? 'page' : undefined}
              className={tab === name ? 'chosen' : ''} onClick={() => setTab(name)}>{copy[name]}</button>)}
          </nav>
          <div className="detail-content">
            {tab === 'overview' && <Overview key={activeId} {...editorFields} />}
            {tab === 'rules' && <Rules key={activeId} {...editorFields} locale={locale} draft={draft.frame} />}
            {tab === 'advanced' && <Advanced key={activeId} {...editorFields} detail={current} />}
          </div>
        </> : <div className="detail-empty"><span className="empty-icon"><Layers3 size={23} /></span>
          <p>{detail.isError ? copy.sourceUnavailable : copy.selectCard}</p>
          {detail.isError && <button className="secondary" onClick={() => void detail.refetch()}>{shell.retry}</button>}
        </div>}
      </section>
    </div>
    <AuthoringReceipt receipt={draft.receipt} copy={copy} />
    {modal === 'create' && <NewCard close={() => setModal(null)} closeLabel={shell.close} {...{ copy, create }}
      template={value ? titleOf(value, activeId) : ''} hot={hot} />}
    {(modal === 'delete' || modal === 'discard') && <Modal title={modal === 'delete' ? copy.deleteCard : copy.discard}
      closeLabel={shell.close} onClose={() => setModal(null)}>
      <p className="muted">{modal === 'delete' ? copy.deletePrompt : copy.discardPrompt}</p>
      <div className="dialog-actions">
        <button className="secondary" onClick={() => setModal(null)}>{copy.cancel}</button>
        <button className="primary destructive" onClick={() => {
          if (modal === 'delete') remove()
          else { actions.discard(); setSelected(''); setModal(null) }
        }}>{modal === 'delete' ? copy.deleteCard : copy.discard}</button>
      </div>
    </Modal>}
  </div>
}

function Loading({ label, count }: { label: string; count: number }) {
  return <div className="row-loading" aria-label={label} role="status" aria-busy="true">
    {Array.from({ length: count }, (_, index) => <div className="skeleton" key={index} />)}
  </div>
}

function NewCard({ copy, close, closeLabel, create, template, hot }: {
  copy: CardCopy; close: () => void; closeLabel: string; template: string; hot: boolean
  create: (id: string, name: string, useTemplate: boolean) => boolean
}) {
  const [useTemplate, setTemplate] = useState(!!template)
  const [failed, setFailed] = useState(false)
  return <Modal title={copy.newCard} closeLabel={closeLabel} onClose={close}>
    <p className="muted">{hot && !template ? copy.blankManual : copy.createHint}</p>
    <form onSubmit={(event) => {
      event.preventDefault()
      const fields = new FormData(event.currentTarget)
      const id = String(fields.get('id')).trim()
      if (!id || id === '.' || id === '..' || !create(id, String(fields.get('name')).trim(), useTemplate)) setFailed(true)
    }}>
      <label htmlFor="new-card-name">{copy.name}</label>
      <input id="new-card-name" name="name" required autoFocus />
      <label htmlFor="new-card-id">{copy.id}</label>
      <input id="new-card-id" name="id" required maxLength={128} pattern={'[a-zA-Z0-9._\\-]+'} placeholder="example_card" />
      {template && <label className="template-option"><input type="checkbox" checked={useTemplate} disabled={hot}
        onChange={(event) => setTemplate(event.target.checked)} /><span>{copy.template} <strong>{template}</strong></span></label>}
      {failed && <p className="error" role="alert">{copy.duplicate}</p>}
      <div className="dialog-actions"><button type="button" className="secondary" onClick={close}>{copy.cancel}</button>
        <button type="submit" className="primary" disabled={hot && !template}><Plus size={16} />{copy.createCard}</button></div>
    </form>
  </Modal>
}
