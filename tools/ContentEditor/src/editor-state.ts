import type { Change, Definition, Operation, Receipt } from './api/client'

export interface Entry {
  operation: Operation
  original?: Definition
}
export type Frame = Entry[]
export type Intent = { kind: 'undo' | 'redo'; target: string }
export type Attempt = { request: Change; version: string }
export interface Draft {
  frame: Frame
  past: Frame[]
  future: Frame[]
  undo: string[]
  redo: string[]
  intent?: Intent
  version?: string
  busy?: 'save' | 'validate' | 'rebase'
  attempt?: Attempt
  error?: unknown
  valid?: boolean
  receipt?: Receipt
}

const empty: Draft = { frame: [], past: [], future: [], undo: [], redo: [] }

export class Workspace {
  private drafts = new Map<string, Draft>()
  private listeners = new Set<() => void>()

  get(setting: string) { return this.drafts.get(setting) ?? empty }
  set(setting: string, draft: Draft) {
    this.drafts.set(setting, draft)
    this.listeners.forEach((listener) => listener())
  }
  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }
  hasUnconfirmed() {
    return [...this.drafts.values()].some((draft) => draft.frame.length || draft.intent || draft.busy || draft.attempt)
  }
}

export function object(value: unknown): Definition {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Definition : {}
}
export function string(value: unknown) { return typeof value === 'string' ? value : '' }
export function array(value: unknown): unknown[] { return Array.isArray(value) ? value : [] }

export function propertyKey(value: Definition, name: string) {
  return Object.keys(value).find((key) => key.toLowerCase() === name.toLowerCase()) ?? name
}

export function same(left: unknown, right: unknown): boolean {
  if (Object.is(left, right)) return true
  if (!left || !right || typeof left !== 'object' || typeof right !== 'object') return false
  if (Array.isArray(left) || Array.isArray(right)) {
    return Array.isArray(left) && Array.isArray(right) && left.length === right.length &&
      left.every((value, index) => same(value, right[index]))
  }
  const leftObject = object(left)
  const rightObject = object(right)
  const keys = Object.keys(leftObject)
  return keys.length === Object.keys(rightObject).length &&
    keys.every((key) => Object.hasOwn(rightObject, key) && same(leftObject[key], rightObject[key]))
}

export function stage(draft: Draft, entry: Entry, version: string): Draft {
  const id = entry.operation.definitionId
  const previous = draft.frame.find((value) =>
    value.operation.kind === entry.operation.kind && value.operation.definitionId === id)
  if (same(previous?.operation, entry.operation)) return draft
  const frame = draft.frame.filter((value) => value !== previous)
  const original = previous ? previous.original : entry.original
  const removingNew = previous?.operation.operation === 'create' && entry.operation.operation === 'delete'
  if (!removingNew && (entry.operation.operation !== 'update' || !same(entry.operation.definition, original))) {
    frame.push({ ...entry, original, operation: {
      ...entry.operation,
      operation: previous?.operation.operation === 'create' ? 'create' : entry.operation.operation,
    } })
  }
  return {
    ...draft, frame, past: [...draft.past, draft.frame], future: [],
    version: draft.frame.length ? draft.version ?? version : version,
    error: undefined, valid: undefined,
  }
}

export function history(draft: Draft, direction: 'undo' | 'redo', version?: string): Draft {
  if (draft.busy || draft.attempt || draft.intent) return draft
  const from = direction === 'undo' ? draft.past : draft.future
  const frame = from.at(-1)
  if (frame) {
    return {
      ...draft, frame, error: undefined, valid: undefined,
      past: direction === 'undo' ? draft.past.slice(0, -1) : [...draft.past, draft.frame],
      future: direction === 'redo' ? draft.future.slice(0, -1) : [...draft.future, draft.frame],
    }
  }
  const target = draft[direction].at(-1)
  return target && !draft.frame.length && version
    ? { ...draft, intent: { kind: direction, target }, version, error: undefined, valid: undefined }
    : draft
}

export function clearDraft(draft: Draft): Draft {
  return { ...draft, frame: [], past: [], future: [], intent: undefined, version: undefined,
    error: undefined, valid: undefined }
}

export function confirmed(draft: Draft, receipt: Receipt): Draft {
  const id = receipt.changeId
  if (!id) throw new Error('The engine omitted the change ID.')
  const reset = { ...clearDraft(draft), attempt: undefined, busy: undefined, receipt }
  if (draft.intent?.kind === 'undo')
    return { ...reset, undo: draft.undo.slice(0, -1), redo: [...draft.redo, id] }
  if (draft.intent?.kind === 'redo')
    return { ...reset, redo: draft.redo.slice(0, -1), undo: [...draft.undo, id] }
  return { ...reset, undo: [...draft.undo, id], redo: [] }
}

export function mergeIndependent(original: Definition, ours: Definition, theirs: Definition) {
  const changed = new Set([...Object.keys(original), ...Object.keys(ours)])
  const result = { ...theirs }
  for (const field of changed) {
    if (same(original[field], ours[field])) continue
    if (!same(original[field], theirs[field]) && !same(ours[field], theirs[field])) return undefined
    if (Object.hasOwn(ours, field)) Object.defineProperty(result, field, {
      value: ours[field], enumerable: true, configurable: true, writable: true,
    })
    else delete result[field]
  }
  return result
}
