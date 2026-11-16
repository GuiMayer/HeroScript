type JsonParser = (text: string, revive: (key: string, value: unknown, context?: { source?: string }) => unknown) => unknown
const parse = JSON.parse as JsonParser

export function parseAuthoringJson(text: string) {
  return parse(text, (key, value, context) => {
    if (key.toLowerCase() !== 'seed' || typeof value !== 'number' || !Number.isInteger(value) || Number.isSafeInteger(value)) return value
    if (!context?.source)
      throw new Error('The seed must be represented as an exact integer.')
    return /^-?\d+$/.test(context.source) ? BigInt(context.source) : value
  })
}

export function stringifyAuthoringJson(value: unknown, space?: number): string {
  const rawJson = (JSON as typeof JSON & { rawJSON: (text: string) => unknown }).rawJSON
  return JSON.stringify(value, (_, item: unknown) => typeof item === 'bigint' ? rawJson(item.toString()) : item, space)
}
