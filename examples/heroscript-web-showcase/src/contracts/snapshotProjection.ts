import { deepClone, isPlainObject, JsonObject } from './types';

/**
 * Shape adaptation only: persisted aggregates and API projections share one reader.
 * Port of scripts/contracts/snapshot_projection.gd
 */
export class SnapshotProjection {
  static combat(snapshot: JsonObject): JsonObject {
    const result = deepClone(snapshot || {});
    const roster = result.actors;
    if (isPlainObject(roster)) {
      const actors: JsonObject[] = [];
      const order: string[] = Array.isArray(result.actorOrder) ? [...result.actorOrder.map(String)] : [];
      for (const id of Object.keys(roster)) {
        if (!order.includes(id)) {
          order.push(id);
        }
      }
      for (const id of order) {
        if (!Object.prototype.hasOwnProperty.call(roster, id) || !isPlainObject(roster[id])) {
          continue;
        }
        const actor = deepClone(roster[id]);
        actor.resources = {};
        const components = actor.components;
        if (isPlainObject(components)) {
          for (const component of Object.values(components)) {
            if (isPlainObject(component) && String(component.type ?? '') === 'resources') {
              actor.resources = deepClone(component.state?.resources ?? {});
            }
          }
        }
        const statusEffects = isPlainObject(result.statusEffects) ? result.statusEffects : {};
        actor.statuses = Array.isArray(statusEffects[id]) ? deepClone(statusEffects[id]) : [];
        actors.push(actor);
      }
      result.actors = actors;
      result.activation = isPlainObject(result.activationState) ? deepClone(result.activationState) : {};
      result.phase = isPlainObject(result.phaseState) ? deepClone(result.phaseState) : {};
      result.step = isPlainObject(result.determinism) ? Number(result.determinism.step ?? 0) : 0;
    }
    if (!('activeActorId' in result)) {
      result.activeActorId = String(result.activation?.activeActorId ?? '');
    }
    return result;
  }

  static run(snapshot: JsonObject): JsonObject {
    const result = deepClone(snapshot || {});
    if (isPlainObject(result.deck) && isPlainObject(result.deck.cardInstances)) {
      result.deck.cardInstances = Object.values(result.deck.cardInstances);
    }
    if (!Array.isArray(result.shops)) {
      result.shops = [];
    }
    return result;
  }
}
