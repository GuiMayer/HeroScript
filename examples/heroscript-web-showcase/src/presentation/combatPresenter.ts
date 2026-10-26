import { SnapshotProjection } from "../contracts/snapshotProjection";
import { deepClone, deepEqual, isPlainObject, JsonObject } from "../contracts/types";
import { CardCalculationPresenter } from "./cardCalculationPresenter";
import { CardFactsPresenter, CostToken } from "./cardFactsPresenter";
import { CardSectionPresenter } from "./cardSectionPresenter";
import { CardZonePresenter } from "./cardZonePresenter";
import { I18n } from "./i18n";
import { ActionOptionModel, ActorModel, CardModel, CombatModel, HeaderModel } from "./viewModels";

/**
 * Passive projection of one engine snapshot. Never evaluates effects or issues commands.
 */
export class CombatPresenter {
  private _i18n: I18n;
  private _run: JsonObject;
  private _combat: JsonObject;
  private _actions: JsonObject[];
  private _cards: Record<string, JsonObject> = {};
  private _actors: Record<string, JsonObject> = {};
  private _byCard: Record<string, JsonObject[]> = {};
  private _evaluations: Record<string, JsonObject> = {};
  private _appearance: JsonObject = {};
  private _zoneSnapshot: JsonObject = {};
  private _sections: CardSectionPresenter;
  private _baseFacts: Record<string, JsonObject> = {};

  constructor(
    run: JsonObject,
    combat: JsonObject,
    actions: JsonObject[],
    translator: I18n,
    appearance: JsonObject = {},
    zoneSnapshot: JsonObject = {}
  ) {
    this._i18n = translator;
    this._run = SnapshotProjection.run(run);
    this._combat = SnapshotProjection.combat(combat);
    this._actions = deepClone(actions);
    this._appearance = isPlainObject(appearance) ? deepClone(appearance) : {};
    this._zoneSnapshot = isPlainObject(zoneSnapshot) ? deepClone(zoneSnapshot) : {};

    const zones = Array.isArray(this._zoneSnapshot.zones)
      ? (this._zoneSnapshot.zones as JsonObject[])
      : [];
    if (zones.length > 0) {
      for (const zone of zones) {
        const cards = Array.isArray(zone.cards) ? (zone.cards as JsonObject[]) : [];
        for (const card of cards) {
          this._cards[String(card.cardInstanceId ?? "")] = card;
        }
      }
    } else {
      const deck = isPlainObject(this._run.deck) ? this._run.deck : {};
      const cardInstances = Array.isArray(deck.cardInstances)
        ? (deck.cardInstances as JsonObject[])
        : [];
      for (const card of cardInstances) {
        this._cards[String(card.cardInstanceId ?? "")] = card;
      }
    }

    const actors = Array.isArray(this._combat.actors)
      ? (this._combat.actors as JsonObject[])
      : [];
    for (const actor of actors) {
      this._actors[String(actor.instanceId ?? "")] = actor;
    }

    this._sections = new CardSectionPresenter(this._i18n, this._appearance, this.actors());

    for (const candidate of this._actions) {
      const command = isPlainObject(candidate.command) ? candidate.command : {};
      const id = String(command.cardInstanceId ?? "");
      if (!this._byCard[id]) {
        this._byCard[id] = [];
      }
      this._byCard[id].push(candidate);
    }
  }

  acceptPresentations(cards: JsonObject[]): void {
    this._baseFacts = {};
    for (const facts of cards) {
      const identity = isPlainObject(facts.identity) ? facts.identity : {};
      const id = String(identity.cardInstanceId ?? "");
      if (id.length > 0) {
        this._baseFacts[id] = deepClone(facts);
      }
    }
  }

  actors(): JsonObject[] {
    const list = Array.isArray(this._combat.actors)
      ? (this._combat.actors as JsonObject[])
      : [];
    return list.map((actor) => new ActorModel(actor).toDictionary());
  }

  candidateForCard(instanceId: string, targetId = ""): JsonObject {
    for (const candidate of this._actions) {
      const command = isPlainObject(candidate.command) ? candidate.command : {};
      if (String(command.cardInstanceId ?? "") !== instanceId) continue;
      const targets = Array.isArray(command.targetIds)
        ? command.targetIds.map(String)
        : [];
      if (
        !targetId ||
        targets.includes(targetId) ||
        String(command.targetId ?? "") === targetId
      ) {
        return deepClone(candidate);
      }
    }
    return {};
  }

