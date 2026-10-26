import { deepClone, deepEqual, isPlainObject, JsonObject } from '../contracts/types';

/**
 * REST workspace: local intentions only. Never computes equipment legality or effective attributes.
 * Port of scripts/application/loadout_workspace.gd
 */
export class LoadoutWorkspace {
  private _gateway?: any;
  source: JsonObject = {};
  assignments: Record<string, string> = {};
  selectedItem = '';
  selectedSlot = '';
  search = '';
  preview: JsonObject = {};
  generation = 0;
  message = '';
  configured = false;
  busy = false;
  error = '';

  constructor(gateway?: any) {
    this._gateway = gateway;
  }

  async loadSetting(setting: JsonObject): Promise<boolean> {
    if (!this._gateway || typeof this._gateway.readLoadoutOptions !== 'function') {
      return false;
    }
    this.busy = true;
    this.error = '';
    const launch = isPlainObject(setting?.launch) ? setting.launch : {};
    const modeId = String(launch.modeId ?? 'default');
    const playerId = String(launch.playerEntityId ?? 'player');
    const seed = Number(launch.seed ?? 20260911);
    const res = await this._gateway.readLoadoutOptions(modeId, playerId, seed);
    this.busy = false;
    if (!res.ok || !isPlainObject(res.data)) {
      this.configured = false;
      this.error = String(res.error ?? 'Loadout not available');
      return false;
    }
    const data = res.data;
    const defaultAssignments: Record<string, string> = {};
    for (const row of data.defaultAssignments ?? []) {
      if (row?.slotId && row?.inscriptionId) {
        defaultAssignments[String(row.slotId)] = String(row.inscriptionId);
      }
    }
    const items = (data.inscriptions ?? []).map((ins: any) => ({
      instance: { instanceId: String(ins.inscriptionId ?? '') },
      definition: ins,
    }));
    this.source = {
      canEquip: true,
      items,
      policy: {
        slots: Array.isArray(data.slots) ? data.slots : [],
        capacity: Number(data.capacity ?? 0),
      },
      assignments: defaultAssignments,
      usedCapacity: Number(data.usedCapacity ?? 0),
    };
    this.assignments = deepClone(defaultAssignments);
    this.configured = true;
    if (typeof this._gateway.previewLoadoutOptions === 'function') {
      const pairs = Object.entries(this.assignments).map(([slotId, inscriptionId]) => ({
        slotId,
        inscriptionId,
      }));
      const prev = await this._gateway.previewLoadoutOptions(modeId, playerId, seed, pairs);
      if (prev.ok && isPlainObject(prev.data)) {
        this.preview = {
          before: deepClone(this.source),
          after: {
            ...deepClone(this.source),
            ...deepClone(prev.data),
          },
        };
        if (typeof prev.data.capacity === 'number') {
          this.source.policy.capacity = prev.data.capacity;
        }
      }
    }
    return true;
  }

  accept(snapshot: JsonObject): boolean {
    if (
      !Array.isArray(snapshot?.items) ||
      !isPlainObject(snapshot?.policy) ||
      !isPlainObject(snapshot?.assignments)
    ) {
      return false;
    }
    this.source = deepClone(snapshot);
    this.assignments = deepClone(this.source.assignments);
    this.selectedItem = '';
    this.selectedSlot = '';
    this.preview = {};
    this.message = '';
    this.generation += 1;
    return true;
  }

  dirty(): boolean {
    return !deepEqual(this.assignments, this.source.assignments ?? {});
  }

  selectItem(id: string): void {
    this.selectedItem = id;
  }

  selectSlot(id: string): void {
    this.selectedSlot = id;
  }

  invalidatePreview(): void {
    this.generation += 1;
    this.preview = {};
    this.message = 'Preview required. Attributes are not calculated locally.';
  }

  stageSelected(): void {
    if (!this.selectedItem || !this.selectedSlot) return;
    let origin = '';
    for (const [slotId, itemId] of Object.entries(this.assignments)) {
      if (itemId === this.selectedItem) {
        origin = slotId;
        break;
      }
    }
    this.stageDrop(this.selectedItem, this.selectedSlot, origin);
  }

