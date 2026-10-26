import { deepClone, deepEqual, isPlainObject, JsonObject } from "../contracts/types";
import { I18n } from "./i18n";

/**
 * Turns revisioned zone presentation metadata into UI groups, without card rules.
 */
export class CardZonePresenter {
  private _zones: JsonObject[] = [];
  private _i18n: I18n;

  constructor(snapshot: JsonObject, translator: I18n) {
    this._i18n = translator;
    this._zones = Array.isArray(snapshot.zones)
      ? deepClone(snapshot.zones as JsonObject[])
      : [];
  }

  playableCards(): JsonObject[] {
    const result: JsonObject[] = [];
    for (const zone of this._zones) {
      if (Boolean(zone.allowsCardPlay) && Boolean(zone.contentsVisible)) {
        const cards = Array.isArray(zone.cards) ? (zone.cards as JsonObject[]) : [];
        result.push(...deepClone(cards));
      }
    }
    return result;
  }

  playableLabel(): string {
    const labels: string[] = [];
    for (const zone of this._zones) {
      if (Boolean(zone.allowsCardPlay)) {
        labels.push(this.zoneLabel(zone));
      }
    }
    return labels.length > 0 ? labels.join(" / ") : this._i18n.text("PLAYABLE CARDS");
  }

  totalCards(): number {
    let result = 0;
    for (const zone of this._zones) {
      result += Math.max(0, Math.trunc(Number(zone.count ?? 0)));
    }
    return result;
  }

  auxiliaryZones(): JsonObject[] {
    const result: JsonObject[] = [];
    for (const zone of this._zones) {
      if (!zone.allowsCardPlay) {
        result.push(deepClone(zone));
      }
    }
    return result;
  }

  zoneLabel(zone: JsonObject): string {
    const presentation = isPlainObject(zone.presentation) ? zone.presentation : {};
    const labelKey = String(presentation.labelKey ?? zone.zoneId ?? "");
    return this._i18n.text(labelKey);
  }

  zoneCards(zoneId: string): JsonObject[] {
    for (const zone of this._zones) {
      if (String(zone.zoneId ?? "") === zoneId) {
        return Boolean(zone.contentsVisible) && Array.isArray(zone.cards)
          ? deepClone(zone.cards as JsonObject[])
          : [];
      }
    }
    return [];
  }

  static toolFlows(run: JsonObject): JsonObject[] {
    const mode = isPlainObject(run.resolvedMode) ? run.resolvedMode : {};
    const policy = isPlainObject(mode.capabilityPolicy) ? mode.capabilityPolicy : {};
    if (!policy.allowCardZoneCheats) {
      return [];
    }
    const system = isPlainObject(mode.cardZoneSystem) ? mode.cardZoneSystem : {};
    const flows = Array.isArray(system.flows) ? (system.flows as JsonObject[]) : [];
    const result: JsonObject[] = [];
    for (const flow of flows) {
      if (
        isPlainObject(flow) &&
        Array.isArray(flow.allowedInvocations) &&
        flow.allowedInvocations.includes("Tool")
      ) {
        result.push(deepClone(flow));
      }
    }
    result.sort((a, b) => String(a.flowId ?? "").localeCompare(String(b.flowId ?? "")));
    return result;
  }

  static toolPayload(
    flow: JsonObject,
    definitionText: string,
    instanceText: string,
    actorId: string
  ): JsonObject {
    const payload: JsonObject = { flowId: String(flow.flowId ?? "") };
    const definitions = CardZonePresenter._splitIds(definitionText);
    const instances = CardZonePresenter._splitIds(instanceText);
    if (definitions.length > 0) payload.cardDefinitionIds = definitions;
    if (instances.length > 0) payload.cardInstanceIds = instances;
    if (actorId.trim().length > 0) payload.actorId = actorId.trim();
    return payload;
  }

  private static _splitIds(value: string): string[] {
    const result: string[] = [];
    for (const token of value.replace(/\n/g, ",").split(",")) {
      const id = token.trim();
      if (id.length > 0 && !result.includes(id)) {
        result.push(id);
      }
    }
    return result;
  }
}

/**
 * Published visibility/order and card instances only. No card arithmetic.
 */
export class CardCollectionPresenter {
  static address(zone: JsonObject): JsonObject {
    return {
      zoneId: zone.zoneId ?? "",
      ownerId: zone.ownerId ?? "",
      scopeId: zone.scopeId ?? null,
    };
  }

  static find(snapshot: JsonObject, requested: JsonObject): JsonObject {
    const zones = Array.isArray(snapshot.zones) ? (snapshot.zones as JsonObject[]) : [];
    for (const zone of zones) {
      if (deepEqual(CardCollectionPresenter.address(zone), requested)) {
        return deepClone(zone);
      }
    }
    return {};
  }

  static build(
    zone: JsonObject,
    presenter: { cardViewModel(instanceId: string): JsonObject },
    translator: I18n
  ): JsonObject {
    const models: JsonObject[] = [];
    if (Boolean(zone.contentsVisible)) {
      const cards = Array.isArray(zone.cards) ? (zone.cards as JsonObject[]) : [];
      for (const instance of cards) {
        const model = presenter.cardViewModel(String(instance.cardInstanceId ?? ""));
        model.availability = translator.text("Inspect card");
        model.collectionTags = Array.isArray(instance.tags) ? [...instance.tags] : [];
        models.push(model);
      }
    }
    if (!zone.orderVisible) {
      models.sort((a, b) =>
        `${a.definitionId ?? ""}${a.cardInstanceId ?? ""}`.localeCompare(
          `${b.definitionId ?? ""}${b.cardInstanceId ?? ""}`
        )
      );
    }
    const presentation = isPlainObject(zone.presentation) ? zone.presentation : {};
    return {
      address: CardCollectionPresenter.address(zone),
      label: translator.text(String(presentation.labelKey ?? zone.zoneId ?? "")),
      count: Math.trunc(Number(zone.count ?? 0)),
      visible: Boolean(zone.contentsVisible),
      ordered: Boolean(zone.orderVisible),
      cards: models,
    };
  }
}