  systemCandidate(actionTypes: string[]): JsonObject {
    for (const candidate of this._actions) {
      const command = isPlainObject(candidate.command) ? candidate.command : {};
      if (
        String(candidate.source ?? "") === "System" &&
        actionTypes.includes(String(command.actionType ?? ""))
      ) {
        return deepClone(candidate);
      }
    }
    return {};
  }

  preview(candidate: JsonObject): string {
    if (Object.keys(candidate).length === 0) {
      return this._i18n.text("No legal actions for this card in the current state.");
    }
    const lines: string[] = [this._i18n.text("Cost: %s", this.costText(candidate))];
    if (Boolean(candidate.outcomeUncertain)) {
      lines.push(this._i18n.text("The outcome depends on the next responses."));
    }
    const applications = Array.isArray(candidate.applications)
      ? (candidate.applications as JsonObject[])
      : [];
    for (const application of applications) {
      lines.push(this.applicationText(application));
    }
    return lines.join("  •  ");
  }

  cardInstance(instanceId: string): JsonObject {
    return this._cards[instanceId] ? deepClone(this._cards[instanceId]) : {};
  }

  intentFor(actorId: string): string {
    const activation = isPlainObject(this._combat.activation)
      ? this._combat.activation
      : {};
    const intents = Array.isArray(activation.intents)
      ? (activation.intents as JsonObject[])
      : [];
    for (const intent of intents) {
      if (String(intent.actorId ?? "") === actorId) {
        const lines: string[] = [
          this._i18n.contentName(String(intent.actionId ?? intent.actionType ?? "")),
        ];
        const previewApps = Array.isArray(intent.previewApplications)
          ? (intent.previewApplications as JsonObject[])
          : [];
        for (const application of previewApps) {
          lines.push(this.compactApplication(application));
        }
        if (Boolean(intent.previewUncertain)) {
          lines.push(this._i18n.text("Outcome uncertain"));
        }
        return lines.join("\n");
      }
    }
    return "";
  }

  candidates(id: string): JsonObject[] {
    return this._byCard[id] ? deepClone(this._byCard[id]) : [];
  }

  targets(candidate: JsonObject): string[] {
    const command = isPlainObject(candidate.command) ? candidate.command : {};
    let targets = Array.isArray(command.targetIds)
      ? command.targetIds.map(String)
      : [];
    if (targets.length === 0 && command.targetId != null) {
      targets = [String(command.targetId)];
    }
    return [...targets];
  }

  actorName(id: string): string {
    const actors = Array.isArray(this._combat.actors)
      ? (this._combat.actors as JsonObject[])
      : [];
    for (const actor of actors) {
      if (String(actor.instanceId ?? "") === id) {
        return this._i18n.contentName(
          String(actor.definitionId ?? ""),
          String(actor.name ?? id)
        );
      }
    }
    return id;
  }

  costText(candidate: JsonObject): string {
    const parts: string[] = [];
    const costs = Array.isArray(candidate.costs)
      ? (candidate.costs as JsonObject[])
      : [];
    for (const cost of costs) {
      parts.push(
        `${this._i18n.number(Number(cost.amount ?? 0))} ${this._i18n.contentName(
          String(cost.resourceId ?? "")
        )}`
      );
    }
    return parts.length > 0 ? parts.join(" + ") : "0";
  }

