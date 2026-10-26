import { deepClone, isPlainObject, JsonObject } from "../contracts/types";
import motionManifestData from "../data/motion_manifest.json";
import artManifestData from "../data/art_manifest.json";
import ivoryRunArtManifestData from "../data/ivory_run_art_manifest.json";
import programmerArtManifestData from "../data/programmer_art_manifest.json";
import { Point2D, Rect2D } from "./mapLayout";

export class CardAnimationProfile {
  static readonly STORAGE_KEY = "heroscript_card_animation_profile.json";
  static readonly KEYS: Record<string, string[]> = {
    burn: ["exile_burn"],
    use: ["card_flight", "staged_discard"],
    retained: ["card_flight", "retain"],
    draw: ["draw"],
    discard: ["discard"],
    create: ["create"],
    destroy: ["destroy"],
  };

  static read(): JsonObject {
    try {
      if (typeof localStorage !== "undefined") {
        const raw = localStorage.getItem(CardAnimationProfile.STORAGE_KEY);
        if (raw) {
          const parsed = JSON.parse(raw);
          if (isPlainObject(parsed)) return parsed;
        }
      }
    } catch {
      // ignore
    }
    return {};
  }

  static apply(policy: MotionPolicy): void {
    const profile = CardAnimationProfile.read();
    for (const operation of Object.keys(CardAnimationProfile.KEYS)) {
      const entry =
        operation === "use" || operation === "retained"
          ? profile.use ?? profile.retained
          : profile[operation];
      if (!isPlainObject(entry)) continue;
      const seconds = Number(entry.seconds);
      if (!Number.isFinite(seconds) || seconds < 0 || seconds > 30) continue;
      const keys = CardAnimationProfile.KEYS[operation];
      for (const key of keys) {
        policy.setValue(key, seconds / keys.length);
      }
      if (
        operation === "burn" &&
        ["center", "edges"].includes(String(entry.direction ?? "center"))
      ) {
        const flows = isPlainObject(policy.getCue("card_flows"))
          ? deepClone(policy.getCue("card_flows"))
          : {};
        for (const flow of Object.keys(flows)) {
          const flowObj = flows[flow];
          if (isPlainObject(flowObj) && flowObj.animation === "burn") {
            flowObj.direction = String(entry.direction ?? "center");
          }
        }
        policy.setCue("card_flows", flows);
      }
    }
  }

  static save(operation: string, seconds: number, direction: string): boolean {
    if (
      !(operation in CardAnimationProfile.KEYS) ||
      !Number.isFinite(seconds) ||
      seconds < 0 ||
      seconds > 30
    ) {
      return false;
    }
    const profile = CardAnimationProfile.read();
    profile[operation] = { seconds, direction };
    if (operation === "use" || operation === "retained") {
      profile.use = { seconds };
      profile.retained = { seconds };
    }
    try {
      if (typeof localStorage !== "undefined") {
        localStorage.setItem(
          CardAnimationProfile.STORAGE_KEY,
          JSON.stringify(profile, null, 2)
        );
      }
      return true;
    } catch {
      return false;
    }
  }
}

export class MotionPolicy {
  static readonly DEFAULTS: Record<string, number> = {
    prepare: 0.075,
    retain: 0.5,
    draw: 1.0,
    discard: 1.0,
    staged_discard: 0.5,
    create: 1.0,
    destroy: 1.0,
    exile_burn: 0.5,
    stagger: 0.0,
    batch_span: 0.0,
    card_flight: 0.5,
    card_entry: 1.0,
    impact: 1.0,
    float: 1.0,
    zone_transfer: 1.0,
    hand_transition: 0.11,
    hover: 0.085,
    watchdog: 12.0,
    max_floats: 24,
    max_transfers: 12,
  };

  static readonly DEFAULT_EASINGS: Record<string, { curve: string; ease: string }> = {
    card_flight: { curve: "cubic", ease: "out" },
    retain: { curve: "cubic", ease: "in_out" },
    zone_transfer: { curve: "cubic", ease: "in_out" },
    hover: { curve: "cubic", ease: "out" },
    card_entry: { curve: "cubic", ease: "out" },
  };

  private _easings: Record<string, { curve: string; ease: string }> = {};
  private _values: Record<string, number> = {};
  private _cues: Record<string, JsonObject> = {};
  private _random: JsonObject = {};
  speed = 1.0;
  instant = false;
  reduced = false;

  setValue(key: string, value: number): void {
    this._values[key] = value;
  }

  getCue(kind: string): JsonObject {
    return this.cue(kind);
  }

  setCue(kind: string, value: JsonObject): void {
    this._cues[kind] = value;
  }

