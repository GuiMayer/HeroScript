import { useDeferredValue, useEffect, useState } from 'react'
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { AlertCircle, CheckCircle2, ChevronDown, ChevronRight, CircleCheck, FileText, Layers3, LockKeyhole, Plus, Redo2, Save, Search, Trash2, Undo2, Zap } from 'lucide-react'
import { api, ApiError, cardPath, settingPath, type Card, type Definition, type Schemas } from './api/client'
import { Advanced, Overview, rarityLabel, Rules, titleOf } from './CardFields'
import { cardCopy, type CardCopy } from './card-copy'
import { object, string, type Workspace } from './editor-state'
import { preference, type Copy, type Locale } from './i18n'
import { Modal } from './Modal'
import { RebaseConflict, useCardDrafts } from './use-card-drafts'

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
    const id = operation.definitionId
    if (!rows.has(id) && (id.toLowerCase().includes(requested.toLowerCase()) ||
      titleOf(object(operation.definition), '').toLowerCase().includes(requested.toLowerCase())))
      rows.set(id, { definitionId: id, canEdit: true, canDelete: true })
  }
  const activeId = selected || rows.keys().next().value || ''
  const activeEntry = draft.frame.find((entry) => entry.operation.definitionId === activeId)
  const created = activeEntry?.operation.operation === 'create'
  const deleted = activeEntry?.operation.operation === 'delete'
  const detail = useQuery({
    queryKey: ['authoring', setting, 'card', activeId], enabled: !!activeId && !created,
    queryFn: ({ signal }) => api<Card>(apiKey, cardPath(setting, activeId), { signal }),
  })
  const current = detail.data?.data
  const value = activeEntry?.operation.definition
    ? object(activeEntry.operation.definition) : current ? object(current.source) : undefined
  const listVersion = list.data?.pages[0]?.version ?? list.data?.pages[0]?.data.sourceVersion ?? undefined
  const version = detail.data?.version ?? listVersion
  const pending = !!draft.frame.length || !!draft.intent
  const editLocked = !writable || !!draft.busy || !!draft.attempt || !!draft.intent
  const canEdit = !editLocked && !deleted && (created || !!current?.card?.canEdit)
  const canDelete = !editLocked && !deleted && (created || !!current?.card?.canDelete)
  const canUndo = !editLocked && (!!draft.past.length || (!draft.frame.length && !!draft.undo.length && !!listVersion))
  const canRedo = !editLocked && (!!draft.future.length || (!draft.frame.length && !!draft.redo.length && !!listVersion))

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (modal || event.defaultPrevented || event.isComposing ||
        (!(event.ctrlKey || event.metaKey)) || event.altKey || event.key.toLowerCase() !== 'z') return
      if (event.target instanceof HTMLElement && event.target.closest('input, textarea, [contenteditable="true"], dialog')) return
      if (event.shiftKey ? canRedo : canUndo) {
        event.preventDefault()
        actions.turn(event.shiftKey ? 'redo' : 'undo', listVersion)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  function choose(id: string) {
    setSelected(id)
    setTab('overview')
    setTyping(false)
  }
  function update(next: Definition) {
    actions.edit({
      operation: { kind: 'cards', operation: created ? 'create' : 'update', definitionId: activeId, definition: next },
      original: current ? object(current.source) : undefined,
    }, version)
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

  const status = draft.busy === 'save' ? copy.saving : draft.busy === 'validate' ? copy.validating :
    draft.busy === 'rebase' ? copy.rebasing : draft.attempt ? copy.waiting :
    pending || typing ? copy.pending : draft.receipt ? copy.saved : copy.noChanges
  const diagnostic = draft.error instanceof ApiError ? draft.error : undefined
  const hasConflict = diagnostic?.status === 412 || draft.error instanceof RebaseConflict
  const diagnostics = diagnostic?.problem.validation?.diagnostics ?? []
  const editorFields = value ? { value, disabled: !canEdit, copy, apiKey, setting, update, onTyping: setTyping } : undefined

  return <div className="cards-page">
    <header className="page-heading">
      <div><h1>{shell.cards}</h1><p>{copy.intro}</p></div>
      <button className="primary" disabled={editLocked || !listVersion} onClick={() => setModal('create')}><Plus size={17} />{copy.newCard}</button>
    </header>
    <section className="actionbar" aria-label={copy.details}>
      <div className="actionbar-left">
        <button className="icon-button" aria-label={copy.undo} title={`${copy.undo} (Ctrl+Z)`} disabled={!canUndo}
          onClick={() => actions.turn('undo', listVersion)}><Undo2 size={18} /></button>
        <button className="icon-button" aria-label={copy.redo} title={`${copy.redo} (Ctrl+Shift+Z)`} disabled={!canRedo}
          onClick={() => actions.turn('redo', listVersion)}><Redo2 size={18} /></button>
        <span className="divider" />
        <label className="mode-control" title={hot ? copy.hot : copy.manual}>
          <Zap size={15} /><select aria-label={copy.mode} value={hot ? 'hot' : 'manual'}
            disabled={!writable || !!draft.busy || pending || typing || !!draft.attempt} onChange={(event) => {
              setHot(event.target.value === 'hot')
              localStorage.setItem('heroscript.editor.mode', event.target.value)
            }}><option value="manual">{copy.manual}</option><option value="hot">{copy.hot}</option></select>
          <ChevronDown size={13} />
        </label>
      </div>
      <span className={`save-status ${draft.error ? 'problem' : draft.receipt && !pending ? 'done' : ''}`} role="status">
        {draft.receipt && !pending ? <CheckCircle2 size={15} /> : <span className="dot" />}{status}
      </span>
      <div className="actionbar-right">
        <button className="secondary" disabled={!writable || (!pending && !typing) || !!draft.busy || !!draft.attempt}
          onClick={() => void actions.validate()}><CircleCheck size={15} />{copy.validate}</button>
        <button className="primary" disabled={!writable || (!pending && !typing) || !!draft.busy}
          onClick={() => void actions.save()}><Save size={15} />{draft.attempt && !draft.busy ? copy.retryWrite : copy.save}</button>
      </div>
    </section>
    {(draft.error || draft.intent || draft.valid || pending) && <div className={`notice ${draft.error ? 'notice-error' : ''}`}>
      <div className="notice-body">
        {draft.error ? <><AlertCircle size={17} /><div><strong>{draft.attempt ? copy.uncertain :
          draft.error instanceof RebaseConflict ? copy.rebaseConflict : hasConflict ? copy.conflict : copy.invalid}</strong>
          {diagnostic?.message && !hasConflict && <p>{diagnostic.message}</p>}
          {!!diagnostics.length && <ul>{diagnostics.map((item, index) => <li key={index}>
            {item.definitionId && <code>{item.definitionId} {item.jsonPointer || ''} </code>}
            {item.parameters?.message || item.code}
          </li>)}</ul>}</div></> : draft.intent ? <><Undo2 size={17} /><strong>{draft.intent.kind === 'undo' ? copy.undoStaged : copy.redoStaged}</strong></>
          : <><CircleCheck size={17} /><strong>{draft.valid ? copy.valid : `${copy.pending} · ${draft.frame.length}`}</strong></>}
      </div>
      {!draft.busy && draft.attempt && <button className="secondary" onClick={() => void actions.checkStatus()}>{copy.checkStatus}</button>}
      {!draft.busy && !draft.attempt && <div className="notice-actions">
        {hasConflict && draft.frame.length > 0 && <button className="secondary" onClick={() => void actions.rebase()}>{copy.rebase}</button>}
        {pending && <button className="text-button" onClick={() => draft.intent ? actions.discard() : setModal('discard')}>
          {draft.intent ? copy.cancelIntent : copy.discard}</button>}
      </div>}
    </div>}
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
              const entry = draft.frame.find((item) => item.operation.definitionId === id)
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
            {canDelete && <button className="icon-button danger-button" aria-label={copy.deleteCard} title={copy.deleteCard}
              onClick={() => setModal('delete')}><Trash2 size={17} /></button>}
          </header>
          <div className="detail-meta">
            {deleted ? <span className="meta-label error">{copy.deletion}</span>
              : created ? <span className="meta-label">{copy.draft}</span>
              : (!writable || !current?.card?.canEdit) && <span className="meta-label"><LockKeyhole size={13} />{copy.inherited}</span>}
            {current?.card?.packageId && <span className="meta-source"><FileText size={14} />{current.card.packageId}</span>}
          </div>
          <nav className="detail-tabs" aria-label={copy.details}>
            {tabs.map((name) => <button key={name} aria-current={tab === name ? 'page' : undefined}
              className={tab === name ? 'chosen' : ''} onClick={() => setTab(name)}>{copy[name]}</button>)}
          </nav>
          <div className="detail-content">
            {tab === 'overview' && <Overview key={activeId} {...editorFields} />}
            {tab === 'rules' && <Rules key={activeId} {...editorFields} />}
            {tab === 'advanced' && <Advanced key={activeId} {...editorFields} detail={current} />}
          </div>
        </> : <div className="detail-empty"><span className="empty-icon"><Layers3 size={23} /></span>
          <p>{detail.isError ? copy.sourceUnavailable : copy.selectCard}</p>
          {detail.isError && <button className="secondary" onClick={() => void detail.refetch()}>{shell.retry}</button>}
        </div>}
      </section>
    </div>
    {draft.receipt && <details className="receipt">
      <summary>{copy.changedFiles} / {copy.revisions}</summary>
      <ul>{draft.receipt.changedFiles?.map((file) => <li key={file}><code>{file}</code></li>)}</ul>
      <dl>{Object.entries(draft.receipt.activeRevisions ?? {}).map(([name, revision]) =>
        <div key={name}><dt>{name}</dt><dd><code>{revision}</code></dd></div>)}</dl>
    </details>}
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
