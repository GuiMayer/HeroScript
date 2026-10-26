import { deepClone, deterministicHash, isPlainObject, JsonObject } from "../contracts/types";
import { CardFactsPresenter } from "./cardFactsPresenter";
import { I18n } from "./i18n";

/**
 * View models join advertised commands with their published offers. No price/rule evaluation.
 */
export class ActivityPresenter {
  static optionReference(choice: JsonObject): string {
    return deterministicHash([
      choice.type ?? "",
      choice.subjectId ?? "",
      choice.payload ?? choice.validPayload ?? {},
    ]);
  }

  private _run: JsonObject;
  private _choices: JsonObject[];
  private _appearance: JsonObject;
  private _i18n: I18n;
  private _cardFacts: JsonObject[] = [];
  private _itemCatalog: Record<string, JsonObject> = {};
  private _ownedItems: Record<string, string> = {};

  constructor(
    run: JsonObject,
    choices: JsonObject[],
    appearance: JsonObject,
    translator: I18n,
    cardFacts: JsonObject[] = [],
    itemSnapshot: JsonObject = {}
  ) {
    this._run = deepClone(run);
    this._choices = deepClone(choices);
    this._appearance = deepClone(appearance);
    this._i18n = translator;
    this._cardFacts = deepClone(cardFacts);

    const catalog = Array.isArray(itemSnapshot.catalog)
      ? (itemSnapshot.catalog as JsonObject[])
      : [];
    for (const item of catalog) {
      this._itemCatalog[String(item.itemDefinitionId ?? "")] = deepClone(item);
    }

    const items = Array.isArray(itemSnapshot.items)
      ? (itemSnapshot.items as JsonObject[])
      : [];
    for (const item of items) {
      const definition = isPlainObject(item.definition) ? item.definition : {};
      const instance = isPlainObject(item.instance) ? item.instance : {};
      const defId = String(definition.itemDefinitionId ?? "");
      this._ownedItems[String(instance.itemInstanceId ?? "")] = defId;
      this._itemCatalog[defId] = deepClone(definition);
    }
  }

  currentNode(): JsonObject {
    const map = isPlainObject(this._run.map) ? this._run.map : {};
    const nodes = Array.isArray(map.nodes) ? (map.nodes as JsonObject[]) : [];
    const currentId = String(this._run.currentNodeId ?? "");
    for (const node of nodes) {
      if (String(node.nodeId ?? "") === currentId) {
        return deepClone(node);
      }
    }
    return {};
  }

  choiceLabel(choice: JsonObject, model: JsonObject = {}): string {
    const type = String(choice.type ?? "");
    const payload = isPlainObject(choice.payload) ? choice.payload : {};
    if (type === "OPEN_INSCRIPTION_OFFER") {
      return this._i18n.text("REVEAL INSCRIPTIONS");
    }
    if (type === "CHOOSE_INSCRIPTION_OFFER") {
      return payload.inscriptionId == null
        ? this._i18n.text("DECLINE INSCRIPTIONS")
        : this._i18n.text("ACQUIRE INSCRIPTION");
    }
    const templates: Record<string, string> = {
      ADVANCE_NODE: "TRAVEL TO %s",
      PICK_CARD_REWARD: "CHOOSE  •  %s",
      DECOMPOSE_CARD_REWARD: "DISMANTLE  •  %s",
      BUY_SHOP_ITEM: "BUY  •  %s",
      APPLY_PREPARATION_OPTION: "PREPARE  •  %s",
      UPGRADE_CARD: "UPGRADE  •  %s",
      REMOVE_CARD_TRANSFORMATION: "REMOVE  •  %s",
      REPLACE_CARD_TRANSFORMATION: "REPLACE  •  %s",
    };
    if (type in templates) {
      const subjectName = String(
        model.name ?? this.nodeName(String(choice.subjectId ?? ""))
      );
      return this._i18n.text(templates[type], subjectName);
    }
    if (type === "ACQUIRE_CARD_MODIFIER_ITEM") {
      return `${this._i18n.text("ACQUIRE MODIFIER")} · ${String(
        model.name ?? ""
      )}`;
    }
    if (type === "APPLY_CARD_MODIFIER_ITEM") {
      return this._i18n.text("APPLY MODIFIER");
    }
    const directMap: Record<string, string> = {
      START_ENCOUNTER: "ENTER COMBAT",
      START_DIALOGUE: "TALK",
      RESOLVE_COMBAT: "CONFIRM RESULT",
      RESOLVE_NODE: "COMPLETE STAGE",
      ACQUIRE_RELIC: "CLAIM RELIC",
      CREATE_CARD_SELECTION: "REVEAL REWARD",
      CREATE_SHOP: "ENTER SHOP",
      CREATE_PREPARATION: "LIGHT CAMPFIRE",
      REROLL_CARD_REWARD: "NEW OPTIONS",
      REROLL_SHOP: "REFRESH STOCK",
    };
    return this._i18n.text(directMap[type] ?? type.replace(/_/g, " "));
  }

