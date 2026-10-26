export type JsonObject = Record<string, any>;

export function deepClone<T>(value: T): T {
  if (value === null || typeof value !== 'object') {
    return value;
  }
  if (Array.isArray(value)) {
    return value.map((item) => deepClone(item)) as unknown as T;
  }
  const result: Record<string, any> = {};
  for (const key of Object.keys(value as Record<string, any>)) {
    result[key] = deepClone((value as Record<string, any>)[key]);
  }
  return result as T;
}

export function deepEqual(a: any, b: any): boolean {
  if (a === b) return true;
  if (a === null || b === null || typeof a !== 'object' || typeof b !== 'object') {
    if (typeof a === 'number' && typeof b === 'number' && Number.isNaN(a) && Number.isNaN(b)) {
      return true;
    }
    return false;
  }
  if (Array.isArray(a) !== Array.isArray(b)) return false;
  if (Array.isArray(a)) {
    if (a.length !== b.length) return false;
    for (let i = 0; i < a.length; i++) {
      if (!deepEqual(a[i], b[i])) return false;
    }
    return true;
  }
  const keysA = Object.keys(a);
  const keysB = Object.keys(b);
  if (keysA.length !== keysB.length) return false;
  for (const key of keysA) {
    if (!Object.prototype.hasOwnProperty.call(b, key)) return false;
    if (!deepEqual(a[key], b[key])) return false;
  }
  return true;
}

export function isPlainObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function deterministicHash(value: unknown): string {
  const input = typeof value === 'string' ? value : JSON.stringify(value);
  // Fast deterministic FNV-1a / 256-bit hex hash synchronous equivalent for UI option references
  let h1 = 0xdeadbeef ^ input.length;
  let h2 = 0x41c6ce57 ^ input.length;
  let h3 = 0x9e3779b9 ^ input.length;
  let h4 = 0x85ebca6b ^ input.length;
  for (let i = 0; i < input.length; i++) {
    const ch = input.charCodeAt(i);
    h1 = Math.imul(h1 ^ ch, 2654435761);
    h2 = Math.imul(h2 ^ ch, 1597334677);
    h3 = Math.imul(h3 ^ ch, 2246822507);
    h4 = Math.imul(h4 ^ ch, 3266489909);
  }
  h1 = Math.imul(h1 ^ (h1 >>> 16), 2246822507) ^ Math.imul(h2 ^ (h2 >>> 13), 3266489909);
  h2 = Math.imul(h2 ^ (h2 >>> 16), 2246822507) ^ Math.imul(h1 ^ (h1 >>> 13), 3266489909);
  h3 = Math.imul(h3 ^ (h3 >>> 16), 2246822507) ^ Math.imul(h4 ^ (h4 >>> 13), 3266489909);
  h4 = Math.imul(h4 ^ (h4 >>> 16), 2246822507) ^ Math.imul(h3 ^ (h3 >>> 13), 3266489909);
  const hex = (n: number) => (n >>> 0).toString(16).padStart(8, '0');
  return hex(h1) + hex(h2) + hex(h3) + hex(h4) + hex(h1 ^ h3) + hex(h2 ^ h4) + hex(h1 ^ h4) + hex(h2 ^ h3);
}

export type Listener<T = void> = (payload: T) => void;

export class Signal<T = void> {
  private listeners = new Set<Listener<T>>();

  connect(listener: Listener<T>, oneShot = false): () => void {
    if (!oneShot) {
      this.listeners.add(listener);
      return () => this.listeners.delete(listener);
    }
    const wrapped: Listener<T> = (payload: T) => {
      this.listeners.delete(wrapped);
      listener(payload);
    };
    this.listeners.add(wrapped);
    return () => this.listeners.delete(wrapped);
  }

  disconnect(listener: Listener<T>): void {
    this.listeners.delete(listener);
  }

  emit(payload: T): void {
    for (const listener of Array.from(this.listeners)) {
      listener(payload);
    }
  }

  clear(): void {
    this.listeners.clear();
  }
}
