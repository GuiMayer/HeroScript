import { deepClone, isPlainObject, JsonObject } from '../contracts/types';

/**
 * Immutable visual revision, never a gameplay ledger. Getters copy at the boundary.
 * Port of scripts/presentation/presentation_envelope.gd
 */
export class PresentationEnvelope {
  private _facts: JsonObject = {};
  private _scope: any[] = [];
  private _trace: Record<string, number> = {};

  static dictionary(value: any): JsonObject {
    return isPlainObject(value) ? value : {};
  }

  static build(
    receipt: JsonObject,
    before: JsonObject,
    after: JsonObject,
    endpoint: any[]
  ): PresentationEnvelope | null {
    const previous = PresentationEnvelope.dictionary(before.run);
    const current = PresentationEnvelope.dictionary(after.run);
    if (!String(current.runId ?? '')) return null;
    if (
      String(previous.runId ?? '') !== String(current.runId ?? '') ||
      String(previous.branchId ?? '') !== String(current.branchId ?? '')
    ) {
      return null;
    }
    const previousSetting = String(previous.settingId ?? previous.configName ?? '');
    if (
      previousSetting.length > 0 &&
      previousSetting !== String(current.settingId ?? current.configName ?? '')
    ) {
      return null;
    }
    if (Number(receipt.sequence ?? -1) !== Number(current.sequence ?? -2)) {
      return null;
    }
    const state = PresentationEnvelope.dictionary(receipt.state);
    const resolution = PresentationEnvelope.dictionary(state.resolution);
    const finalCombat = PresentationEnvelope.dictionary(after.combat);
    const combat = isPlainObject(state.combat) ? state.combat : finalCombat;
    if (String(resolution.combatId ?? combat.combatId ?? '') !== String(combat.combatId ?? '')) {
      return null;
    }
    if (
      Object.keys(finalCombat).length > 0 &&
      String(finalCombat.combatId ?? '') !== String(combat.combatId ?? '')
    ) {
      return null;
    }
    const result = new PresentationEnvelope();
    result._scope = [
      deepClone(endpoint),
      current.settingId ?? current.configName ?? '',
      current.runId ?? '',
      current.branchId ?? '',
      combat.combatId ?? '',
      resolution.commandId ?? receipt.commandId ?? '',
      current.sequence ?? 0,
      current.contentRevision ?? current.contentRevisionId ?? '',
    ];
    result._facts = {
      receipt: deepClone(receipt),
      before: deepClone(before),
      after: deepClone(after),
      combat: deepClone(combat),
    };
    result.mark('synchronized');
    return result;
  }

  scope(): any[] {
    return deepClone(this._scope);
  }

  snapshot(which: string): JsonObject {
    return deepClone(this._facts[which] ?? {});
  }

  receipt(): JsonObject {
    return deepClone(this._facts.receipt ?? {});
  }

  mark(name: string): void {
    if (!(name in this._trace)) {
      this._trace[name] = Math.round(performance.now());
    }
  }

  markAt(name: string, timestamp: number): void {
    if (!(name in this._trace)) {
      this._trace[name] = timestamp;
    }
  }

  timings(): Record<string, number> {
    return { ...this._trace };
  }

  static visibleZoneSteps(steps: any[], snapshots: JsonObject[]): JsonObject[] {
    const visible = new Set<string>();
    for (const snapshot of snapshots) {
      for (const zone of snapshot.zones ?? []) {
        if (!zone?.contentsVisible) continue;
        for (const card of zone.cards ?? []) {
          visible.add(String(card?.cardInstanceId ?? ''));
        }
      }
    }
    const result: JsonObject[] = [];
    for (const fact of steps || []) {
      const step = deepClone(fact);
      for (const key of [
        'sourceOrderBefore',
        'sourceOrderAfter',
        'targetOrderBefore',
        'targetOrderAfter',
      ]) {
        delete step[key];
      }
      for (const key of ['instanceIds', 'createdInstanceIds', 'destroyedInstanceIds']) {
        const ids: any[] = Array.isArray(step[key]) ? step[key] : [];
        step[key] = ids.filter((id) => visible.has(String(id)));
        step[`${key}HiddenCount`] = ids.length - step[key].length;
      }
      result.push(step);
    }
    return result;
  }
}