  stageDrop(item: string, slot: string, origin: string): boolean {
    if (!this.source.canEquip) return false;
    const hasItem = (this.source.items ?? []).some(
      (row: any) => String(row?.instance?.instanceId ?? '') === item
    );
    if (!hasItem) return false;
    const hasSlot = (this.source.policy?.slots ?? []).some(
      (row: any) => String(row?.slotId ?? '') === slot
    );
    if (!hasSlot) return false;

    let actual = '';
    for (const [slotId, itemId] of Object.entries(this.assignments)) {
      if (itemId === item) {
        actual = slotId;
        break;
      }
    }
    if (actual !== origin || slot === origin) return false;

    const displaced = String(this.assignments[slot] ?? '');
    if (origin.length > 0) {
      delete this.assignments[origin];
      if (displaced.length > 0) {
        this.assignments[origin] = displaced;
      }
    }
    this.assignments[slot] = item;
    this.selectedItem = item;
    this.selectedSlot = slot;
    this.invalidatePreview();
    return true;
  }

  removeSelected(): void {
    if (this.source.canEquip && this.selectedSlot) {
      delete this.assignments[this.selectedSlot];
      this.invalidatePreview();
    }
  }

  clear(): void {
    if (this.source.canEquip) {
      this.assignments = {};
      this.invalidatePreview();
    }
  }

  restore(): void {
    this.assignments = deepClone(this.source.assignments ?? {});
    this.selectedItem = '';
    this.selectedSlot = '';
    this.invalidatePreview();
  }

  acceptPreview(value: JsonObject, ticket: number, captured: Record<string, string>): boolean {
    if (
      ticket !== this.generation ||
      !deepEqual(captured, this.assignments) ||
      !isPlainObject(value?.before) ||
      !isPlainObject(value?.after)
    ) {
      return false;
    }
    for (const key of [
      'runId',
      'actorId',
      'inventoryId',
      'runSequence',
      'expectedStep',
      'contentRevision',
    ]) {
      if (value.before[key] !== this.source[key] || value.after[key] !== this.source[key]) {
        return false;
      }
    }
    if (!deepEqual(value.after.assignments, this.assignments)) {
      return false;
    }
    this.preview = deepClone(value);
    this.message = 'Authoritative preview ready.';
    return true;
  }

  canConfirm(): boolean {
    return (
      this.dirty() && Object.keys(this.preview).length > 0 && Boolean(this.source.canEquip)
    );
  }

  snapshot(): JsonObject {
    const current = isPlainObject(this.preview.after) ? this.preview.after : this.source;
    const items: JsonObject[] = [];
    for (const row of this.source.items ?? []) {
      const definition = row.definition ?? {};
      const presentation = definition.presentation ?? {};
      items.push({
        instanceId: String(row.instance?.instanceId ?? ''),
        name: presentation.name ?? definition.inscriptionId ?? '',
        family: 'Character inscription',
        rarity: presentation.rarity ?? 'common',
        cost: definition.equipmentCost ?? 0,
        tags: definition.tags ?? [],
        attributeBonuses: [],
        tradeoffs: [],
        icon: presentation.icon ?? '',
        attributeIds: (definition.attributeBonuses ?? []).map((b: any) =>
          String(b?.attributeId ?? '')
        ),
        description: presentation.description ?? '',
        equippedActorId: row.equippedActorId ?? '',
      });
    }
    const attributes: JsonObject[] = [];
    const projection = isPlainObject(current.attributes) ? current.attributes : {};
    for (const stat of projection.attributes ?? []) {
      const baseline = Number(stat.baseValue ?? 0);
      const effective = Number(stat.effectiveValue ?? baseline);
      attributes.push({
        id: stat.attributeId,
        label: stat.displayName,
        group: 'Character attributes',
        base: baseline,
        value: effective,
        change: effective - baseline,
        format: ['percent', '%'].includes(String(stat.unit ?? '')) ? 'percent' : 'number',
        sources: [],
        contextTags: stat.contextTags ?? [],
      });
    }
    return {
      authority: 'REST',
      contextKey: [
        this.source.runId,
        this.source.actorId,
        this.source.inventoryId,
        this.source.contentRevision,
      ],
      slots: (this.source.policy?.slots ?? []).map((slot: any) => String(slot?.slotId ?? '')),
      items,
      assignments: deepClone(this.assignments),
      selectedItem: this.selectedItem,
      selectedSlot: this.selectedSlot,
      usedCapacity: current.usedCapacity ?? 0,
      capacity: this.source.policy?.capacity ?? 0,
      overBudget: false,
      presets: [],
      presetIndex: -1,
      search: this.search,
      status:
        this.message.length > 0
          ? this.message
          : String(current.attributesUnavailableReason ?? ''),
      canEdit: Boolean(this.source.canEquip),
      attributesReady:
        (!this.dirty() || Object.keys(this.preview).length > 0) &&
        isPlainObject(current.attributes),
      character: {
        name: String(this.source.actorId ?? ''),
        attributes,
        remote: true,
      },
    };
  }
}
