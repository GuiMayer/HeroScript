import type { components } from './schema'
import { parseAuthoringJson, stringifyAuthoringJson } from '../authoring-json'

export type Schemas = components['schemas']
export type Definition = Record<string, unknown>
export type Operation = Schemas['CardAuthoringOperation'] & { definitionId: string }
export type Change = Schemas['CardAuthoringChangeRequest']
export type Receipt = Schemas['CardAuthoringChangeReceipt']
export type Card = Schemas['CardAuthoringDefinition']
export type Diagnostic = Schemas['AuthoringDiagnostic']

export class ApiError extends Error {
  constructor(public status: number, public problem: Partial<Schemas['AuthoringProblem']> = {}) {
    super(problem.detail ?? problem.title ?? `HTTP ${status}`)
  }
}
const root = '/api/v1/admin/authoring'
export const settingPath = (setting: string) => setting ? `/settings/${encodeURIComponent(setting)}` : '/workspace'
export const cardPath = (setting: string, id: string) =>
  `${settingPath(setting)}/definitions/cards/${encodeURIComponent(id)}`
export const definitionsPath = (setting: string, kind: string) =>
  `${settingPath(setting)}/definitions/${encodeURIComponent(kind)}`
export const definitionPath = (setting: string, kind: string, id: string) =>
  `${definitionsPath(setting, kind)}/${encodeURIComponent(id)}`

export async function api<T>(key: string, path: string, options: {
  signal?: AbortSignal; body?: Change; version?: string
} = {}) {
  const headers: Record<string, string> = { 'X-Admin-Key': key, Accept: 'application/json' }
  if (options.version) headers['If-Match'] = options.version
  if (options.body) headers['Content-Type'] = 'application/json'
  const response = await fetch(root + path, {
    method: options.body ? 'POST' : 'GET', headers, signal: options.signal,
    body: options.body ? stringifyAuthoringJson(options.body) : undefined,
  })
  let json: unknown
  try { json = parseAuthoringJson(await response.text()) }
  catch (error) { if (!(error instanceof SyntaxError)) throw error }
  if (!response.ok) throw new ApiError(response.status, json && typeof json === 'object' ? json as ApiError['problem'] : undefined)
  if (json === undefined) throw new Error('The engine returned an empty response.')
  return { data: json as T, version: response.headers.get('ETag') ?? undefined }
}