  cardViewModel(instanceId: string): JsonObject {
    const card = this.cardInstance(instanceId);
    const definitionId = String(card.definitionId ?? "");
    const cardsMap = isPlainObject(this._appearance.cards)
      ? this._appearance.cards
      : {};
    const appearance = isPlainObject(cardsMap[definitionId])
      ? cardsMap[definitionId]
      : {};
    const candidate = this.candidateForCard(instanceId);
    const inspection = this._evaluations[instanceId] ?? {};
    const base = isPlainObject(inspection.baseContainer)
      ? inspection.baseContainer
      : {};
    const upgrades = Array.isArray(inspection.appliedUpgrades)
      ? inspection.appliedUpgrades
      : Array.isArray(card.upgrades)
      ? card.upgrades
      : [];
    const name = this._i18n.contentName(
      definitionId,
      String(appearance.name ?? definitionId)
    );
    const tone = String(appearance.tone ?? "skill");
    const tags = Array.isArray(base.tags) ? base.tags.map(String) : [];
    const cardType = this._cardType(tags, tone);
    let summary = String(
      appearance.text ?? this._i18n.text("See inspection for details")
    );
    const changes = this._changeBadges(inspection, upgrades);
    const inspectedEvaluation = isPlainObject(inspection.evaluation)
      ? inspection.evaluation
      : {};
    const publishedCosts =
      Object.keys(candidate).length > 0 && Array.isArray(candidate.costs)
        ? (candidate.costs as JsonObject[])
        : Array.isArray(inspectedEvaluation.costs)
        ? (inspectedEvaluation.costs as JsonObject[])
        : [];

    const effectiveBase = isPlainObject(inspection.effectiveBase)
      ? inspection.effectiveBase
      : {};
    const effComponents = Array.isArray(effectiveBase.components)
      ? (effectiveBase.components as JsonObject[])
      : [];
    const requiresAlternative = effComponents.some((component) => {
      if (component.type !== "cost" || !isPlainObject(component.costs)) return false;
      const alt = component.costs.alternativeCosts;
      return Array.isArray(alt) && alt.length > 0;
    });

    const hasInspection = Object.keys(inspection).length > 0;
    const hasCandidate = Object.keys(candidate).length > 0;
    const costsKnown =
      (hasInspection || hasCandidate) &&
      (!requiresAlternative ||
        hasCandidate ||
        publishedCosts.some((cost) => cost.optionId != null));

    const sections = this._sections.sections(inspection, appearance);
    if (sections.effectRows.length > 0) {
      summary = sections.effectRows
        .map((row) =>
          row.segments.map((segment) => String(segment.text ?? "")).join("")
        )
        .join("\n");
    }

    const costOptions: CostToken[][] = [];
    for (const option of this.candidates(instanceId)) {
      const optCosts = Array.isArray(option.costs)
        ? (option.costs as JsonObject[])
        : [];
      const tokens = this._costTokens(optCosts);
      if (!costOptions.some((existing) => deepEqual(existing, tokens))) {
        costOptions.push(tokens);
      }
    }

    const result: JsonObject = {
      cardInstanceId: instanceId,
      definitionId,
      tone,
      name,
      upgraded: upgrades.length > 0,
      cardType: this._i18n.text(cardType),
      rarity: this._i18n.text(String(base.rarity ?? "").toUpperCase()),
      summary,
      effects: summary ? summary.split("\n").filter((s) => s.length > 0) : [],
      costs: this._costTokens(publishedCosts),
      costsKnown,
      costUnknownLabel: this._i18n.text("Cost unavailable"),
      cost: hasCandidate || hasInspection ? "" : this._i18n.text("Cost — inspect"),
      costOptions,
      availability: hasCandidate
        ? this._i18n.text("SELECT TO PLAY")
        : this.unavailableReason(instanceId),
      changeBadges: changes,
      inspectionText: hasInspection ? this.inspectionText(instanceId) : "",
      alternativeCostCount: costOptions.length,
      artPlaceholder: true,
      ...sections,
    };

    result.rarity = this._i18n.text(String(result.rarityId ?? "").toUpperCase());

    const facts = isPlainObject(inspection.presentation)
      ? inspection.presentation
      : this._baseFacts[instanceId] ?? {};
    if (Object.keys(facts).length > 0) {
      const projected = new CardFactsPresenter(
        this._i18n,
        this._appearance
      ).project(facts);
      Object.assign(result, projected);
      if (
        !result.costsKnown &&
        Array.isArray(inspectedEvaluation.costs) &&
        inspectedEvaluation.costs.length > 0
      ) {
        result.costs = this._costTokens(inspectedEvaluation.costs as JsonObject[]);
        result.costsKnown = true;
      }
    }

    return new CardModel(result).toDictionary();
  }

  private _cardType(tags: string[], tone: string): string {
    for (const kind of ["attack", "skill", "power"]) {
      if (tags.includes(kind)) {
        return kind.toUpperCase();
      }
    }
    return tone.toUpperCase();
  }

