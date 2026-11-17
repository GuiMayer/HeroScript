import type { Definition, Diagnostic } from './api/client'
import { array, object, propertyKey, string } from './editor-state'

export interface DiagnosticFocus {
  path: string
}

export function isWithin(path: string, parent: string) {
  const target = path.toLowerCase()
  const prefix = parent.toLowerCase()
  return target === prefix || target.startsWith(`${prefix}/`)
}

export function joinPointer(path: string, key: string | number) {
  return `${path}/${String(key).replaceAll('~', '~0').replaceAll('/', '~1')}`
}

const referenceFields: Record<string, string[]> = {
  resources: ['resourceId', 'targetResource', 'selectionResourceId'],
  'status-effects': ['statusId'],
  modifiers: ['modifierId'],
  cards: ['cardDefinitionId'],
  'calculation-pipelines': ['calculationPipelineId', 'pipelineId', 'overflowPipelineId'],
  'condensation-recipes': ['condensationRecipeId'],
}

function findReference(value: unknown, path: string, fields: string[], id: string): string[] {
  if (Array.isArray(value)) return value.flatMap((item, index) => findReference(item, joinPointer(path, index), fields, id))
  return Object.entries(object(value)).flatMap(([key, item]) => {
    const address = joinPointer(path, key)
    return item === id && fields.some((field) => field.toLowerCase() === key.toLowerCase()) ? [address]
      : findReference(item, address, fields, id)
  })
}

export function diagnosticCardId(item: Diagnostic) {
  if (item.kind !== 'cards') return undefined
  return item.definitionId || /(?:^|\s)cards\/([^/:\s]+)/.exec(item.parameters?.message ?? '')?.[1]
}

export function cardDiagnosticPath(item: Diagnostic, definition: Definition): string | undefined {
  if (item.jsonPointer?.startsWith('/')) return item.jsonPointer
  const message = item.parameters?.message ?? ''
  const jsonPath = /Path: \$(\S+)/.exec(message)?.[1]
  if (jsonPath) return jsonPath.replace(/\[(\d+)\]/g, '/$1').replaceAll('.', '/')

  const componentsKey = propertyKey(definition, 'components')
  const components = array(definition[componentsKey])
  const prefix = `cards/${diagnosticCardId(item) ?? ''}/components/`
  const remainder = message.startsWith(prefix) ? message.slice(prefix.length) : ''
  const index = remainder ? components.findIndex((component) => {
    const entry = object(component)
    const id = string(entry[propertyKey(entry, 'componentId')])
    return id && (remainder.startsWith(`${id}/`) || remainder.startsWith(`${id}:`))
  }) : -1
  let path = ''
  let value: unknown = definition
  if (index >= 0) {
    path = joinPointer(joinPointer('', componentsKey), index)
    value = components[index]
    const effectMatch = /\/effects\[(\d+)\]((?:\/chainedEffects\[\d+\])*)/.exec(remainder)
    if (effectMatch && object(value).type === 'effect') {
      const key = propertyKey(object(value), 'effect')
      path = joinPointer(path, key)
      value = object(value)[key]
      for (const child of effectMatch[2].matchAll(/\[(\d+)\]/g)) {
        const children = propertyKey(object(value), 'chainedEffects')
        path = joinPointer(joinPointer(path, children), child[1])
        value = array(object(value)[children])[Number(child[1])]
      }
    }
  }
  const missing = /references missing ([^/\s]+)\/(\S+)$/.exec(message)
  if (missing && referenceFields[missing[1]]) {
    const candidates = findReference(value, path, referenceFields[missing[1]], missing[2])
    if (candidates.length === 1) return candidates[0]
  }
  return path || undefined
}