  nodeName(id: string): string {
    const map = isPlainObject(this._run.map) ? this._run.map : {};
    const nodes = Array.isArray(map.nodes) ? (map.nodes as JsonObject[]) : [];
    for (const node of nodes) {
      if (String(node.nodeId ?? "") === id) {
        const metadata = isPlainObject(node.metadata) ? node.metadata : {};
        if ("displayName" in metadata) {
          return this._i18n.contentName(id, String(metadata.displayName ?? ""));
        }
      }
    }
    const appNodes = isPlainObject(this._appearance.nodes)
      ? this._appearance.nodes
      : {};
    const appNode = isPlainObject(appNodes[id]) ? appNodes[id] : {};
    return String(appNode.name ?? this._i18n.contentName(id));
  }

  nodeDescription(node: JsonObject, fallback: string): string {
    const metadata = isPlainObject(node.metadata) ? node.metadata : {};
    const authored = String(metadata.description ?? fallback);
    return this._i18n
      .contentName(
        `${node.nodeId ?? ""}.description`,
        authored.length > 0 ? authored : " "
      )
      .trim();
  }

  route(): JsonObject[] {
    const result: JsonObject[] = [];
    const map = isPlainObject(this._run.map) ? this._run.map : {};
    const nodes = Array.isArray(map.nodes) ? (map.nodes as JsonObject[]) : [];
    const resolvedIds = Array.isArray(map.resolvedNodeIds)
      ? map.resolvedNodeIds.map(String)
      : [];
    const currentId = String(this._run.currentNodeId ?? "");

    for (const node of nodes) {
      const id = String(node.nodeId ?? "");
      let travel: JsonObject = {};
      for (const choice of this._choices) {
        if (
          String(choice.type ?? "") === "ADVANCE_NODE" &&
          String(choice.subjectId ?? "") === id
        ) {
          travel = deepClone(choice);
        }
      }
      const activity = isPlainObject(node.activity) ? node.activity : {};
      const metadata = isPlainObject(node.metadata) ? node.metadata : {};
      const next = Array.isArray(node.nextNodeIds)
        ? [...node.nextNodeIds]
        : [];
      result.push({
        id,
        name: this.nodeName(id),
        kind: String(activity.type ?? ""),
        description: this.nodeDescription(node, ""),
        risk: String(metadata.risk ?? ""),
        next,
        travel,
        current: id === currentId,
        resolved: resolvedIds.includes(id),
      });
    }
    return result;
  }

