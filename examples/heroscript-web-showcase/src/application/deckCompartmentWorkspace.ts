import { deepClone, deepEqual, isPlainObject, JsonObject } from '../contracts/types';

/**
 * Local draft only. Capacity, eligibility and component composition belong to the engine.
 * Port of scripts/application/deck_compartment_workspace.gd
 */
export class DeckCompartmentWorkspace {
  private _gateway?: any;
  source: JsonObject = {};
  draft: Record<string, string> = {};
  previewData: JsonObject = {};
  revision = 0;
  configured = false;
  busy = false;
  error = '';
  private _modeId = '';
  private _playerId = '';
  private _seed = 0;
  private _counts: Record<string, Record<string, number>> = {};

  constructor(gateway?: any) {
    this._gateway = gateway;
  }

  loadSnapshot(snapshot: JsonObject): void {
    this.source = deepClone(snapshot);
    this.draft = {};
    for (const allocation of this.source.allocations ?? []) {
      this.draft[String(allocation.cardInstanceId)] = String(allocation.compartmentId);
    }
    this.previewData = deepClone(this.source);
    this.configured = true;
    this.revision += 1;
  }

  async loadMode(modeId: string, playerId: string, seed: number): Promise<boolean> {
    if (!this._gateway || typeof this._gateway.readMode !== 'function') {
      return false;
    }
    this._modeId = modeId;
    this._playerId = playerId;
    this._seed = seed;
    this.busy = true;
    this.error = '';
    const res = await this._gateway.readMode(modeId, playerId, seed);
    this.busy = false;
    if (!res.ok || !isPlainObject(res.data)) {
      this.configured = false;
      this.error = String(res.error ?? 'Deck compartments unavailable');
      return false;
    }
    const data = res.data;
    this.source = deepClone(data);
    this.configured = Boolean(data.configured ?? true);
    this._counts = {};
    for (const group of data.defaultAllocations ?? []) {
      const compId = String(group?.compartmentId ?? '');
      if (!compId) continue;
      this._counts[compId] = {};
      for (const c of group?.cards ?? []) {
        this._counts[compId][String(c.cardDefinitionId ?? '')] = Number(c.count ?? 0);
      }
    }
    this.previewData = isPlainObject(data.preview) ? deepClone(data.preview) : {};
    this.revision += 1;
    return this.configured;
  }

  adjust(compartmentId: string, cardDefinitionId: string, delta: number): void {
    if (!this._counts[compartmentId]) {
      this._counts[compartmentId] = {};
    }
    const current = Number(this._counts[compartmentId][cardDefinitionId] ?? 0);
    this._counts[compartmentId][cardDefinitionId] = Math.max(0, current + delta);
    this.revision += 1;
  }

  async preview(): Promise<JsonObject> {
    if (!this._gateway || typeof this._gateway.previewMode !== 'function') {
      return { ok: false };
    }
    this.busy = true;
    const res = await this._gateway.previewMode(
      this._modeId,
      this._playerId,
      this._seed,
      this.allocations()
    );
    this.busy = false;
    if (res.ok && isPlainObject(res.data)) {
      this.previewData = deepClone(res.data);
    }
    return res;
  }

  async startRun(seed = this._seed): Promise<JsonObject> {
    if (!this._gateway || typeof this._gateway.startRunWithCompartments !== 'function') {
      return { ok: false };
    }
    this.busy = true;
    const res = await this._gateway.startRunWithCompartments(
      this._modeId,
      this._playerId,
      seed,
      this.allocations()
    );
    this.busy = false;
    return res;
  }

  snapshot(): JsonObject {
    return {
      configured: this.configured,
      compartments: deepClone(this.source.compartments ?? []),
      cards: deepClone(this.source.cards ?? []),
      allocations: deepClone(this._counts),
      preview: deepClone(this.previewData),
    };
  }

  allocations(): Array<{ cardInstanceId: string; compartmentId: string }> {
    const ids = Object.keys(this.draft).sort();
    return ids.map((id) => ({
      cardInstanceId: id,
      compartmentId: this.draft[id],
    }));
  }

  dirty(): boolean {
    const original: Record<string, string> = {};
    for (const allocation of this.source.allocations ?? []) {
      original[String(allocation.cardInstanceId)] = String(allocation.compartmentId);
    }
    return !deepEqual(original, this.draft);
  }

  stage(cardId: string, compartmentId: string): boolean {
    if (!this.source.canChange) return false;
    const exists = (this.source.cards ?? []).some(
      (card: any) => String(card?.cardInstanceId ?? '') === cardId
    );
    if (!exists) return false;
    if (!compartmentId) {
      delete this.draft[cardId];
    } else {
      this.draft[cardId] = compartmentId;
    }
    this.previewData = {};
    this.revision += 1;
    return true;
  }

  clear(): void {
    this.draft = {};
    this.previewData = {};
    this.revision += 1;
  }

  acceptPreview(response: JsonObject, ticket: number): boolean {
    if (
      ticket !== this.revision ||
      String(response.runId ?? '') !== String(this.source.runId ?? '') ||
      Number(response.runSequence ?? -1) !== Number(this.source.runSequence ?? 0)
    ) {
      return false;
    }
    if (
      String(response.contentRevision ?? '') !== String(this.source.contentRevision ?? '') ||
      String(response.actorId ?? '') !== String(this.source.actorId ?? '')
    ) {
      return false;
    }
    this.previewData = deepClone(response);
    return true;
  }
}