  private _costTokens(costs: JsonObject[]): CostToken[] {
    const result: CostToken[] = [];
    const resources = isPlainObject(this._appearance.resources)
      ? this._appearance.resources
      : {};
    for (const cost of costs) {
      const resourceId = String(cost.resourceId ?? "");
      const style = isPlainObject(resources[resourceId])
        ? resources[resourceId]
        : {};
      const symbol = String(
        style.symbol ?? resourceId.slice(0, 1).toUpperCase()
      );
      result.push({
        resourceId,
        amount: Number(cost.amount ?? 0),
        symbol: symbol.length > 0 ? symbol : "•",
        name: this._i18n.contentName(resourceId),
      });
    }
    return result;
  }

  private _changeBadges(inspection: JsonObject, upgrades: unknown[]): JsonObject[] {
    const badges: JsonObject[] = [];
    if (upgrades.length > 0) {
      badges.push({
        kind: "upgrade",
        label: this._i18n.text("UPGRADED"),
        symbol: "↑",
      });
    }
    if (Object.keys(inspection).length === 0) {
      return badges;
    }

    let contextualChange = false;
    const sourceNames: string[] = [];
    const calculations = Array.isArray(inspection.calculations)
      ? (inspection.calculations as JsonObject[])
      : [];
    for (const calculation of calculations) {
      const quantity = isPlainObject(calculation.quantity)
        ? calculation.quantity
        : {};
      const stages = Array.isArray(quantity.incorporatedStages)
        ? (quantity.incorporatedStages as JsonObject[])
        : [];
      const outcomeStages = stages
        .filter((stage) =>
          ["Target", "Shared"].includes(String(stage.scope ?? ""))
        )
        .map((stage) => String(stage.stageId ?? ""));

      const buckets = Array.isArray(calculation.buckets)
        ? (calculation.buckets as JsonObject[])
        : [];
      for (const bucket of buckets) {
        if (outcomeStages.includes(String(bucket.stageId ?? ""))) continue;
        const contributions = Array.isArray(bucket.contributions)
          ? (bucket.contributions as JsonObject[])
          : [];
        for (const contribution of contributions) {
          if (!contribution.applied) continue;
          const input = Number(contribution.input ?? 0);
          const output = Number(contribution.output ?? input);
          if (Math.abs(input - output) < 1e-6) continue;
          const kind = this._sourceKind(contribution.sourceKind ?? "");
          if (
            ![
              "Actor",
              "Status",
              "Relic",
              "GameMode",
              "Encounter",
              "Modifier",
            ].includes(kind)
          ) {
            continue;
          }
          contextualChange = true;
          const sourceId = String(contribution.sourceId ?? "");
          if (sourceId.length > 0 && !sourceNames.includes(sourceId)) {
            sourceNames.push(sourceId);
          }
        }
      }
    }

    if (!contextualChange) {
      return badges;
    }
    badges.push({
      kind: "modified",
      label: this._i18n.text("CONTEXT CHANGED"),
      symbol: "~",
      sources: sourceNames.map((id) => this._i18n.contentName(id)),
    });
    return badges;
  }

  private _sourceKind(value: unknown): string {
    if (typeof value === "number" && Number.isFinite(value)) {
      const kinds = [
        "Effect",
        "Card",
        "Actor",
        "Target",
        "Status",
        "Relic",
        "Upgrade",
        "GameMode",
        "Encounter",
        "Modifier",
      ];
      const idx = Math.min(Math.max(Math.trunc(value), 0), kinds.length - 1);
      return kinds[idx];
    }
    return String(value ?? "");
  }

  applicationText(application: JsonObject, includeSource = false): string {
    let text = `${this.actorName(String(application.targetEntityId ?? ""))}: `;
    const previous = application.previousValue;
    const current = application.currentValue;
    if (previous != null && current != null) {
      text += `${this._i18n.number(Number(previous))} → ${this._i18n.number(
        Number(current)
      )} ${this._i18n.contentName(String(application.resourceId ?? ""))}`;
      const field = String(application.resourceField ?? 0);
      if (["1", "1.0", "Minimum"].includes(field)) {
        text += ` (${this._i18n.text("Minimum")})`;
      } else if (["2", "2.0", "Maximum"].includes(field)) {
        text += ` (${this._i18n.text("Maximum")})`;
      }
    } else if (application.statusId != null) {
      text += this._i18n.contentName(String(application.statusId));
    } else {
      text += this._i18n.contentName(String(application.effectType ?? "effect"));
    }

    const source = isPlainObject(application.provenance)
      ? application.provenance
      : {};
    const sourceId = String(source.sourceId ?? "");
    if (includeSource && sourceId.length > 0) {
      let kind: unknown = source.kind ?? "";
      if (typeof kind === "number") {
        const kinds = [
          "Card",
          "Status",
          "Relic",
          "Ability",
          "GameMode",
          "Encounter",
          "Rule",
        ];
        kind = kinds[Math.min(Math.max(Math.trunc(kind), 0), 6)];
      }
      const cardDef = this._cards[sourceId]?.definitionId ?? sourceId;
      text += `  [${this._i18n.text(String(kind))}: ${this._i18n.contentName(
        String(cardDef)
      )}]`;
    }
    return text;
  }

