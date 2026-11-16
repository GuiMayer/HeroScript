import { object } from './editor-state'

export interface Schema {
  $ref?: string
  type?: string | string[]
  properties?: Record<string, Schema>
  items?: Schema
  enum?: unknown[]
  const?: unknown
  anyOf?: Schema[]
  oneOf?: Schema[]
  additionalProperties?: Schema | boolean
}

export function resolveSchema(schema: Schema, root: unknown): Schema {
  const visited = new Set<string>()
  while (schema.$ref?.startsWith('#/') && !visited.has(schema.$ref)) {
    visited.add(schema.$ref)
    const segments = schema.$ref.slice(2).split('/').map((part) => part.replace(/~1/g, '/').replace(/~0/g, '~'))
    schema = object(segments.reduce<unknown>((node, part) => {
      return Array.isArray(node) ? node[Number(part)] : object(node)[part]
    }, root)) as Schema
  }
  return schema
}

export function cardComponentSchemas(root: unknown): Map<string, Schema> {
  const components = (root as Schema | undefined)?.properties?.components
  return components?.items ? discriminatedSchemas(components.items, root) : new Map()
}

export function discriminatedSchemas(schema: Schema, root: unknown): Map<string, Schema> {
  const item = resolveSchema(schema, root)
  const result = new Map<string, Schema>()
  for (const option of item.oneOf ?? item.anyOf ?? []) {
    const branch = resolveSchema(option, root)
    const type = branch.properties?.type?.const
    if (typeof type === 'string') result.set(type, branch)
  }
  return result
}
