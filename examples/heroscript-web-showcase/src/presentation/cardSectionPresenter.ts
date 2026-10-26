import { deepClone, isPlainObject, JsonObject } from "../contracts/types";
import { EffectDurationPresenter } from "./effectDurationPresenter";
import { I18n } from "./i18n";

export interface EffectSegment {
  text: string;
  value?: number;
  baseValue?: number | null;
}

export interface EffectRow {
  componentId?: string;
  segments: EffectSegment[];
}

export interface RequirementRow {
  text: string;
  passed: boolean | null;
}

export interface CardSections {
  rarityId: string;
  identityTags: string[];
  requirements: RequirementRow[];
  effectRows: EffectRow[];
  behaviors: string[];
  previewNote: string;
}

/**
 * Formats published definitions and preview facts. Never evaluates a formula or an effect.
 */
export class CardSectionPresenter {
  static readonly EFFECT_TYPES = [
    "DAMAGE",
    "HEAL",
    "MODIFY_RESOURCE",
    "APPLY_STATUS",
    "REMOVE_STATUS",
    "DISPEL_STATUS",
    "CARD_ZONE_FLOW",
    "APPLY_MODIFIER",
    "REMOVE_MODIFIER",
    "CONDENSE_STACKS",
    "MODIFY_ATTRIBUTE",
  ];
  static readonly TARGET_TYPES = [
    "SELF",
    "TARGET",
    "ALL_ENEMIES",
    "ALL_ALLIES",
    "RANDOM_ENEMY",
    "LOWEST_RESOURCE_ENEMY",
    "HIGHEST_RESOURCE_ENEMY",
  ];
  static readonly PARAMETER_TYPES = [
    "Amount",
    "StatusStacks",
    "StatusDuration",
    "ModifierStacks",
    "ModifierDuration",
    "CardCount",
  ];

  private _i18n: I18n;
  private _appearance: JsonObject;

  constructor(translator: I18n, appearance: JsonObject, _actors: JsonObject[] = []) {
    this._i18n = translator;
    this._appearance = deepClone(appearance);
  }

  sections(rawInspection: JsonObject, appearance: JsonObject): CardSections {
    let inspection = rawInspection;
    if (isPlainObject(inspection.presentation)) {
      inspection = deepClone(inspection);
      const facts = inspection.presentation as JsonObject;
      const components: JsonObject[] = [];
      const effects = Array.isArray(facts.effects) ? (facts.effects as JsonObject[]) : [];
      for (const effect of effects) {
        const definition = isPlainObject(effect.definition) ? deepClone(effect.definition) : {};
        definition.chainedEffects = [];
        components.push({
          type: "effect",
          componentId: effect.effectPath ?? "",
          effect: definition,
        });
      }
      const pairs: [string, string][] = [
        ["conditions", "condition"],
        ["dispositions", "disposition"],
        ["ruleOverrides", "effect_rule_override"],
      ];
      for (const [sourceKey, discriminator] of pairs) {
        const list = Array.isArray(facts[sourceKey]) ? (facts[sourceKey] as JsonObject[]) : [];
        for (const item of list) {
          const typed = deepClone(item);
          typed.type = discriminator;
          components.push(typed);
        }
      }
      inspection.effectiveBase = {
        components,
        tags: Array.isArray(facts.tags) ? facts.tags : [],
      };
      inspection.baseContainer = {
        rarity: facts.rarity ?? "Common",
      };
    }

    const original = isPlainObject(inspection.compiledContainer) ? inspection.compiledContainer : {};
    const effective = isPlainObject(inspection.effectiveBase) ? inspection.effectiveBase : {};
    const container = isPlainObject(inspection.baseContainer) ? inspection.baseContainer : {};
    const evaluation = isPlainObject(inspection.evaluation) ? inspection.evaluation : {};

    const rows: EffectRow[] = [];
    const requirements: RequirementRow[] = [];
    const behaviors: string[] = [];

    const components = Array.isArray(effective.components) ? (effective.components as JsonObject[]) : [];
    for (const component of components) {
      if (isPlainObject(component.effect)) {
        this._effectRows(
          component.effect,
          String(component.componentId ?? ""),
          original,
          inspection,
          rows,
          behaviors
        );
      } else if (component.type === "condition") {
        const conditionsMap = isPlainObject(appearance.conditions) ? appearance.conditions : {};
        let label = String(
          conditionsMap[String(component.componentId ?? "")] ?? component.failureReason ?? ""
        );
        if (!label || label === "Condition not met") {
          label = "Special requirement — inspect for details";
        }
        let passed: boolean | null = null;
        const evalConditions = Array.isArray(evaluation.conditions)
          ? (evaluation.conditions as JsonObject[])
          : [];
        for (const condition of evalConditions) {
          if (condition.componentId === component.componentId && typeof condition.passed === "boolean") {
            passed = condition.passed;
          }
        }
        requirements.push({ text: this._i18n.text(label), passed });
      } else if (component.type === "disposition") {
        const flowId = String(component.cardZoneResolutionFlowId ?? "");
        const labels = isPlainObject(this._appearance.card_flow_labels)
          ? this._appearance.card_flow_labels
          : {};
        if (flowId in labels) {
          behaviors.push(this._i18n.text(String(labels[flowId])));
        }
      } else if (
        component.type === "effect_rule_override" &&
        isPlainObject(component.rule) &&
        component.rule.critical === false
      ) {
        CardSectionPresenter._addUnique(behaviors, this._i18n.text("Cannot crit"));
      }
    }

    const tags: string[] = [];
    const tagLabels = isPlainObject(this._appearance.card_tag_labels)
      ? this._appearance.card_tag_labels
      : {};
    const sourceTags = Array.isArray(effective.tags)
      ? effective.tags
      : Array.isArray(container.tags)
      ? container.tags
      : [];
    for (const tag of sourceTags) {
      const key = String(tag);
      if (key in tagLabels) {
        tags.push(this._i18n.text(String(tagLabels[key])));
      }
    }

    const rarityId = CardSectionPresenter.enumName(
      container.rarity ?? appearance.rarityId ?? "",
      ["Common", "Uncommon", "Rare", "Legendary"]
    ).toLowerCase();

    return {
      rarityId,
      identityTags: tags,
      requirements,
      effectRows: rows,
      behaviors,
      previewNote: "",
    };
  }