  relationship(actor: JsonObject, viewpoint: string): string {
    const policy = isPlainObject(this._combat.relationships)
      ? this._combat.relationships
      : {};
    const fromSide = String(this._actors[viewpoint]?.sideId ?? "");
    const toSide = String(actor.sideId ?? "");
    const rules = Array.isArray(policy.rules)
      ? (policy.rules as JsonObject[])
      : [];
    for (const rule of rules) {
      if (
        String(rule.fromSideId ?? "") === fromSide &&
        String(rule.toSideId ?? "") === toSide
      ) {
        return String(rule.relationship ?? "Neutral");
      }
    }
    return String(
      policy[fromSide === toSide ? "sameSide" : "differentSides"] ?? "Neutral"
    );
  }

  acceptEvaluations(cards: JsonObject[]): void {
    for (const card of cards) {
      const version = isPlainObject(card.version) ? card.version : {};
      if (
        Math.trunc(Number(version.runSequence ?? -1)) ===
        Math.trunc(Number(this._run.sequence ?? 0))
      ) {
        const evaluation = isPlainObject(card.evaluation)
          ? card.evaluation
          : {};
        this._evaluations[String(evaluation.cardInstanceId ?? "")] =
          deepClone(card);
      }
    }
  }

  unavailableReason(id: string): string {
    const evalObj = isPlainObject(this._evaluations[id]?.evaluation)
      ? (this._evaluations[id].evaluation as JsonObject)
      : {};
    const reasons = Array.isArray(evalObj.failureReasons)
      ? evalObj.failureReasons.map(String)
      : [];
    return reasons.length > 0
      ? reasons.join("\n")
      : this._i18n.text("No legal actions for this card in the current state.");
  }

  actionSummary(id: string): string {
    const cands = this.candidates(id);
    if (cands.length === 0) return this._i18n.text("UNAVAILABLE");
    const descriptions: string[] = [];
    for (const candidate of cands) {
      const effects: string[] = [];
      const applications = Array.isArray(candidate.applications)
        ? (candidate.applications as JsonObject[])
        : [];
      for (const application of applications) {
        if (!this._isCost(application, candidate)) {
          effects.push(this.compactApplication(application));
        }
      }
      let summary = effects.slice(0, 3).join("\n");
      if (effects.length > 3) {
        summary += "\n" + this._i18n.text("More effects in inspection");
      }
      if (Boolean(candidate.outcomeUncertain)) {
        summary += "\n" + this._i18n.text("Outcome uncertain");
      }
      if (!summary) {
        summary = this._i18n.text("See inspection for details");
      }
      if (!descriptions.includes(summary)) {
        descriptions.push(summary);
      }
    }
    return descriptions.length === 1
      ? descriptions[0]
      : this._i18n.text(
          "%s legal choices — select a target",
          descriptions.length
        );
  }

  actionPreview(id: string, targetId = ""): string {
    let cands = this.candidates(id);
    if (targetId.length > 0) {
      cands = cands.filter((c) => this.targets(c).includes(targetId));
    }
    if (cands.length === 0) return this.unavailableReason(id);
    if (!targetId && cands.length > 1) {
      return this._i18n.text("Choose a highlighted target.");
    }
    return cands.map((c) => this.preview(c)).join("\n");
  }

