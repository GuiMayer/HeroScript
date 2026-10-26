import { deepClone, isPlainObject, JsonObject } from "../contracts/types";

/**
 * Read-only projection boundary. Data is never exposed by reference.
 */
export class RecordModel {
  protected _data: JsonObject;

  constructor(data: JsonObject) {
    this._data = deepClone(data);
  }

  get id(): string {
    return String(this._data.id ?? "");
  }

  toDictionary(): JsonObject {
    return deepClone(this._data);
  }

  read<T = unknown>(key: string, fallback: T = null as unknown as T): T {
    if (!(key in this._data) || this._data[key] === undefined) {
      return fallback;
    }
    const value = this._data[key];
    return (isPlainObject(value) || Array.isArray(value) ? deepClone(value) : value) as T;
  }
}

export class CardModel extends RecordModel {
  get instanceId(): string {
    return String(this._data.cardInstanceId ?? "");
  }
}

export class ActorModel extends RecordModel {
  get instanceId(): string {
    return String(this._data.instanceId ?? "");
  }

  get relationship(): string {
    return String(this._data.relationship ?? "Neutral");
  }

  get sideId(): string {
    return String(this._data.sideId ?? "");
  }
}

export class ResourceValueModel extends RecordModel {
  get current(): number {
    return Number(this._data.current ?? 0);
  }

  get hasBounds(): boolean {
    return (
      "minimum" in this._data &&
      "maximum" in this._data &&
      Number(this._data.maximum) > Number(this._data.minimum)
    );
  }
}

export class ActionOptionModel extends RecordModel {
  get reference(): string {
    return String(this._data.reference ?? "");
  }
}

export class CombatModel extends RecordModel {
  get context(): string {
    return String(this._data.context ?? "");
  }
}

export class HeaderModel extends RecordModel {}
export class HudModel extends RecordModel {}
export class MapModel extends RecordModel {}
export class ActivityModel extends RecordModel {}
export class OfferModel extends RecordModel {}
export class SceneryModel extends RecordModel {}
