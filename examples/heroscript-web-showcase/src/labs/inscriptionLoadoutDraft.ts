import { deepClone, isPlainObject, JsonObject } from "../contracts/types";

/**
 * LAB ONLY: display-unit additions to authored sample attributes, NOT combat math.
 * Replace this adapter with the engine's attribute projection on REST integration.
 */
export class LoadoutAttributeSample {
  static validate(document: JsonObject): string {
    const character = document.character;
    if (!isPlainObject(character) || !Array.isArray(character.attributes)) {
      return "Invalid sample character.";
    }
    const ids: Record<string, boolean> = {};
    for (const stat of character.attributes) {
      if (!isPlainObject(stat)) return "Invalid sample attribute.";
      const id = String(stat.id ?? "");
      if (!id || id in ids || typeof stat.base !== "number") {
        return "Invalid sample attribute.";
      }
      if (
        !Number.isFinite(stat.base) ||
        !["number", "percent"].includes(String(stat.format ?? ""))
      ) {
        return "Invalid sample attribute unit.";
      }
      if (typeof stat.label !== "string" || typeof stat.group !== "string") {
        return "Invalid sample attribute label.";
      }
      ids[id] = true;
    }

    const inscriptions = Array.isArray(document.inscriptions)
      ? document.inscriptions
      : [];
    for (const item of inscriptions) {
      if (!isPlainObject(item) || !Array.isArray(item.attributeBonuses)) {
        return "Invalid sample bonuses.";
      }
      for (const bonus of item.attributeBonuses) {
        if (
          !isPlainObject(bonus) ||
          !(String(bonus.attributeId ?? "") in ids)
        ) {
          return "Unknown sample attribute.";
        }
        if (typeof bonus.amount !== "number" || !Number.isFinite(bonus.amount)) {
          return "Invalid sample bonus.";
        }
      }
    }
    return "";
  }

  static project(character: JsonObject, equipped: JsonObject[]): JsonObject {
    const result = deepClone(character);
    const attributes = Array.isArray(result.attributes)
      ? (result.attributes as JsonObject[])
      : [];
    for (const stat of attributes) {
      let val = Number(stat.base ?? 0);
      const sources: JsonObject[] = [];
      for (const item of equipped) {
        const bonuses = Array.isArray(item.attributeBonuses)
          ? (item.attributeBonuses as JsonObject[])
          : [];
        for (const bonus of bonuses) {
          if (bonus.attributeId !== stat.id) continue;
          val += Number(bonus.amount ?? 0);
          sources.push({ name: item.name, amount: bonus.amount });
        }
      }
      stat.value = val;
      stat.sources = sources;
      stat.change = val - Number(stat.base ?? 0);
    }
    return result;
  }
}

/**
 * Offline UI draft only. No authoritative legality, combat math, RNG, API or persistence.
 */
export class InscriptionLoadoutDraft {
  private _fixture: JsonObject = {};
  private _items: Record<string, JsonObject> = {};
  private _assignments: Record<string, string> = {};
  selectedItem = "";
  selectedSlot = "";
  presetIndex = 0;
  search = "";

  configure(document: JsonObject): string {
    if (
      document.schemaVersion !== 1 ||
      document.authority !== "VisualFixture"
    ) {
      return "Invalid visual fixture.";
    }
    const sampleError = LoadoutAttributeSample.validate(document);
    if (sampleError.length > 0) return sampleError;
    if (
      typeof document.capacity !== "number" ||
      document.capacity < 0 ||
      !Number.isFinite(document.capacity)
    ) {
      return "Invalid visual capacity.";
    }
    const slots = document.slots;
    if (!Array.isArray(slots) || slots.length === 0 || slots.length > 64) {
      return "Invalid visual positions.";
    }
    const unique: Record<string, boolean> = {};
    for (const id of slots) {
      if (typeof id !== "string" || !id || id in unique) {
        return "Duplicate or invalid visual position.";
      }
      unique[id] = true;
    }

    const items: Record<string, JsonObject> = {};
    const inscriptions = Array.isArray(document.inscriptions)
      ? document.inscriptions
      : [];
    for (const item of inscriptions) {
      if (!isPlainObject(item) || !String(item.instanceId ?? "")) {
        return "Invalid visual item.";
      }
      const id = String(item.instanceId);
      if (
        id in items ||
        typeof item.cost !== "number" ||
        !Number.isFinite(item.cost) ||
        item.cost < 0
      ) {
        return "Invalid visual item cost or identity.";
      }
      items[id] = deepClone(item);
    }

    const presets = Array.isArray(document.presets)
      ? (document.presets as JsonObject[])
      : [];
    if (presets.length === 0) {
      return "Visual fixture requires a preset.";
    }
    for (const preset of presets) {
      const seen: Record<string, boolean> = {};
      const assignments = isPlainObject(preset.assignments)
        ? preset.assignments
        : {};
      for (const slot of Object.keys(assignments)) {
        const id = String(assignments[slot]);
        if (!slots.includes(slot) || !(id in items) || id in seen) {
          return "Invalid visual preset reference.";
        }
        seen[id] = true;
      }
    }

    this._fixture = deepClone(document);
    this._items = items;
    this.selectedItem = "";
    this.selectedSlot = "";
    this.search = "";
    this.applyPreset(0);
    return "";
  }

