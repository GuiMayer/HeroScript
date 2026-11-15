import { useSyncExternalStore } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, ApiError, cardPath, settingPath, type Card, type Change, type Receipt, type Schemas } from './api/client'
import { clearDraft, confirmed, history, mergeIndependent, object, same, stage, type Draft, type Entry, type Workspace } from './editor-state'

export class RebaseConflict extends Error {}

export function useCardDrafts(workspace: Workspace, setting: string, key: string, hot: boolean) {
  const queryClient = useQueryClient()
  const draft = useSyncExternalStore(workspace.subscribe, () => workspace.get(setting))
  const changes = `${settingPath(setting)}/changes`
  const set = (next: Draft) => workspace.set(setting, next)
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['authoring'] })

  function command(current: Draft): Change {
    const changeId = crypto.randomUUID()
    if (!current.intent) return { changeId, operations: current.frame.map((entry) => entry.operation) }
    return current.intent.kind === 'undo'
      ? { changeId, undoOf: current.intent.target }
      : { changeId, redoOf: current.intent.target }
  }

  async function save() {
    const current = workspace.get(setting)
    if (current.busy || !current.version || (!current.frame.length && !current.intent)) return
    const attempt = current.attempt ?? { request: command(current), version: current.version }
    set({ ...current, busy: 'save', error: undefined, attempt, valid: undefined })
    async function complete(receipt: Receipt) {
      set({ ...confirmed(current, receipt), busy: 'save' })
      await refresh()
      set({ ...workspace.get(setting), busy: undefined })
    }
    try {
      if (current.attempt) {
        try {
          const { data } = await api<Receipt>(key, `${changes}/${attempt.request.changeId}`)
          if (data.state === 'applied') {
            await complete(data)
            return
          }
        } catch (error) {
          if (!(error instanceof ApiError && error.status === 404)) throw error
        }
      }
      const { data } = await api<Receipt>(key, changes, { body: attempt.request, version: attempt.version })
      await complete(data)
    } catch (error) {
      const uncertain = !(error instanceof ApiError) || error.status >= 500 || error.status === 408
      set({ ...current, busy: undefined, error, attempt: uncertain ? attempt : undefined })
      if (error instanceof ApiError && error.status === 412) void refresh()
    }
  }

  async function validate() {
    const current = workspace.get(setting)
    if (current.busy || current.attempt || !current.version || (!current.frame.length && !current.intent)) return
    set({ ...current, busy: 'validate', error: undefined })
    try {
      await api<Schemas['CardAuthoringValidation']>(key, `${changes}/validate`, {
        body: command(current), version: current.version,
      })
      set({ ...current, valid: true, error: undefined })
    } catch (error) {
      set({ ...current, error, valid: false })
      if (error instanceof ApiError && error.status === 412) void refresh()
    }
  }

  async function rebase() {
    const current = workspace.get(setting)
    if (current.busy || current.intent || current.attempt || !current.frame.length) return
    set({ ...current, busy: 'rebase' })
    try {
      const latest = await api<Schemas['CardAuthoringSummaryAuthoringPage']>(key, `${settingPath(setting)}/definitions/cards?limit=1`)
      const version = latest.version ?? latest.data.sourceVersion
      if (!version) throw new RebaseConflict()
      const adjusted = await Promise.all(current.frame.map(async (entry): Promise<Entry | null> => {
        const { operation, original } = entry
        if (operation.operation === 'create') {
          try {
            await api<Card>(key, cardPath(setting, operation.definitionId))
          } catch (error) {
            if (error instanceof ApiError && error.status === 404) return entry
            throw error
          }
          throw new RebaseConflict()
        }
        const latestCard = await api<Card>(key, cardPath(setting, operation.definitionId))
        if (latestCard.version !== version || !original || !latestCard.data.card?.canEdit) throw new RebaseConflict()
        const theirs = object(latestCard.data.source)
        if (operation.operation === 'delete') {
          if (!same(original, theirs) || !latestCard.data.card.canDelete) throw new RebaseConflict()
          return { ...entry, original: theirs }
        }
        const combined = mergeIndependent(original, object(operation.definition), theirs)
        if (!combined) throw new RebaseConflict()
        return same(combined, theirs) ? null : { original: theirs, operation: { ...operation, definition: combined } }
      }))
      if ((await api<unknown>(key, `${settingPath(setting)}/definitions/cards?limit=1`)).version !== version)
        throw new RebaseConflict()
      const frame = adjusted.filter((entry): entry is Entry => !!entry)
      set({ ...current, frame, past: [], future: [], version, busy: undefined, error: undefined, valid: undefined })
    } catch (error) {
      set({ ...current, busy: undefined, error })
    } finally {
      void refresh()
    }
  }

  function edit(entry: Entry, version?: string | null) {
    const current = workspace.get(setting)
    if (current.busy || current.intent || current.attempt || !version) return false
    const next = stage(current, entry, version)
    if (next === current) return false
    set(next)
    if (hot && !current.error) void save()
    return true
  }

  function turn(direction: 'undo' | 'redo', version?: string) {
    const current = workspace.get(setting)
    const next = history(current, direction, version)
    if (current === next) return
    set(next)
    if (hot && next.intent && !current.error) void save()
  }

  function discard() {
    const current = workspace.get(setting)
    if (!current.busy && !current.attempt) set(clearDraft(current))
  }

  return { draft, edit, save, validate, rebase, turn, discard, refresh }
}