  static capture(
    setting: string,
    options: JsonObject,
    customDocument?: JsonObject
  ): MotionPolicy {
    const policy = new MotionPolicy();
    const document: JsonObject = customDocument
      ? deepClone(customDocument)
      : deepClone(motionManifestData as unknown as JsonObject);
    policy._values = { ...MotionPolicy.DEFAULTS };

    const settings = isPlainObject(document.settings) ? document.settings : {};
    const override = isPlainObject(settings[setting]) ? settings[setting] : {};

    for (const layer of [document.durations, override.durations]) {
      if (!isPlainObject(layer)) continue;
      for (const key of Object.keys(layer)) {
        const val = Number(layer[key]);
        if (
          key in MotionPolicy.DEFAULTS &&
          Number.isFinite(val) &&
          val >= 0 &&
          val <= 64
        ) {
          policy._values[key] = val;
        }
      }
    }

    const baseCues = isPlainObject(document.cues)
      ? deepClone(document.cues as Record<string, JsonObject>)
      : {};
    const overrideCues = isPlainObject(override.cues)
      ? deepClone(override.cues as Record<string, JsonObject>)
      : {};
    policy._cues = { ...baseCues, ...overrideCues };

    policy._random = isPlainObject(override.random_inputs)
      ? deepClone(override.random_inputs)
      : {};
    policy._easings = deepClone(MotionPolicy.DEFAULT_EASINGS);

    for (const layer of [document.easings, override.easings]) {
      if (!isPlainObject(layer)) continue;
      for (const key of Object.keys(layer)) {
        const curveObj = layer[key];
        if (isPlainObject(curveObj)) {
          const curve = String(curveObj.curve ?? "");
          const ease = String(curveObj.ease ?? "");
          if (
            ["linear", "cubic", "sine", "quad"].includes(curve) &&
            ["in", "out", "in_out"].includes(ease)
          ) {
            policy._easings[key] = { curve, ease };
          }
        }
      }
    }

    policy.instant = String(options.animation_preset ?? "normal") === "instant";
    policy.speed =
      options.animation_preset === "fast"
        ? 2.0
        : Math.min(Math.max(Number(options.animation_speed ?? 1), 0.25), 2.0);
    policy.reduced = Boolean(options.reduced_motion);

    CardAnimationProfile.apply(policy);

    let longest = 0.0;
    for (const key of [
      "card_flight",
      "retain",
      "staged_discard",
      "draw",
      "discard",
      "create",
      "destroy",
      "exile_burn",
      "zone_transfer",
    ]) {
      longest = Math.max(longest, Number(policy._values[key] ?? 0));
    }
    policy._values.watchdog = Math.max(
      Number(policy._values.watchdog ?? 12),
      longest + 2 * policy.speed
    );
    return policy;
  }

  duration(key: string, movement = false): number {
    if (this.instant || (movement && this.reduced)) return 0.0;
    const base = this._values[key] ?? MotionPolicy.DEFAULTS[key] ?? 0.12;
    return base / this.speed;
  }

  limit(key: string): number {
    return Math.max(
      1,
      Math.trunc(Number(this._values[key] ?? MotionPolicy.DEFAULTS[key] ?? 16))
    );
  }

  cue(kind: string): JsonObject {
    return this._cues[kind] ? deepClone(this._cues[kind]) : {};
  }

  randomLabels(): JsonObject {
    return deepClone(this._random);
  }

  cssEasing(key: string): string {
    const spec = this._easings[key] ?? { curve: "cubic", ease: "out" };
    if (spec.curve === "linear") return "linear";
    if (spec.ease === "in") return "cubic-bezier(0.32, 0, 0.67, 0)";
    if (spec.ease === "in_out") return "cubic-bezier(0.65, 0, 0.35, 1)";
    return "cubic-bezier(0.22, 1, 0.36, 1)";
  }
}

export class ArtCatalog {
  static readonly MANIFEST = "res://data/art_manifest.json";
  static readonly IVORY_MANIFEST = "res://data/ivory_run_art_manifest.json";
  static readonly PROGRAMMER_MANIFEST = "res://data/programmer_art_manifest.json";

  private static _variantChoices: Record<string, string> = {};

  static toWebPath(resPath: string): string {
    if (!resPath) return "";
    if (resPath.startsWith("res://")) {
      return "/" + resPath.slice("res://".length);
    }
    return resPath.startsWith("/") ? resPath : "/" + resPath;
  }

  static manifest(path = ArtCatalog.MANIFEST): JsonObject {
    if (path.includes("programmer_art")) {
      return deepClone(programmerArtManifestData as unknown as JsonObject);
    }
    if (path.includes("ivory_run")) {
      return deepClone(ivoryRunArtManifestData as unknown as JsonObject);
    }
    return deepClone(artManifestData as unknown as JsonObject);
  }