  private _isCost(application: JsonObject, candidate: JsonObject): boolean {
    const source = isPlainObject(application.provenance)
      ? application.provenance
      : {};
    const command = isPlainObject(candidate.command) ? candidate.command : {};
    if (String(source.sourceId ?? "") !== String(command.cardInstanceId ?? "")) {
      return false;
    }
    const costs = Array.isArray(candidate.costs)
      ? (candidate.costs as JsonObject[])
      : [];
    return costs.some(
      (cost) =>
        String(cost.componentId ?? "") === String(source.componentId ?? "")
    );
  }

  compactApplication(application: JsonObject): string {
    const previous = application.previousValue;
    const current = application.currentValue;
    if (previous == null || current == null) {
      return this.applicationText(application);
    }
    const delta = Number(current) - Number(previous);
    const field = String(application.resourceField ?? 0);
    let suffix = "";
    if (["1", "1.0", "Minimum"].includes(field)) {
      suffix = " " + this._i18n.text("Minimum");
    } else if (["2", "2.0", "Maximum"].includes(field)) {
      suffix = " " + this._i18n.text("Maximum");
    }
    const sign = delta > 0 ? "+" : "";
    return `${sign}${this._i18n.number(delta)} ${this._i18n.contentName(
      String(application.resourceId ?? "")
    )}${suffix} · ${this.actorName(String(application.targetEntityId ?? ""))}`;
  }

  inspectionText(id: string): string {
    const data = this._evaluations[id] ?? {};
    if (isPlainObject(data.presentation)) {
      return new CardCalculationPresenter(this._i18n)
        .describe(data.presentation, false)
        .join("\n");
    }
    const lines: string[] = [this.actionSummary(id)];
    const cands = this.candidates(id);
    if (cands.length === 0) {
      lines.push(this.unavailableReason(id));
    }
    for (const candidate of cands) {
      lines.push(this.preview(candidate));
    }
    if (Object.keys(data).length === 0) {
      lines.push(this._i18n.text("Detailed inspection is unavailable."));
    } else {
      const appliedUpgrades = Array.isArray(data.appliedUpgrades)
        ? (data.appliedUpgrades as JsonObject[])
        : [];
      lines.push(this._i18n.text("Upgrades: %s", appliedUpgrades.length));
      lines.push(this._i18n.text("Base components"));
      const effectiveBase = isPlainObject(data.effectiveBase)
        ? data.effectiveBase
        : {};
      const components = Array.isArray(effectiveBase.components)
        ? (effectiveBase.components as JsonObject[])
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
      const procs = Array.isArray(data.procs) ? (data.procs as JsonObject[]) : [];
      for (const proc of procs) {
        const procLabel = this._i18n.text(
          proc.isCondensation ? "Condensed proc" : "Proc"
        );
        const impactCount = Array.isArray(proc.impactIds)
          ? proc.impactIds.length
          : 0;
        lines.push(
          `${procLabel} · ${this._i18n.text("Impacts: %s", impactCount)}`
        );
        const consumedStacks = Array.isArray(proc.consumedStacks)
          ? (proc.consumedStacks as JsonObject[])
          : [];
        for (const consumed of consumedStacks) {
          lines.push(
            this._i18n.text("Consumed stacks: %s → %s", [
              consumed.previousStacks ?? 0,
              consumed.currentStacks ?? 0,
            ])
          );
        }
        const continuations = Array.isArray(proc.continuations)
          ? (proc.continuations as JsonObject[])
          : [];
        for (const hop of continuations) {
          lines.push(
            `${this.actorName(
              String(hop.fromEntityId ?? "")
            )} → ${this.actorName(String(hop.toEntityId ?? ""))}`
          );
        }
      }
      const context = isPlainObject(data.contextSources)
        ? data.contextSources
        : {};
      for (const upgrade of appliedUpgrades) {
        lines.push(this._i18n.contentName(String(upgrade.upgradeId ?? "")));
      }
      const pairs: [string, string, string][] = [
        ["Relics", "relics", "definitionId"],
        ["Modifiers", "modifiers", "modifierId"],
      ];
      for (const [heading, fieldKey, idKey] of pairs) {
        const items = Array.isArray(context[fieldKey])
          ? (context[fieldKey] as JsonObject[])
          : [];
        const names = items.map((item) =>
          this._i18n.contentName(
            String(item[idKey] ?? item.definitionId ?? "")
          )
        );
        lines.push(`${this._i18n.text(heading)}: ${names.join(", ")}`);
      }
      const statuses = isPlainObject(context.statuses) ? context.statuses : {};
      for (const actorId of Object.keys(statuses)) {
        const actorStatuses = Array.isArray(statuses[actorId])
          ? (statuses[actorId] as JsonObject[])
          : [];
        for (const status of actorStatuses) {
          lines.push(
            `${this.actorName(actorId)}: ${this._i18n.contentName(
              String(status.statusId ?? "")
            )} ×${status.stacks ?? 1}`
          );
        }
      }
    }
    return lines.join("\n\n");
  }

