import type { components } from './schema'

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
export const settingPath = (setting: string) => `/settings/${encodeURIComponent(setting)}`
export const cardPath = (setting: string, id: string) =>
  `${settingPath(setting)}/definitions/cards/${encodeURIComponent(id)}`

export async function api<T>(key: string, path: string, options: {
  signal?: AbortSignal; body?: Change; version?: string
} = {}) {
  const headers: Record<string, string> = { 'X-Admin-Key': key, Accept: 'application/json' }
  if (options.version) headers['If-Match'] = options.version
  if (options.body) headers['Content-Type'] = 'application/json'
  const response = await fetch(root + path, {
    method: options.body ? 'POST' : 'GET', headers, signal: options.signal,
    body: options.body ? JSON.stringify(options.body) : undefined,
  })
  const json = await response.json().catch(() => undefined)
  if (!response.ok) throw new ApiError(response.status, json && typeof json === 'object' ? json : undefined)
  if (json === undefined) throw new Error('The engine returned an empty response.')
  return { data: json as T, version: response.headers.get('ETag') ?? undefined }
}