  static resolve(
    category: string,
    contentId: string,
    manifestPath = ArtCatalog.IVORY_MANIFEST
  ): JsonObject {
    const data = ArtCatalog.manifest(manifestPath);
    const defaults = isPlainObject(data.defaults) ? data.defaults : {};
    const fallback = String(defaults[category] ?? "card_unknown");
    const categories = isPlainObject(data.categories) ? data.categories : {};
    const catMap = isPlainObject(categories[category]) ? categories[category] : {};
    let key = String(catMap[contentId] ?? fallback);
    const slots = isPlainObject(data.slots) ? data.slots : {};
    let slot = isPlainObject(slots[key]) ? deepClone(slots[key]) : {};

    if (Object.keys(slot).length === 0 && isPlainObject(slots[fallback])) {
      key = fallback;
      slot = deepClone(slots[fallback]);
    }

    const variants = Array.isArray(slot.variants)
      ? slot.variants.filter((v: unknown): v is string => typeof v === "string" && v.length > 0)
      : [];
    if (variants.length > 0) {
      const choiceKey = `${manifestPath}::${key}`;
      if (!(choiceKey in ArtCatalog._variantChoices)) {
        const idx = Math.floor(Math.random() * variants.length);
        ArtCatalog._variantChoices[choiceKey] = variants[idx];
      }
      slot.path = ArtCatalog._variantChoices[choiceKey];
    }

    slot.key = key;
    if (typeof slot.path === "string" && slot.path.length > 0) {
      slot.webPath = ArtCatalog.toWebPath(slot.path);
    } else {
      slot.webPath = "";
    }
    return slot;
  }
}

export class HandVisualStyle {
  gap = 12.0;
  minimumStep = 92.0;
  maximumAngleDegrees = 8.0;
  minimumFanAngleDegrees = 4.0;
  preferredStepRatio = 0.86;
  arcHeight = 14.0;
  lift = 32.0;
  transitionSeconds = 0.13;
  pointerHysteresis = 4.0;
  dragThreshold = 16.0;
  padding = 6.0;

  presentationKey(): string {
    return JSON.stringify(this);
  }
}

export interface HandLayoutResult {
  positions: Point2D[];
  rotations: number[]; // in radians
  rotationDegrees: number[];
  hitRects: Rect2D[];
  extent: number;
  step: number;
  compression: number;
  overflow: boolean;
  height: number;
}

/**
 * Pure presentation geometry, not an engine calculation.
 */
export class HandLayout {
  static calculate(
    count: number,
    available: number,
    card: Point2D,
    style: HandVisualStyle = new HandVisualStyle(),
    mode = "adaptive"
  ): HandLayoutResult {
    const degToRad = (deg: number) => (deg * Math.PI) / 180;
    const angle =
      mode === "adaptive" && count > 1
        ? degToRad(
            Math.max(
              style.minimumFanAngleDegrees,
              style.maximumAngleDegrees * Math.min(1.0, (count - 1) / 3.0)
            )
          )
        : 0.0;
    const reservedAngle =
      mode === "adaptive" ? degToRad(style.maximumAngleDegrees) : 0.0;
    const inset = style.padding + card.y * Math.abs(Math.sin(reservedAngle));
    const usable = Math.max(0.0, available - 2.0 * inset);
    const preferred =
      mode === "adaptive"
        ? card.x * style.preferredStepRatio
        : card.x + style.gap;
    let step = preferred;
    if (count > 1 && mode === "adaptive") {
      const rawStep = (usable - card.x) / (count - 1);
      step = Math.min(
        Math.max(rawStep, Math.min(card.x, style.minimumStep)),
        preferred
      );
    }
    const span = card.x + Math.max(0, count - 1) * step;
    const extent = Math.max(available, span + 2.0 * inset);
    const compression = Math.min(Math.max(1.0 - step / preferred, 0.0), 1.0);

    const positions: Point2D[] = [];
    const rotations: number[] = [];
    const rotationDegrees: number[] = [];
    const hitRects: Rect2D[] = [];

    const x0 = (extent - span) * 0.5;
    const top = style.padding + style.lift;

    for (let i = 0; i < count; i++) {
      const t = count > 1 ? (2.0 * i) / (count - 1) - 1.0 : 0.0;
      const px = x0 + i * step;
      const py =
        mode === "adaptive" && count > 1
          ? top + style.arcHeight * t * t
          : top;
      positions.push({ x: px, y: py });
      const rad = t * angle;
      rotations.push(rad);
      rotationDegrees.push((rad * 180) / Math.PI);

      const left =
        i === 0 ? x0 + i * step : x0 + i * step + (card.x - step) * 0.5;
      const right =
        i === count - 1
          ? x0 + i * step + card.x
          : x0 + (i + 1) * step + (card.x - step) * 0.5;
      const width = right - left;
      const height = card.y + style.lift + style.arcHeight;
      hitRects.push({
        x: left,
        y: style.padding,
        width,
        height,
        endX: left + width,
        endY: style.padding + height,
        center: {
          x: left + width * 0.5,
          y: style.padding + height * 0.5,
        },
      });
    }

    return {
      positions,
      rotations,
      rotationDegrees,
      hitRects,
      extent,
      step,
      compression,
      overflow: extent > available + 1,
      height:
        card.y +
        top +
        style.arcHeight +
        card.x * Math.abs(Math.sin(reservedAngle)) * 0.5 +
        style.padding,
    };
  }
}
