import { deepClone, isPlainObject, JsonObject } from "../contracts/types";
import { DialoguePresenter } from "./dialoguePresenter";
import { I18n } from "./i18n";
import { SceneryModel } from "./viewModels";

/**
 * Immutable timeline metadata; the cursor does not belong to the live session.
 */
export class TimelinePresenter {
  private _entries: JsonObject[] = [];
  nextCursor = 0;

  appendPage(page: JsonObject): void {
    const items = Array.isArray(page.items) ? (page.items as JsonObject[]) : [];
    for (const rawItem of items) {
      const item = deepClone(rawItem);
      item.runSequence = item.sequence ?? item.runSequence ?? 0;
      item.stateAvailable =
        page.historicalInspectionAllowed ?? item.stateAvailable ?? false;
      if (
        !this._entries.some(
          (previous) => previous.runSequence === item.runSequence
        )
      ) {
        this._entries.push(deepClone(item));
      }
    }
    this._entries.sort(
      (a, b) => Math.trunc(Number(a.runSequence ?? 0)) - Math.trunc(Number(b.runSequence ?? 0))
    );
    this.nextCursor = Math.trunc(Number(page.nextCursor ?? this.nextCursor));
  }

  entries(): JsonObject[] {
    return deepClone(this._entries);
  }

  static lineage(root: JsonObject, depth = 0): JsonObject[] {
    const rows: JsonObject[] = [
      {
        runId: String(root.runId ?? ""),
        parentRunId: root.parentRunId ?? null,
        depth,
        branchKey: root.branchKey ?? null,
        sourceSequence: root.sourceSequence ?? null,
      },
    ];
    const children = Array.isArray(root.children)
      ? (root.children as JsonObject[])
      : [];
    for (const child of children) {
      rows.push(...TimelinePresenter.lineage(child, depth + 1));
    }
    return rows;
  }
}

/**
 * Formats facts from the replay read models; no rule evaluation.
 */
export class RunReplayPresenter {
  static framesText(entry: JsonObject, translator: I18n): string {
    const frames = Array.isArray(entry.frames)
      ? (entry.frames as JsonObject[])
      : [];
    if (frames.length === 0) {
      return translator.text("No internal frames were recorded for this command.");
    }
    const lines: string[] = [
      translator.text("%s internal frame(s)", frames.length),
    ];
    for (const frame of frames) {
      const context: string[] = [];
      if (frame.actorId != null) context.push(String(frame.actorId));
      if (frame.phaseId != null) context.push(String(frame.phaseId));
      const kind = String(frame.kind ?? "transition")
        .replace(/_/g, " ")
        .replace(/\./g, " › ")
        .replace(/\b\w/g, (c) => c.toUpperCase());
      const ctxSuffix = context.length > 0 ? `  ·  ${context.join(" / ")}` : "";
      lines.push(
        `• ${String(frame.scope ?? "run").toUpperCase()}  ·  ${kind}${ctxSuffix}`
      );
    }
    return lines.join("\n");
  }

  static summaryText(projection: JsonObject, translator: I18n): string {
    const summary = isPlainObject(projection.stateSummary)
      ? projection.stateSummary
      : {};
    const resources: string[] = [];
    const pools = Array.isArray(summary.resources)
      ? (summary.resources as JsonObject[])
      : [];
    for (const pool of pools) {
      resources.push(
        `${translator.contentName(String(pool.resourceId ?? ""))} ${translator.number(
          Number(pool.current ?? 0)
        )}/${translator.number(Number(pool.maximum ?? 0))}`
      );
    }
    return [
      translator.text("State after command: %s", String(summary.lifecycle ?? "—")),
      translator.text("Stop: %s  •  Deck: %s cards  •  Relics: %s", [
        translator.contentName(
          String(summary.currentNodeId ?? ""),
          translator.text("Unknown stop")
        ),
        summary.cardCount ?? 0,
        summary.relicCount ?? 0,
      ]),
      translator.text(
        "Resources: %s",
        resources.length > 0 ? resources.join(", ") : "—"
      ),
      translator.text("State hash: %s", String(projection.stateHash ?? "")),
    ].join("\n");
  }

  static transcriptText(projection: JsonObject, translator: I18n): string {
    const lines: string[] = [];
    const summary = isPlainObject(projection.stateSummary)
      ? projection.stateSummary
      : {};
    const transcript = Array.isArray(summary.transcript)
      ? (summary.transcript as JsonObject[])
      : [];
    for (const line of transcript) {
      const text = DialoguePresenter.localized(line.text ?? {}, translator.locale);
      if (line.choiceId != null) {
        lines.push(`› ${text}`);
      } else {
        lines.push(
          `${DialoguePresenter.localized(
            line.speaker ?? {},
            translator.locale
          )}: ${text}`
        );
      }
    }
    return lines.length === 0
      ? ""
      : `${translator.text("CONVERSATION HISTORY")}\n\n${lines.join("\n\n")}`;
  }
}

/**
 * Classifies recorded outcomes, never calculates amounts or resource rules.
 */