  private _effectRows(
    effect: JsonObject,
    componentId: string,
    original: JsonObject,
    inspection: JsonObject,
    rows: EffectRow[],
    behaviors: string[],
    depth = 0
  ): void {
    if (depth > 32) return;
    const type = CardSectionPresenter.enumName(
      effect.type ?? effect.effectType ?? "",
      CardSectionPresenter.EFFECT_TYPES
    );
    let resource = this._i18n.contentName(String(effect.targetResource ?? ""));
    const target = this._targetText(effect);
    let template = "";
    let parameter = "Amount";

    switch (type) {
      case "DAMAGE":
        template = "Reduce {value} {resource} on {target}.";
        break;
      case "HEAL":
        template = "Restore {value} {resource} on {target}.";
        break;
      case "MODIFY_RESOURCE": {
        const operation = CardSectionPresenter.enumName(effect.operation ?? "ADD", [
          "ADD",
          "SUBTRACT",
          "SET",
        ]);
        const opTemplates: Record<string, string> = {
          ADD: "Add {value} {resource} to {target}.",
          SUBTRACT: "Reduce {value} {resource} on {target}.",
          SET: "Set {resource} to {value} on {target}.",
        };
        template = opTemplates[operation] ?? "";
        const field = CardSectionPresenter.enumName(effect.resourceField ?? "Current", [
          "Current",
          "Minimum",
          "Maximum",
        ]);
        if (field !== "Current") {
          resource += " " + this._i18n.text(field);
        }
        break;
      }
      case "APPLY_STATUS":
        template = "Apply {value} {resource} to {target}.";
        resource = this._i18n.contentName(String(effect.statusId ?? ""));
        parameter = "StatusStacks";
        break;
      case "APPLY_MODIFIER":
        template = "Apply {value} {resource} to {target}.";
        resource = this._i18n.contentName(String(effect.modifierId ?? ""));
        parameter = "ModifierStacks";
        break;
      case "CARD_ZONE_FLOW": {
        template = "Move/create {value} cards: {resource}.";
        const flowLabels = isPlainObject(this._appearance.card_flow_labels)
          ? this._appearance.card_flow_labels
          : {};
        const flowId = String(effect.cardZoneFlowId ?? "");
        resource = this._i18n.text(
          String(flowLabels[flowId] ?? "Configured card flow — inspect")
        );
        parameter = "CardCount";
        break;
      }
      case "MODIFY_ATTRIBUTE": {
        template = "Modify {resource} by {value} on {target}.";
        const attrMutation = isPlainObject(effect.attributeMutation)
          ? effect.attributeMutation
          : {};
        resource = this._i18n.contentName(String(attrMutation.attributeId ?? "attribute"));
        break;
      }
      case "REMOVE_STATUS":
        rows.push(
          CardSectionPresenter._plain(
            this._i18n.text(
              "Remove %s from %s.",
              this._i18n.contentName(String(effect.statusId ?? "")),
              target
            )
          )
        );
        break;
      case "REMOVE_MODIFIER":
        rows.push(
          CardSectionPresenter._plain(
            this._i18n.text(
              "Remove %s from %s.",
              this._i18n.contentName(String(effect.modifierId ?? "")),
              target
            )
          )
        );
        break;
      case "DISPEL_STATUS":
        rows.push(
          CardSectionPresenter._plain(
            this._i18n.text("Dispel configured statuses from %s.", target)
          )
        );
        break;
      case "CONDENSE_STACKS": {
        const recipeId = String(effect.condensationRecipeId ?? "");
        const condLabels = isPlainObject(this._appearance.condensation_labels)
          ? this._appearance.condensation_labels
          : {};
        const description = String(
          condLabels[recipeId] ?? "Consume selected stacks in one proc — inspect"
        );
        rows.push(CardSectionPresenter._plain(this._i18n.text(description)));
        CardSectionPresenter._addUnique(behaviors, this._i18n.text("Condense"));
        break;
      }
      default:
        rows.push(
          CardSectionPresenter._plain(this._i18n.text("Configured effect — inspect for details"))
        );
        break;
    }

    if (template.length > 0) {
      const value = this._numeric(effect, componentId, parameter, original, inspection, depth === 0);
      const formatted = this._i18n
        .text(template)
        .replace("{resource}", resource)
        .replace("{target}", target);
      const splitIdx = formatted.indexOf("{value}");
      const before = splitIdx >= 0 ? formatted.slice(0, splitIdx) : formatted;
      const after = splitIdx >= 0 ? formatted.slice(splitIdx + "{value}".length) : "";
      const segment: EffectSegment = { text: String(value.text ?? "—") };
      if ("value" in value && typeof value.value === "number") {
        segment.value = value.value;
        segment.baseValue = typeof value.baseValue === "number" ? value.baseValue : null;
      }
      rows.push({
        componentId,
        segments: [{ text: before }, segment, { text: after }],
      });
    }

    if (effect.condition != null && String(effect.condition).length > 0) {
      const metadata = isPlainObject(effect.metadata) ? effect.metadata : {};
      rows.push(
        CardSectionPresenter._plain(
          this._i18n.text(
            String(metadata.conditionText ?? "Conditional effect — inspect for details")
          )
        )
      );
    }
    const chance = Number(effect.chance ?? 1.0);
    if (Number.isFinite(chance) && chance < 1.0) {
      rows.push(
        CardSectionPresenter._plain(
          this._i18n.text("Chance: %s%%", this._i18n.number(chance * 100))
        )
      );
    }
    const repeat = Math.trunc(Number(effect.repeat ?? 1));
    if (repeat > 1) {
      CardSectionPresenter._addUnique(behaviors, this._i18n.text("Repeat: %s", repeat));
    }
    const parameters = Array.isArray(effect.parameters) ? (effect.parameters as JsonObject[]) : [];
    for (const numeric of parameters) {
      if (isPlainObject(numeric.distribution)) {
        CardSectionPresenter._addUnique(behaviors, this._i18n.text("Shared sequence budget"));
      }
    }
    if (isPlainObject(effect.continuation)) {
      CardSectionPresenter._addUnique(
        behaviors,
        this._i18n.text(
          "Transfer excess · up to %s hops",
          Math.trunc(Number(effect.continuation.maximumHops ?? 1))
        )
      );
    }

    const durationPairs: [string, string][] = [
      ["statusDuration", "StatusDuration"],
      ["modifierDuration", "ModifierDuration"],
    ];
    for (const [fieldKey, paramKind] of durationPairs) {
      const hasParam = parameters.some(
        (item) =>
          CardSectionPresenter.enumName(
            item.parameter,
            CardSectionPresenter.PARAMETER_TYPES
          ) === paramKind
      );
      if (effect[fieldKey] != null || hasParam) {
        const duration = this._numeric(
          effect,
          componentId,
          paramKind,
          original,
          inspection,
          depth === 0
        );
        const definition = isPlainObject(effect.metadata) ? effect.metadata : {};
        rows.push(
          CardSectionPresenter._plain(
            EffectDurationPresenter.describe(duration.value, this._i18n, definition)
          )
        );
      }
    }

    const chained = Array.isArray(effect.chainedEffects)
      ? (effect.chainedEffects as JsonObject[])
      : [];
    for (const child of chained) {
      const timing = CardSectionPresenter.enumName(
        child.childTiming ?? "AfterParentImpact",
        ["AfterParentImpact", "BeforeParentImpact"]
      );
      rows.push(
        CardSectionPresenter._plain(
          this._i18n.text(
            timing === "BeforeParentImpact" ? "Before impact:" : "After impact:"
          )
        )
      );
      rows.push(
        CardSectionPresenter._plain(
          this._i18n.text("Chained effect base — inspect executed impacts")
        )
      );
      this._effectRows(child, componentId, original, inspection, rows, behaviors, depth + 1);
    }
  }