  inspectionData(id: string): JsonObject {
    return this._evaluations[id] ? deepClone(this._evaluations[id]) : {};
  }

  pileCards(zone: string): JsonObject[] {
    const result: JsonObject[] = [];
    const zones = Array.isArray(this._zoneSnapshot.zones)
      ? (this._zoneSnapshot.zones as JsonObject[])
      : [];
    if (zones.length > 0) {
      for (const view of zones) {
        if (String(view.zoneId ?? "") === zone && Boolean(view.contentsVisible)) {
          const cards = Array.isArray(view.cards)
            ? (view.cards as JsonObject[])
            : [];
          result.push(...deepClone(cards));
        }
      }
    } else {
      const deck = isPlainObject(this._run.deck) ? this._run.deck : {};
      const ids = Array.isArray(deck[zone]) ? deck[zone] : [];
      for (const id of ids) {
        const card = this.cardInstance(String(id));
        if (Object.keys(card).length > 0) {
          result.push(card);
        }
      }
    }
    result.sort((a, b) =>
      String(a.definitionId ?? "").localeCompare(String(b.definitionId ?? ""))
    );
    return result;
  }

  contextKey(): string {
    return JSON.stringify([
      this._run.runId ?? "",
      this._run.branchId ?? "",
      this._run.sequence ?? 0,
      this._combat.combatId ?? "",
      this._combat.step ?? 0,
    ]);
  }

  actionOptions(instanceId: string): ActionOptionModel[] {
    const result: ActionOptionModel[] = [];
    for (let index = 0; index < this._actions.length; index++) {
      const candidate = this._actions[index];
      const command = isPlainObject(candidate.command) ? candidate.command : {};
      if (String(command.cardInstanceId ?? "") === instanceId) {
        result.push(
          new ActionOptionModel({
            reference: `${this.contextKey()}/${index}`,
            candidate,
          })
        );
      }
    }
    return result;
  }

  combatModel(viewpoint: string): CombatModel {
    const intentIcons = isPlainObject(this._appearance.intent_icons)
      ? this._appearance.intent_icons
      : {};
    const activation = isPlainObject(this._combat.activation)
      ? this._combat.activation
      : {};
    const intents = Array.isArray(activation.intents)
      ? (activation.intents as JsonObject[])
      : [];

    const items = this.actors().map((actor) => {
      const relation = this.relationship(actor, viewpoint);
      actor.relationship = relation;
      actor.isInputActor = String(actor.instanceId ?? "") === viewpoint;
      for (const intent of intents) {
        if (String(intent.actorId ?? "") === String(actor.instanceId ?? "")) {
          actor.intentIconId = String(
            intentIcons[String(intent.actionId ?? "")] ?? "unknown"
          );
        }
      }
      return {
        actor,
        relationship: relation,
        sideId: String(actor.sideId ?? ""),
        intent: this.intentFor(String(actor.instanceId ?? "")),
      };
    });

    return new CombatModel({
      context: this.contextKey(),
      actors: items,
    });
  }

  headerModel(labels: Record<string, string>): HeaderModel {
    const activation = isPlainObject(this._combat.activation)
      ? this._combat.activation
      : {};
    const round = Math.trunc(
      Number(activation.round ?? this._combat.currentTurn ?? 1)
    );
    return new HeaderModel({
      title: this._i18n.text("COMBAT"),
      context: `${this._combat.runNodeId ?? ""} · ${
        this._combat.stateHash ?? ""
      }`,
      round: this._i18n.text("ROUND %s", round),
      timeline: this._i18n.text(
        "TIMELINE  [%s]",
        String(labels.open_timeline ?? "open_timeline")
      ),
      tools: this._i18n.text("ZONE TOOLS"),
      allow_tools: CardZonePresenter.toolFlows(this._run).length > 0,
      pause: this._i18n.text("PAUSE"),
    });
  }
}