export class SemanticCues {
  static kind(application: JsonObject, frame: JsonObject = {}): string {
    const source = isPlainObject(application.provenance)
      ? application.provenance
      : {};
    const payments = Array.isArray(frame.payments)
      ? (frame.payments as JsonObject[])
      : [];
    for (const payment of payments) {
      if (
        String(payment.sourceId ?? "") === String(source.sourceId ?? "") &&
        String(payment.componentId ?? "") === String(source.componentId ?? "")
      ) {
        return "payment";
      }
    }
    const paymentComponentIds = Array.isArray(frame.paymentComponentIds)
      ? frame.paymentComponentIds.map(String)
      : [];
    if (paymentComponentIds.includes(String(source.componentId ?? ""))) {
      return "payment";
    }
    const field = String(application.resourceField ?? "Current");
    if (!["Current", "0", "0.0"].includes(field) && application.resourceId != null) {
      return "resource_bound";
    }
    const effectType = String(application.effectType ?? "");
    switch (effectType) {
      case "DAMAGE":
        return "impact";
      case "HEAL":
        return "heal";
      case "APPLY_STATUS":
      case "REMOVE_STATUS":
      case "DISPEL_STATUS":
        return "status";
      case "APPLY_MODIFIER":
      case "REMOVE_MODIFIER":
      case "MODIFY_ATTRIBUTE":
        return "modifier";
      case "CONDENSE_STACKS":
        return "condense";
      case "CARD_ZONE_FLOW":
        return "card_move";
      case "MODIFY_RESOURCE":
        if (application.previousValue != null && application.currentValue != null) {
          return Number(application.currentValue) > Number(application.previousValue)
            ? "resource_gain"
            : "resource_loss";
        }
        return "fact";
      default:
        return "fact";
    }
  }
}

/**
 * Published facts to cosmetic cues. Does not calculate effects or assume health.
 */
export class CombatAudioPresenter {
  static cues(
    frame: JsonObject,
    resourceGainCues: JsonObject = {}
  ): string[] {
    const result: string[] = [];
    const applications = Array.isArray(frame.applications)
      ? (frame.applications as JsonObject[])
      : [];
    for (const application of applications) {
      if (SemanticCues.kind(application, frame) === "payment") continue;
      let cue = "";
      const type = String(application.effectType ?? "");
      if (["DAMAGE", "HEAL", "MODIFY_RESOURCE"].includes(type)) {
        const before = application.previousValue;
        const after = application.currentValue;
        const field = String(application.resourceField ?? "Current");
        if (
          before != null &&
          after != null &&
          ["Current", "0", "0.0"].includes(field) &&
          Math.abs(Number(before) - Number(after)) > 1e-6
        ) {
          if (type === "DAMAGE") cue = "impact";
          else if (type === "HEAL") cue = "heal";
          else if (Number(after) > Number(before)) {
            cue = String(
              resourceGainCues[String(application.resourceId ?? "")] ?? ""
            );
          }
        }
      } else if (
        ["APPLY_STATUS", "APPLY_MODIFIER", "CONDENSE_STACKS"].includes(type)
      ) {
        cue = "status";
      } else if (
        type === "CARD_ZONE_FLOW" &&
        Array.isArray(application.cardZoneSteps) &&
        application.cardZoneSteps.length > 0
      ) {
        cue = "card_move";
      }
      if (cue.length > 0 && !result.includes(cue)) {
        result.push(cue);
      }
    }
    return result.slice(0, 3);
  }
}

/**
 * Resolve visual mappings using published IDs, not inferred gameplay rules.
 */
export class SceneryPresenter {
  static build(
    run: JsonObject,
    combat: JsonObject,
    setting: string,
    mappings: JsonObject
  ): SceneryModel {
    const defaults = isPlainObject(mappings.defaults)
      ? deepClone(mappings.defaults)
      : { backgroundId: "combat" };
    const result: JsonObject = defaults;

    const settingsMap = isPlainObject(mappings.settings)
      ? mappings.settings
      : {};
    const specific = isPlainObject(settingsMap[setting])
      ? settingsMap[setting]
      : {};
    if (isPlainObject(specific.defaults)) {
      Object.assign(result, deepClone(specific.defaults));
    }

    const node = String(run.currentNodeId ?? combat.runNodeId ?? "");
    const nodesMap = isPlainObject(mappings.nodes) ? mappings.nodes : {};
    if (isPlainObject(nodesMap[node])) {
      Object.assign(result, deepClone(nodesMap[node]));
    }
    const specNodes = isPlainObject(specific.nodes) ? specific.nodes : {};
    if (isPlainObject(specNodes[node])) {
      Object.assign(result, deepClone(specNodes[node]));
    }

    const encounter = String(combat.encounterDefinitionId ?? "");
    if (encounter.length > 0) {
      const encMap = isPlainObject(mappings.encounters)
        ? mappings.encounters
        : {};
      if (isPlainObject(encMap[encounter])) {
        Object.assign(result, deepClone(encMap[encounter]));
      }
      const specEnc = isPlainObject(specific.encounters)
        ? specific.encounters
        : {};
      if (isPlainObject(specEnc[encounter])) {
        Object.assign(result, deepClone(specEnc[encounter]));
      }
    }

    result.settingId = setting;
    result.nodeId = node;
    return new SceneryModel(result);
  }
}