  private _numeric(
    effect: JsonObject,
    componentId: string,
    parameter: string,
    original: JsonObject,
    inspection: JsonObject,
    isRoot: boolean
  ): { text: string; value?: number; baseValue?: number | null } {
    if (isPlainObject(inspection.presentation)) {
      const facts = inspection.presentation;
      const effects = Array.isArray(facts.effects) ? (facts.effects as JsonObject[]) : [];
      for (const item of effects) {
        if (String(item.effectPath ?? "") !== componentId) continue;
        const publishedList = Array.isArray(item.parameters)
          ? (item.parameters as JsonObject[])
          : [];
        for (const published of publishedList) {
          if (
            CardSectionPresenter.enumName(
              published.parameter,
              CardSectionPresenter.PARAMETER_TYPES
            ) !== parameter
          ) {
            continue;
          }
          const values = isPlainObject(published.values) ? published.values : {};
          const source = String(facts.faceValueMode ?? "ComposedBase") === "Source";
          const value = values[source ? "source" : "composedBase"];
          if (value == null || typeof value !== "number") {
            return { text: this._i18n.text("Calculated — inspect") };
          }
          const rawBase = values[source ? "composedBase" : "original"];
          return {
            text: this._i18n.number(value),
            value,
            baseValue: typeof rawBase === "number" ? rawBase : null,
          };
        }
      }
      return { text: this._i18n.text("Calculated — inspect") };
    }

    const fieldMap: Record<string, string> = {
      Amount: "flatValue",
      StatusStacks: "statusStacks",
      StatusDuration: "statusDuration",
      ModifierStacks: "modifierStacks",
      ModifierDuration: "modifierDuration",
      CardCount: "cardCount",
    };
    const field = fieldMap[parameter] ?? "flatValue";
    let current = this._flatParameter(effect, parameter, field);
    let baseline = current;

    if (isRoot) {
      const origComponents = Array.isArray(original.components)
        ? (original.components as JsonObject[])
        : [];
      for (const component of origComponents) {
        if (
          String(component.componentId ?? "") === componentId &&
          isPlainObject(component.effect)
        ) {
          baseline = this._flatParameter(component.effect, parameter, field);
        }
      }
      const published: JsonObject[] = [];
      const previewSteps = Array.isArray(inspection.previewSteps)
        ? (inspection.previewSteps as JsonObject[])
        : [];
      for (const step of previewSteps) {
        const prov = isPlainObject(step.provenance) ? step.provenance : {};
        if (String(prov.componentId ?? "") !== componentId || !step.applied) continue;
        if (isPlainObject(step.identity) && step.identity.parentProcId != null) continue;
        if (isPlainObject(step.continuation)) continue;
        let foundParameter = false;
        const budgets = Array.isArray(step.sequenceBudgets)
          ? (step.sequenceBudgets as JsonObject[])
          : [];
        for (const budget of budgets) {
          if (
            CardSectionPresenter.enumName(
              budget.parameter,
              CardSectionPresenter.PARAMETER_TYPES
            ) === parameter &&
            isPlainObject(budget.capture)
          ) {
            published.push(budget.capture);
            foundParameter = true;
          }
        }
        if (foundParameter) continue;
        const stepParams = Array.isArray(step.parameters)
          ? (step.parameters as JsonObject[])
          : [];
        for (const resolved of stepParams) {
          if (
            CardSectionPresenter.enumName(
              resolved.parameter,
              CardSectionPresenter.PARAMETER_TYPES
            ) === parameter &&
            isPlainObject(resolved.calculation)
          ) {
            published.push(resolved.calculation);
            foundParameter = true;
          }
        }
        if (parameter === "Amount" && !foundParameter && isPlainObject(step.calculation)) {
          published.push(step.calculation);
        }
      }
      if (published.length > 0) {
        const sourceValues = published
          .map((calc) => this._sourceValue(calc))
          .filter((v): v is number => v !== null);
        if (sourceValues.length > 0) {
          current = sourceValues[0];
          if (sourceValues.some((v) => v !== current)) {
            return { text: this._i18n.text("Calculated — inspect") };
          }
          if (baseline == null && typeof published[0].baseValue === "number") {
            baseline = published[0].baseValue;
          }
        }
      }
    }

    if (typeof current !== "number" || !Number.isFinite(current)) {
      return { text: this._i18n.text("Calculated — inspect") };
    }
    return {
      text: this._i18n.number(current),
      value: current,
      baseValue: typeof baseline === "number" ? baseline : null,
    };
  }