  selectItem(id: string): void {
    if (id in this._items) {
      this.selectedItem = id;
    }
  }

  selectSlot(id: string): void {
    const slots = Array.isArray(this._fixture.slots)
      ? this._fixture.slots.map(String)
      : [];
    if (slots.includes(id)) {
      this.selectedSlot = id;
    }
  }

  stageSelected(): void {
    if (!this.selectedItem || !this.selectedSlot) return;
    let source = "";
    for (const slot of Object.keys(this._assignments)) {
      if (this._assignments[slot] === this.selectedItem) {
        source = slot;
      }
    }
    this.stageDrop(this.selectedItem, this.selectedSlot, source);
  }

  removeSelected(): void {
    delete this._assignments[this.selectedSlot];
  }

  stageDrop(itemId: string, target: string, source: string): boolean {
    const slots = Array.isArray(this._fixture.slots)
      ? this._fixture.slots.map(String)
      : [];
    if (!(itemId in this._items) || !slots.includes(target)) {
      return false;
    }
    let actualSource = "";
    for (const slot of Object.keys(this._assignments)) {
      if (this._assignments[slot] === itemId) {
        actualSource = slot;
      }
    }
    if (actualSource !== source || target === source) {
      return false;
    }
    const next = { ...this._assignments };
    const displaced = next[target] ?? "";
    if (source.length > 0) {
      delete next[source];
      if (displaced.length > 0) {
        next[source] = displaced;
      }
    }
    next[target] = itemId;
    this._assignments = next;
    this.selectedItem = itemId;
    this.selectedSlot = target;
    return true;
  }

  clear(): void {
    this._assignments = {};
  }

  applyPreset(index: number): void {
    const presets = Array.isArray(this._fixture.presets)
      ? (this._fixture.presets as JsonObject[])
      : [];
    if (index < 0 || index >= presets.length) return;
    this.presetIndex = index;
    const rawAssign = isPlainObject(presets[index].assignments)
      ? presets[index].assignments
      : {};
    this._assignments = {};
    for (const [k, v] of Object.entries(rawAssign)) {
      this._assignments[k] = String(v);
    }
    this.selectedItem = "";
    this.selectedSlot = "";
  }

  restore(): void {
    this.applyPreset(this.presetIndex);
  }

  snapshot(): JsonObject {
    let used = 0.0;
    const contributions: JsonObject[] = [];
    const slots = Array.isArray(this._fixture.slots)
      ? this._fixture.slots.map(String)
      : [];
    for (const slot of slots) {
      const assignedId = this._assignments[slot] ?? "";
      const item = this._items[assignedId];
      if (!item) continue;
      used += Number(item.cost ?? 0);
      contributions.push(deepClone(item));
    }
    const capacity = Number(this._fixture.capacity ?? 0);
    const character = isPlainObject(this._fixture.character)
      ? this._fixture.character
      : {};
    return {
      authority: "VisualFixture",
      slots: [...slots],
      items: deepClone(Object.values(this._items)),
      assignments: { ...this._assignments },
      selectedItem: this.selectedItem,
      selectedSlot: this.selectedSlot,
      usedCapacity: used,
      capacity,
      overBudget: used > capacity,
      contributions,
      presets: Array.isArray(this._fixture.presets)
        ? deepClone(this._fixture.presets)
        : [],
      presetIndex: this.presetIndex,
      character: LoadoutAttributeSample.project(character, contributions),
      search: this.search,
    };
  }
}
