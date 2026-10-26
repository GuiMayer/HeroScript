import { isPlainObject, JsonObject } from "../contracts/types";
import { I18n } from "./i18n";

/**
 * Player-facing summary of published numbers. Never evaluates a formula/bucket.
 */
export class CardCalculationPresenter {
  private _i18n: I18n;

  constructor(translator: I18n) {
    this._i18n = translator;
  }

  describe(facts: JsonObject, _compact = false): string[] {
    const lines: string[] = [];
    const effects = Array.isArray(facts.effects) ? (facts.effects as JsonObject[]) : [];
    const explanations = Array.isArray(facts.explanations)
      ? (facts.explanations as JsonObject[])
      : [];

    for (const effect of effects) {
      const parameters = Array.isArray(effect.parameters)
        ? (effect.parameters as JsonObject[])
        : [];
      for (const parameter of parameters) {
        const kind = String(parameter.parameter ?? "Amount");
        if (kind !== "Amount" && kind !== "CardCount") continue;
        const values = isPlainObject(parameter.values) ? parameter.values : {};
        const base = values.composedBase;
        const value =
          String(facts.faceValueMode ?? "") === "Source" ? values.source : base;
        const details: string[] = [];

        for (const explanation of explanations) {
          if (
            explanation.effectPath !== effect.effectPath ||
            String(explanation.parameter) !== kind
          ) {
            continue;
          }
          if (explanation.conditionPassed === false) {
            this._add(details, this._i18n.text("Condition not met"));
            continue;
          }
          const attributes = Array.isArray(explanation.attributes)
            ? (explanation.attributes as JsonObject[])
            : [];
          for (const attribute of attributes) {
            if (
              !attribute.eligible ||
              attribute.applied === false ||
              String(attribute.scope ?? "Actor") !== "Actor"
            ) {
              continue;
            }
            if (attribute.currentValue == null) continue;
            this._add(details, this._scaling(attribute, explanation));
          }
          const sources = Array.isArray(explanation.sources)
            ? (explanation.sources as JsonObject[])
            : [];
          for (const source of sources) {
            if (
              !source.applied ||
              ["Actor", "Target", "GameMode"].includes(String(source.kind ?? ""))
            ) {
              continue;
            }
            const id = source.definitionId != null ? String(source.definitionId) : "";
            if (id.length > 0) {
              this._add(
                details,
                this._i18n.text("Affected by %s", this._i18n.contentName(id))
              );
            }
          }
        }

        const changed =
          this._numeric(value) &&
          this._numeric(base) &&
          Math.abs(Number(value) - Number(base)) > 1e-6;
        if (details.length === 0 && !changed && this._numeric(value)) {
          continue;
        }
        const definition = isPlainObject(effect.definition) ? effect.definition : {};
        const heading = this._label(definition, kind);
        if (this._numeric(value)) {
          const suffix = changed
            ? ` (${this._i18n.text("base %s", this._number(base))})`
            : "";
          lines.push(`${heading}: ${this._number(value)}${suffix}`);
        } else {
          const prefix = this._numeric(base)
            ? `${this._i18n.text("base %s", this._number(base))} · `
            : "";
          lines.push(
            `${heading}: ${prefix}${this._i18n.text("Calculated in combat")}`
          );
        }
        lines.push(...details);
      }
    }
    return lines;
  }

  private _scaling(attribute: JsonObject, explanation: JsonObject): string {
    let name = this._i18n.contentName(String(attribute.attributeId ?? ""));
    const field = String(attribute.resourceField ?? "");
    if (field === "Maximum") {
      name = this._i18n.text("Maximum %s", name);
    } else if (field === "Current") {
      name = this._i18n.text("Current %s", name);
    }
    let operation = String(attribute.operation ?? "");
    if (!operation) {
      const buckets = Array.isArray(explanation.buckets)
        ? (explanation.buckets as JsonObject[])
        : [];
      for (const bucket of buckets) {
        if (String(bucket.bucketId ?? "") === String(attribute.bucket ?? "")) {
          operation = String(bucket.operation ?? "");
        }
      }
    }
    const contribution = attribute.contributionValue;
    if (operation === "AddPercent" && this._numeric(contribution)) {
      const sign = Number(contribution) >= 0 ? "+" : "";
      return `${name}: ${sign}${this._i18n.number(Number(contribution) * 100)}%`;
    }
    if (
      operation === "Add" &&
      "scale" in attribute &&
      Math.abs(Number(attribute.offset ?? 0)) < 1e-6
    ) {
      return `${this._i18n.number(Number(attribute.scale) * 100)}% ${name} (${this._number(
        attribute.currentValue
      )})`;
    }
    return this._i18n.text(
      "Scales with %s",
      `${name} (${this._number(attribute.currentValue)})`
    );
  }

  private _label(effect: JsonObject, parameter: string): string {
    if (parameter === "CardCount") return this._i18n.text("Cards");
    const type = String(effect.type ?? "");
    if (type === "DAMAGE") {
      return `${this._i18n.text("Damage")} · ${this._i18n.contentName(
        String(effect.targetResource ?? "")
      )}`;
    }
    if (type === "HEAL") {
      return `${this._i18n.text("Healing")} · ${this._i18n.contentName(
        String(effect.targetResource ?? "")
      )}`;
    }
    return this._i18n.contentName(String(effect.targetResource ?? "Amount"));
  }

  private _numeric(value: unknown): value is number {
    return typeof value === "number" && Number.isFinite(value);
  }

  private _number(value: unknown): string {
    return this._numeric(value) ? this._i18n.number(value) : "—";
  }

  private _add(lines: string[], text: string): void {
    if (text.length > 0 && !lines.includes(text)) {
      lines.push(text);
    }
  }
}