  private _flatParameter(effect: JsonObject, parameter: string, field: string): number | null {
    const parameters = Array.isArray(effect.parameters) ? (effect.parameters as JsonObject[]) : [];
    for (const item of parameters) {
      if (
        CardSectionPresenter.enumName(item.parameter, CardSectionPresenter.PARAMETER_TYPES) ===
        parameter
      ) {
        const hasFormula = item.formulaValue != null && String(item.formulaValue).length > 0;
        if (!hasFormula && item.inputQuantityId == null && typeof item.flatValue === "number") {
          return item.flatValue;
        }
        return null;
      }
    }
    if (
      parameter === "Amount" &&
      effect.formulaValue != null &&
      String(effect.formulaValue).length > 0
    ) {
      return null;
    }
    return typeof effect[field] === "number" ? (effect[field] as number) : null;
  }

  private _sourceValue(calculation: JsonObject): number | null {
    const sourceStages: string[] = [];
    const quantity = isPlainObject(calculation.quantity) ? calculation.quantity : {};
    const stages = Array.isArray(quantity.incorporatedStages)
      ? (quantity.incorporatedStages as JsonObject[])
      : [];
    for (const stage of stages) {
      if (String(stage.scope ?? "") !== "Actor") break;
      sourceStages.push(String(stage.stageId ?? ""));
    }
    if (sourceStages.length === 0) return null;
    let value: number | null = null;
    const buckets = Array.isArray(calculation.buckets)
      ? (calculation.buckets as JsonObject[])
      : [];
    for (const bucket of buckets) {
      if (sourceStages.includes(String(bucket.stageId ?? "")) && typeof bucket.output === "number") {
        value = bucket.output;
      }
    }
    if (
      calculation.captureOnly === true &&
      sourceStages.length === stages.length &&
      typeof calculation.value === "number"
    ) {
      value = calculation.value;
    }
    return value;
  }

  private _targetText(effect: JsonObject): string {
    const target = CardSectionPresenter.enumName(
      effect.target ?? "TARGET",
      CardSectionPresenter.TARGET_TYPES
    );
    const labels: Record<string, string> = {
      SELF: "yourself",
      TARGET: "a selected target",
      ALL_ENEMIES: "all enemies",
      ALL_ALLIES: "all allies",
      RANDOM_ENEMY: "a random enemy",
      LOWEST_RESOURCE_ENEMY: "the enemy with the lowest selected resource",
      HIGHEST_RESOURCE_ENEMY: "the enemy with the highest selected resource",
    };
    return this._i18n.text(labels[target] ?? "configured targets");
  }

  static enumName(value: unknown, names: string[]): string {
    if (typeof value === "number" && Number.isFinite(value)) {
      const index = Math.trunc(value);
      return index >= 0 && index < names.length ? names[index] : "";
    }
    return value != null ? String(value) : "";
  }

  private static _plain(text: string): EffectRow {
    return { segments: [{ text }] };
  }

  private static _addUnique(items: string[], value: string): void {
    if (!items.includes(value)) {
      items.push(value);
    }
  }
}
