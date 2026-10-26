import { deepClone, isPlainObject, JsonObject } from "../contracts/types";
import { CardSectionPresenter } from "./cardSectionPresenter";
import { I18n } from "./i18n";

export interface CostToken {
  resourceId: string;
  amount: number;
  symbol: string;
  name: string;
}

/**
 * One passive adapter for base and source facts. No formula, legality, RNG or target simulation.
 */
export class CardFactsPresenter {
  private _i18n: I18n;
  private _appearance: JsonObject;

  constructor(translator: I18n, appearance: JsonObject) {
    this._i18n = translator;
    this._appearance = deepClone(appearance);
  }

  project(facts: JsonObject): JsonObject {
    const identity = isPlainObject(facts.identity) ? facts.identity : {};
    const id = String(identity.definitionId ?? "");
    const cardsMap = isPlainObject(this._appearance.cards) ? this._appearance.cards : {};
    const appearance = isPlainObject(cardsMap[id]) ? cardsMap[id] : {};

    const sections = new CardSectionPresenter(this._i18n, this._appearance, []).sections(
      { presentation: facts },
      appearance
    );

    const costs: JsonObject[] = [];
    const alternatives: CostToken[][] = [];
    let costsKnown = true;

    const costComponents = Array.isArray(facts.costs) ? (facts.costs as JsonObject[]) : [];
    for (const component of costComponents) {
      const innerCosts = isPlainObject(component.costs) ? component.costs : {};
      const list = Array.isArray(innerCosts.costs) ? (innerCosts.costs as JsonObject[]) : [];
      costs.push(...list);
      if (list.some((cost) => cost.formula != null && String(cost.formula).length > 0)) {
        costsKnown = false;
      }
      const altList = Array.isArray(innerCosts.alternativeCosts)
        ? (innerCosts.alternativeCosts as JsonObject[])
        : [];
      for (const option of altList) {
        const optionCosts = Array.isArray(option.costs) ? (option.costs as JsonObject[]) : [];
        if (optionCosts.some((cost) => cost.formula != null && String(cost.formula).length > 0)) {
          costsKnown = false;
        }
        alternatives.push(this.tokens(optionCosts));
      }
    }

    const summary = sections.effectRows
      .map((row) => row.segments.map((segment) => String(segment.text ?? "")).join(""))
      .join("\n");

    const tone = String(appearance.tone ?? "skill");
    const model: JsonObject = {
      definitionId: id,
      cardInstanceId: identity.cardInstanceId ?? "",
      name: this._i18n.contentName(id, String(appearance.name ?? id)),
      tone,
      cardType: this._i18n.text(tone.toUpperCase()),
      summary,
      effects: summary ? summary.split("\n").filter((s) => s.length > 0) : [],
      cost: "",
      costs: costsKnown ? this.tokens(costs) : [],
      costsKnown,
      costUnknownLabel: this._i18n.text("Contextual cost — inspect"),
      costOptions: alternatives,
      alternativeCostCount: alternatives.length,
      presentationFacts: deepClone(facts),
      ...sections,
    };

    const badges: JsonObject[] = [];
    const seen: string[] = [];
    const permanentChanges = Array.isArray(facts.permanentChanges)
      ? (facts.permanentChanges as JsonObject[])
      : [];
    for (const change of permanentChanges) {
      const category = String(change.category ?? "Base");
      if (seen.includes(category)) continue;
      seen.push(category);
      badges.push({
        kind: category === "Base" ? "upgraded" : "modified",
        label: this._i18n.text(category === "Base" ? "UPGRADED" : "MODIFIED"),
        symbol: category === "Base" ? "↑" : "~",
        sources: [],
      });
    }

    const contributions = Array.isArray(facts.componentContributions)
      ? (facts.componentContributions as JsonObject[])
      : [];
    for (const source of contributions) {
      let label = String(source.nameKey ?? "");
      if (!label) label = "COMPONENT GRANT";
      const description = String(source.descriptionKey ?? "");
      badges.push({
        kind: "contribution",
        label: this._i18n.text(label),
        symbol: "+",
        sources: description ? [this._i18n.text(description)] : [],
      });
    }

    model.changeBadges = badges;
    model.upgraded = seen.length > 0;

    const influences = Array.isArray(facts.influences) ? (facts.influences as JsonObject[]) : [];
    const behaviors = Array.isArray(model.behaviors) ? (model.behaviors as string[]) : [];
    for (const influence of influences) {
      let text = `${this._i18n.contentName(
        String(influence.channel ?? "")
      )} · ${this._i18n.contentName(String(influence.bucket ?? ""))}: `;
      text +=
        influence.value != null && influence.formula == null
          ? this._i18n.number(Number(influence.value))
          : this._i18n.text("Calculated — inspect");
      behaviors.push(text);
    }
    model.behaviors = behaviors;
    model.rarity = this._i18n.text(String(model.rarityId ?? "").toUpperCase());
    return model;
  }

  tokens(costs: JsonObject[]): CostToken[] {
    const result: CostToken[] = [];
    const resourcesMap = isPlainObject(this._appearance.resources)
      ? this._appearance.resources
      : {};
    for (const cost of costs) {
      const id = String(cost.resourceId ?? "");
      const style = isPlainObject(resourcesMap[id]) ? resourcesMap[id] : {};
      result.push({
        resourceId: id,
        amount: Number(cost.amount ?? 0),
        symbol: String(style.symbol ?? id.slice(0, 1).toUpperCase()),
        name: this._i18n.contentName(id),
      });
    }
    return result;
  }
}
