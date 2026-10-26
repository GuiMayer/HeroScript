import { JsonObject } from "../contracts/types";
import { I18n } from "./i18n";

/**
 * Presentation only. Missing lifecycle metadata must never imply a gameplay rule.
 */
export class EffectDurationPresenter {
  static describe(value: unknown, translator: I18n, definition: JsonObject = {}): string {
    if (typeof value !== "number" || !Number.isFinite(value)) {
      return translator.text("Duration determined when applied");
    }
    if (value === -1) {
      return translator.text("No time-based expiration");
    }
    if (value < 0) {
      return translator.text("Duration determined when applied");
    }
    if (Math.abs(value) < 1e-6) {
      return translator.text("Expired");
    }
    const boundary = String(definition.durationTickBoundary ?? definition.durationBoundary ?? "");
    const labels: Record<string, string> = {
      StartActivation: "Duration: %s · decreases at the start of the affected actor's activation",
      EndActivation: "Duration: %s · decreases at the end of the affected actor's activation",
      StartRound: "Duration: %s · decreases at the start of each round",
      EndRound: "Duration: %s · decreases at the end of each round",
      Command: "Duration: %s · decreases after each command",
      Activation: "Duration: %s · decreases after each activation",
      Round: "Duration: %s · decreases after each round",
      Combat: "Duration: %s · decreases after each combat",
      Node: "Duration: %s · decreases after each map stop",
      Run: "Duration: %s · decreases after each run",
    };
    const template = labels[boundary] ?? "Duration: %s cycles";
    return translator.text(template, translator.number(value));
  }
}