  offer(choice: JsonObject): JsonObject {
    const type = String(choice.type ?? "");
    const subject = String(choice.subjectId ?? "");
    const payload = isPlainObject(choice.payload) ? choice.payload : {};
    let definition = subject;
    if (type === "APPLY_CARD_MODIFIER_ITEM") {
      definition = String(
        this._ownedItems[String(payload.itemInstanceId ?? "")] ?? subject
      );
    }

    let costs: JsonObject[] = [];
    let hasCost = false;
    if (Array.isArray(choice.costs)) {
      costs = deepClone(choice.costs as JsonObject[]);
      hasCost = true;
    }

    if (type === "BUY_SHOP_ITEM") {
      const shops = Array.isArray(this._run.shops)
        ? (this._run.shops as JsonObject[])
        : [];
      for (const shop of shops) {
        if (String(shop.shopInstanceId ?? "") !== String(payload.shopInstanceId ?? "")) {
          continue;
        }
        const items = Array.isArray(shop.items)
          ? (shop.items as JsonObject[])
          : [];
        for (const item of items) {
          if (String(item.itemId ?? "") === subject) {
            definition = item.cardId != null ? String(item.cardId) : subject;
            costs = Array.isArray(item.costs)
              ? deepClone(item.costs as JsonObject[])
              : [];
            hasCost = "costs" in item;
            if (item.modifierItemDefinitionId != null) {
              definition = String(item.modifierItemDefinitionId);
            }
          }
        }
      }
    } else if (type === "APPLY_PREPARATION_OPTION") {
      const preparations = Array.isArray(this._run.preparations)
        ? (this._run.preparations as JsonObject[])
        : [];
      for (const preparation of preparations) {
        if (
          String(preparation.preparationInstanceId ?? "") !==
          String(payload.preparationInstanceId ?? "")
        ) {
          continue;
        }
        const options = Array.isArray(preparation.options)
          ? (preparation.options as JsonObject[])
          : [];
        for (const option of options) {
          if (String(option.optionId ?? "") === subject) {
            costs = Array.isArray(option.costs)
              ? deepClone(option.costs as JsonObject[])
              : [];
            hasCost = "costs" in option;
          }
        }
      }
    } else if (type === "REROLL_SHOP" || type === "REROLL_CARD_REWARD") {
      const isShop = type === "REROLL_SHOP";
      const identity = isShop ? "shopInstanceId" : "selectionInstanceId";
      const offers = Array.isArray(
        this._run[isShop ? "shops" : "cardSelections"]
      )
        ? (this._run[isShop ? "shops" : "cardSelections"] as JsonObject[])
        : [];
      for (const offerState of offers) {
        if (String(offerState[identity] ?? "") === String(payload[identity] ?? "")) {
          costs = Array.isArray(offerState.rerollCosts)
            ? deepClone(offerState.rerollCosts as JsonObject[])
            : [];
          hasCost = "rerollCosts" in offerState;
        }
      }
    }

    const cardsMap = isPlainObject(this._appearance.cards)
      ? this._appearance.cards
      : {};
    const card = isPlainObject(cardsMap[definition]) ? cardsMap[definition] : {};
    const isCard =
      String(choice.subjectType ?? "") === "card" ||
      Object.keys(card).length > 0;
    let name = this._i18n.contentName(
      definition,
      String(card.name ?? definition)
    );
    if (String(choice.subjectType ?? "") === "node") {
      name = this.nodeName(subject);
    }
    let summary = String(
      card.text ?? this._i18n.text("Choose your next action.")
    );
    if (String(choice.subjectType ?? "") === "node") {
      const map = isPlainObject(this._run.map) ? this._run.map : {};
      const nodes = Array.isArray(map.nodes) ? (map.nodes as JsonObject[]) : [];
      for (const node of nodes) {
        if (String(node.nodeId ?? "") === subject) {
          summary = this.nodeDescription(node, summary);
        }
      }
    }
    if ("variantId" in choice && choice.variantId != null) {
      summary = this._i18n.contentName(String(choice.variantId));
    }

    const price: string[] = [];
    for (const cost of costs) {
      price.push(
        `${this._i18n.number(Number(cost.amount ?? 0))} ${this._i18n.contentName(
          String(cost.resourceId ?? "")
        )}`
      );
    }

    const model: JsonObject = {
      name,
      definitionId: definition,
      tone: card.tone ?? "skill",
      summary,
      category: isCard ? "cards" : "activities",
      cost: hasCost
        ? costs.length === 0
          ? this._i18n.text("Free")
          : price.join(" + ")
        : "",
      choice: deepClone(choice),
    };

    if (definition in this._itemCatalog) {
      const itemDef = this._itemCatalog[definition];
      const itemPres = isPlainObject(itemDef.presentation)
        ? itemDef.presentation
        : {};
      model.name = this._i18n.contentName(
        definition,
        String(itemPres.name ?? definition)
      );
      model.category = "modifier_items";
      model.summary = ActivityPresenter.modifierDescription(
        itemDef,
        this._i18n
      );
      model.artId = String(itemPres.artId ?? definition);
    }

    for (const facts of this._cardFacts) {
      if (
        String(choice.subjectType ?? "") === "inscription" ||
        type === "APPLY_CARD_MODIFIER_ITEM"
      ) {
        break;
      }
      const identity = isPlainObject(facts.identity) ? facts.identity : {};
      if ("cardInstanceId" in payload) {
        if (
          String(identity.cardInstanceId ?? "") !==
          String(payload.cardInstanceId)
        ) {
          continue;
        }
      } else {
        if (
          String(identity.kind ?? "") !== "Offer" ||
          String(identity.definitionId ?? "") !== definition
        ) {
          continue;
        }
        const scope = String(
          payload.shopInstanceId ?? payload.selectionInstanceId ?? ""
        );
        if (
          !scope ||
          !String(identity.offerId ?? "").includes(scope)
        ) {
          continue;
        }
      }
      const priceText = String(model.cost ?? "");
      const projected = new CardFactsPresenter(
        this._i18n,
        this._appearance
      ).project(facts);
      Object.assign(model, projected);
      model.acquisitionPrice = priceText;
      break;
    }

    if (
      String(choice.subjectType ?? "") === "inscription" &&
      isPlainObject(choice.presentation)
    ) {
      model.name = this._i18n.text(
        String(choice.presentation.name ?? definition)
      );
      model.summary = this._i18n.text(
        String(choice.presentation.description ?? "")
      );
    }

    return model;
  }

