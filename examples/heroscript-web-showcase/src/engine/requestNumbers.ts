import { deepClone, isPlainObject, JsonObject } from '../contracts/types';

/**
 * Wire-format adaptation only. Never round gameplay values or rewrite a retry envelope.
 * Port of scripts/engine/request_numbers.gd
 */
export class RequestNumbers {
  static readonly COMMAND_PATHS: Record<string, string[]> = {
    REMOVE_CARD_TRANSFORMATION: ['transformationId'],
    REPLACE_CARD_TRANSFORMATION: ['transformationId'],
    RESTORE_HEAD_FROM_HISTORY: ['sourceSequence'],
    INVOKE_CARD_ZONE_GAMEPLAY_FLOW: ['requestedCount'],
    APPLY_RUN_RESOURCE: ['operation', 'field'],
    PLAY_CARD: ['actionType'],
    END_TURN: ['actionType'],
    EXECUTE_ACTION: ['actionType'],
    START_ENCOUNTER: ['participants/*/identityBinding', 'participants/*/controllerBinding/kind'],
  };

  static readonly SCENARIO_PATHS: string[] = [
    'schemaVersion',
    'seed',
    'initialState/effects/*/stacks',
    'influences/*/priority',
    'influences/*/scope',
    'participants/*/controllerBinding/kind',
    'relationships/sameSide',
    'relationships/differentSides',
    'relationships/rules/*/relationship',
  ];

  static integerToken(value: any): any {
    if (
      typeof value === 'number' &&
      Number.isFinite(value) &&
      value === Math.floor(value) &&
      Math.abs(value) <= 9007199254740991
    ) {
      const t = Math.trunc(value);
      return Object.is(t, -0) ? 0 : t;
    }
    return value;
  }

  static normalize<T = any>(value: T): T {
    if (typeof value === 'number') {
      return RequestNumbers.integerToken(value) as unknown as T;
    }
    if (Array.isArray(value)) {
      return value.map((item) => RequestNumbers.normalize(item)) as unknown as T;
    }
    if (isPlainObject(value)) {
      const out: JsonObject = {};
      for (const [k, v] of Object.entries(value)) {
        out[k] = RequestNumbers.normalize(v);
      }
      return out as unknown as T;
    }
    return value;
  }

  static fields(document: JsonObject, paths: string[]): JsonObject {
    const result = deepClone(document);
    for (const path of paths) {
      RequestNumbers.visit(result, String(path).split('/'), 0);
    }
    return result;
  }

  static payload(type: string, document: JsonObject): JsonObject {
    const key = String(type ?? '').trim().toUpperCase();
    return RequestNumbers.fields(document, RequestNumbers.COMMAND_PATHS[key] ?? []);
  }

  private static visit(node: any, path: string[], index: number): void {
    if (index >= path.length || node === null || typeof node !== 'object') return;
    if (path[index] === '*') {
      const values: any[] = Array.isArray(node)
        ? node
        : isPlainObject(node)
          ? Object.values(node)
          : [];
      for (const child of values) {
        if (index + 1 < path.length) {
          RequestNumbers.visit(child, path, index + 1);
        }
      }
    } else if (isPlainObject(node)) {
      const targetKey = path[index].toLowerCase();
      for (const key of Object.keys(node)) {
        if (key.toLowerCase() !== targetKey) continue;
        if (index + 1 === path.length) {
          node[key] = RequestNumbers.integerToken(node[key]);
        } else {
          RequestNumbers.visit(node[key], path, index + 1);
        }
      }
    }
  }
}