  static modifierDescription(definition: JsonObject, translator: I18n): string {
    const pres = isPlainObject(definition.presentation)
      ? definition.presentation
      : {};
    let authored = String(pres.description ?? "").trim();
    if (!authored) {
      authored =
        "Effect description unavailable. Inspect a compatible card before applying.";
    }
    return translator.contentName(
      `${definition.itemDefinitionId ?? ""}.description`,
      translator.text(authored)
    );
  }

  presentationKey(): unknown[] {
    return [this._cardFacts, this._itemCatalog, this._ownedItems];
  }

  transformationModels(assessment: JsonObject): JsonObject[] {
    const models: JsonObject[] = [];
    for (const side of ["before", "after"]) {
      const facts = assessment[`${side}Presentation`] ?? assessment[side];
      if (!isPlainObject(facts) || !("identity" in facts) || !("effects" in facts)) {
        return [];
      }
      models.push(
        new CardFactsPresenter(this._i18n, this._appearance).project(facts)
      );
    }
    return models;
  }

  transformationText(assessment: JsonObject): string {
    const lines: string[] = [];
    if (!assessment.isCompatible) {
      const diagnostics = Array.isArray(assessment.diagnostics)
        ? assessment.diagnostics
        : [];
      for (const diagnostic of diagnostics) {
        lines.push(
          isPlainObject(diagnostic)
            ? String(diagnostic.message ?? "")
            : String(diagnostic)
        );
      }
      return lines.join("\n");
    }
    for (const side of ["before", "after"]) {
      const card = isPlainObject(assessment[side]) ? assessment[side] : {};
      lines.push(this._i18n.text(side === "before" ? "Before" : "After"));
      const tags = Array.isArray(card.tags) ? card.tags.map(String) : [];
      lines.push(tags.join(", "));
      const components = Array.isArray(card.components)
        ? (card.components as JsonObject[])
        : [];
      for (const component of components) {
        if (isPlainObject(component.effect)) {
          const effect = component.effect;
          lines.push(
            `${component.componentId ?? ""} · ${this._i18n.contentName(
              String(effect.type ?? effect.effectType ?? "effect")
            )} · ×${effect.repeat ?? 1}`
          );
          const params = Array.isArray(effect.parameters)
            ? (effect.parameters as JsonObject[])
            : [];
          for (const parameter of params) {
            lines.push(
              `${
                parameter.parameter ?? parameter.parameterId ?? "value"
              }: ${parameter.flatValue ?? "—"}`
            );
          }
        }
      }
    }
    return lines.join("\n");
  }
}
