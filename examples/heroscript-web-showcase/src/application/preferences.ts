import { deepClone, JsonObject, Signal } from "../contracts/types";
import { ConfigStore, RunResumeIndex } from "./runResumeIndex";

export const SETTINGS_CATEGORIES: Record<string, [string, string]> = {
  general: ["GENERAL", "Language and basic gameplay presentation."],
  audio: ["AUDIO", "Balance music and interface feedback."],
  display: ["DISPLAY", "Window mode, resolution and presentation."],
  accessibility: ["ACCESSIBILITY", "Readability and motion preferences."],
  controls: ["CONTROLS", "Keyboard and gamepad bindings."],
  connection: ["CONNECTION", "HeroScript service connection."],
};

export interface OptionSpec {
  values?: string[];
  type?: "bool" | "number";
  min?: number;
  max?: number;
  step?: number;
  control?: string;
}

export const SETTINGS_OPTIONS: Record<string, OptionSpec> = {
  locale: { values: ["en", "pt_BR"] },
  animation_speed: { min: 0.25, max: 2.0, step: 0.05, control: "AnimationSpeedSlider" },
  auto_animations: { type: "bool" },
  animation_preset: { values: ["normal", "fast", "instant"] },
  hand_layout_mode: { values: ["adaptive", "reading"] },
  hand_emphasis: { min: 0.0, max: 1.0, step: 0.1, control: "HandEmphasisSlider" },
  master_volume: { min: 0.0, max: 1.0, step: 0.01, control: "MasterVolumeSlider" },
  music_volume: { min: 0.0, max: 1.0, step: 0.01, control: "MusicVolumeSlider" },
  sfx_volume: { min: 0.0, max: 1.0, step: 0.01, control: "SfxVolumeSlider" },
  fullscreen: { type: "bool" },
  text_scale: { min: 0.9, max: 1.2, step: 0.05, control: "TextScaleSlider" },
  high_contrast: { type: "bool" },
  reveal_text_gradually: { type: "bool" },
  reduced_motion: { type: "bool" },
};

export class SettingsSchema {
  static option(key: string): OptionSpec {
    return SETTINGS_OPTIONS[key] ? deepClone(SETTINGS_OPTIONS[key]) : {};
  }

  static forControl(controlId: string): OptionSpec {
    for (const value of Object.values(SETTINGS_OPTIONS)) {
      if (String(value.control ?? "") === controlId) {
        return deepClone(value);
      }
    }
    return {};
  }
}

export interface Resolution {
  x: number;
  y: number;
}

class LocalStorageConfigStore implements ConfigStore {
  private sections: Record<string, Record<string, unknown>> = {};

  constructor(raw?: string | null) {
    if (raw) {
      try {
        const parsed = JSON.parse(raw);
        if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
          this.sections = parsed as Record<string, Record<string, unknown>>;
        }
      } catch {
        this.sections = {};
      }
    }
  }

  hasSection(section: string): boolean {
    return Object.prototype.hasOwnProperty.call(this.sections, section);
  }

  hasSectionKey(section: string, key: string): boolean {
    return Boolean(this.sections[section] && Object.prototype.hasOwnProperty.call(this.sections[section], key));
  }

  getValue(section: string, key: string, fallback: unknown = null): unknown {
    if (!this.hasSectionKey(section, key)) {
      return fallback;
    }
    return this.sections[section][key];
  }

  setValue(section: string, key: string, value: unknown): void {
    if (!this.sections[section]) {
      this.sections[section] = {};
    }
    this.sections[section][key] = value;
  }

  toJSON(): string {
    return JSON.stringify(this.sections);
  }
}

export class Preferences {
  static readonly STORAGE_KEY = "heroscript_showcase.cfg";
  static readonly DEFAULT_API_URL = "http://127.0.0.1:5271";
  static readonly ACTIONS = ["pause_game", "end_turn", "open_timeline", "confirm_action", "inspect_card"] as const;
  static readonly DEFAULT_KEYS: Record<string, string> = {
    pause_game: "Escape",
    end_turn: "E",
    open_timeline: "T",
    confirm_action: "F",
    inspect_card: "I",
  };
  static readonly DEFAULT_BUTTONS: Record<string, number> = {
    pause_game: 6, // Start
    end_turn: 3, // Y
    open_timeline: 4, // Back
    confirm_action: 10, // Right Shoulder
    inspect_card: 2, // X
  };
  static readonly RESERVED_KEYS = ["Enter", "NumpadEnter", "Tab", "Space", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"];
  static readonly RESERVED_BUTTONS = [0, 1, 11, 12, 13, 14];
  static readonly DEFAULT_RESOLUTION: Resolution = { x: 1280, y: 800 };
  static readonly RESOLUTIONS: Resolution[] = [
    { x: 1280, y: 720 },
    { x: 1280, y: 800 },
    { x: 1366, y: 768 },
    { x: 1440, y: 900 },
    { x: 1600, y: 900 },
    { x: 1920, y: 1080 },
    { x: 1920, y: 1200 },
    { x: 2560, y: 1080 },
    { x: 2560, y: 1440 },
    { x: 3440, y: 1440 },
    { x: 3840, y: 2160 },
  ];

  readonly changed = new Signal<void>();

  textScale = 1.0;
  windowResolution: Resolution = { ...Preferences.DEFAULT_RESOLUTION };
  windowMonitor = -1;
  windowMonitorPosition: Resolution | null = null;

  masterVolume = 0.8;
  musicVolume = 0.42;
  sfxVolume = 0.75;
  animationSpeed = 1.0;
  animationPreset = "normal";
  handLayoutMode = "adaptive";
  handEmphasis = 1.0;
  locale = "en";
  reducedMotion = false;
  revealTextGradually = false;
  autoAnimations = true;
  fullscreen = false;
  highContrast = false;
  apiUrl = Preferences.DEFAULT_API_URL;
  lastRunId = "";
  selectedSettingId = "default";
  campaignCounter = 0;
  resumeIndex = new RunResumeIndex();

  private _savedApiUrl = "";
  private _apiOverride = false;
  private _keys: Record<string, string> = { ...Preferences.DEFAULT_KEYS };
  private _buttons: Record<string, number> = { ...Preferences.DEFAULT_BUTTONS };

  constructor() {
    this.loadSettings();
    this._savedApiUrl = this.apiUrl;
    if (typeof window !== "undefined" && window.location) {
      const params = new URLSearchParams(window.location.search);
      const overrideUrl = params.get("api-url") ?? params.get("apiUrl");
      if (overrideUrl) {
        const normalized = Preferences.normalizeApiUrl(overrideUrl);
        if (normalized.length > 0) {
          this.apiUrl = normalized;
          this._apiOverride = true;
        }
      }
    }
  }

  get(key: string): unknown {
    const map: Record<string, unknown> = {
      text_scale: this.textScale,
      master_volume: this.masterVolume,
      music_volume: this.musicVolume,
      sfx_volume: this.sfxVolume,
      animation_speed: this.animationSpeed,
      animation_preset: this.animationPreset,
      hand_layout_mode: this.handLayoutMode,
      hand_emphasis: this.handEmphasis,
      locale: this.locale,
      reduced_motion: this.reducedMotion,
      reveal_text_gradually: this.revealTextGradually,
      auto_animations: this.autoAnimations,
      fullscreen: this.fullscreen,
      high_contrast: this.highContrast,
      api_url: this.apiUrl,
      last_run_id: this.lastRunId,
      selected_setting_id: this.selectedSettingId,
      campaign_counter: this.campaignCounter,
    };
    return key in map ? map[key] : (this as unknown as Record<string, unknown>)[key];
  }

  set(key: string, value: unknown): void {
    switch (key) {
      case "text_scale":
        this.textScale = Number(value);
        break;
      case "master_volume":
        this.masterVolume = Number(value);
        break;
      case "music_volume":
        this.musicVolume = Number(value);
        break;
      case "sfx_volume":
        this.sfxVolume = Number(value);
        break;
      case "animation_speed":
        this.animationSpeed = Number(value);
        break;
      case "animation_preset":
        this.animationPreset = String(value);
        break;
      case "hand_layout_mode":
        this.handLayoutMode = String(value);
        break;
      case "hand_emphasis":
        this.handEmphasis = Number(value);
        break;
      case "locale":
        this.locale = String(value);
        break;
      case "reduced_motion":
        this.reducedMotion = Boolean(value);
        break;
      case "reveal_text_gradually":
        this.revealTextGradually = Boolean(value);
        break;
      case "auto_animations":
        this.autoAnimations = Boolean(value);
        break;
      case "fullscreen":
        this.fullscreen = Boolean(value);
        break;
      case "high_contrast":
        this.highContrast = Boolean(value);
        break;
      case "api_url":
        this.apiUrl = String(value);
        break;
      case "last_run_id":
        this.lastRunId = String(value);
        break;
      case "selected_setting_id":
        this.selectedSettingId = String(value);
        break;
      case "campaign_counter":
        this.campaignCounter = Math.trunc(Number(value));
        break;
      default:
        (this as unknown as Record<string, unknown>)[key] = value;
        break;
    }
  }

  loadSettings(): void {
    let raw: string | null = null;
    try {
      if (typeof localStorage !== "undefined") {
        raw = localStorage.getItem(Preferences.STORAGE_KEY);
      }
    } catch {
      raw = null;
    }
    if (!raw) {
      this._applyDefaultKeys();
      return;
    }
    const config = new LocalStorageConfigStore(raw);
    this.textScale = Number(this._readOption(config, "video", "text_scale", "text_scale", 1.0));
    this.masterVolume = Number(this._readOption(config, "audio", "master", "master_volume", 0.8));
    this.musicVolume = Number(this._readOption(config, "audio", "music", "music_volume", 0.42));
    this.sfxVolume = Number(this._readOption(config, "audio", "sfx", "sfx_volume", 0.75));
    this.animationSpeed = Number(this._readOption(config, "game", "animation_speed", "animation_speed", 1.0));
    this.animationPreset = String(this._readOption(config, "game", "animation_preset", "animation_preset", "normal"));
    this.handLayoutMode = String(this._readOption(config, "game", "hand_layout", "hand_layout_mode", "adaptive"));
    this.handEmphasis = Number(this._readOption(config, "game", "hand_emphasis", "hand_emphasis", 1.0));
    this.locale = String(this._readOption(config, "game", "locale", "locale", "en"));
    this.reducedMotion = Boolean(this._readOption(config, "game", "reduced_motion", "reduced_motion", false));
    this.revealTextGradually = Boolean(
      this._readOption(config, "accessibility", "reveal_text_gradually", "reveal_text_gradually", false)
    );
    this.autoAnimations = Boolean(this._readOption(config, "game", "auto_animations", "auto_animations", true));
    this.fullscreen = Boolean(this._readOption(config, "video", "fullscreen", "fullscreen", false));

    const width = this._readDimension(config, "width", Preferences.DEFAULT_RESOLUTION.x);
    const height = this._readDimension(config, "height", Preferences.DEFAULT_RESOLUTION.y);
    this.windowResolution = Preferences.validResolution({ x: width, y: height })
      ? { x: width, y: height }
      : { ...Preferences.DEFAULT_RESOLUTION };
    this.highContrast = Boolean(this._readOption(config, "video", "high_contrast", "high_contrast", false));

    const savedUrl = config.getValue("network", "api_url", Preferences.DEFAULT_API_URL);
    this.apiUrl = typeof savedUrl === "string" ? Preferences.normalizeApiUrl(savedUrl) : "";
    if (!this.apiUrl) {
      this.apiUrl = Preferences.DEFAULT_API_URL;
    }
    this.lastRunId = String(config.getValue("session", "last_run_id", this.lastRunId));
    this.selectedSettingId = String(config.getValue("session", "selected_setting_id", this.selectedSettingId));
    this.campaignCounter = Math.trunc(Number(config.getValue("session", "campaign_counter", this.campaignCounter)));
    this.resumeIndex.loadConfig(config);

    const keys = this._loadBindings(config, "input", Preferences.DEFAULT_KEYS, Preferences.validKey);
    const buttons = this._loadBindings(config, "gamepad", Preferences.DEFAULT_BUTTONS, Preferences.validButton);
    for (const action of Preferences.ACTIONS) {
      this._keys[action] = String(keys[action]);
      this._buttons[action] = Number(buttons[action]);
    }
  }

  private _readOption(
    config: LocalStorageConfigStore,
    section: string,
    key: string,
    option: string,
    fallback: unknown
  ): unknown {
    const value = config.getValue(section, key, fallback);
    const spec = SettingsSchema.option(option);
    if (spec.values) {
      return typeof value === "string" && spec.values.includes(value) ? value : fallback;
    }
    if (spec.type === "bool") {
      return typeof value === "boolean" ? value : fallback;
    }
    if (typeof value !== "number" || !Number.isFinite(value)) {
      return fallback;
    }
    return Math.min(Math.max(value, Number(spec.min ?? 0)), Number(spec.max ?? 1));
  }

  private _readDimension(config: LocalStorageConfigStore, key: string, fallback: number): number {
    const value = config.getValue("video", key, fallback);
    return typeof value === "number" && Number.isInteger(value) && value >= 0 && value <= 7680 ? value : fallback;
  }

  private _loadBindings<T>(
    config: LocalStorageConfigStore,
    section: string,
    defaults: Record<string, T>,
    valid: (value: unknown) => boolean
  ): Record<string, T> {
    const result: Record<string, T> = {};
    const used: T[] = [];
    for (const action of Preferences.ACTIONS) {
      const value = config.getValue(section, action, defaults[action]);
      if (valid(value) && !used.includes(value as T)) {
        result[action] = value as T;
        used.push(value as T);
      }
    }
    for (const action of Preferences.ACTIONS) {
      if (action in result) continue;
      const candidates = [defaults[action], ...Object.values(defaults)];
      for (const candidate of candidates) {
        if (!used.includes(candidate)) {
          result[action] = candidate;
          used.push(candidate);
          break;
        }
      }
    }
    return result;
  }

  static validKey(value: unknown): boolean {
    if (typeof value !== "string" || value.trim().length === 0) return false;
    if (Preferences.RESERVED_KEYS.includes(value)) return false;
    return /^[a-zA-Z0-9]$|^Escape$|^F([1-9]|1[0-2])$|^Backspace$|^Delete$|^Home$|^End$|^PageUp$|^PageDown$/.test(value);
  }

  static validButton(value: unknown): boolean {
    return (
      typeof value === "number" &&
      Number.isInteger(value) &&
      value >= 0 &&
      value < 20 &&
      !Preferences.RESERVED_BUTTONS.includes(value)
    );
  }

  save(notify = true): void {
    const config = new LocalStorageConfigStore();
    config.setValue("video", "text_scale", Math.min(Math.max(this.textScale, 0.9), 1.2));
    config.setValue("audio", "master", this.masterVolume);
    config.setValue("audio", "music", this.musicVolume);
    config.setValue("audio", "sfx", this.sfxVolume);
    config.setValue("game", "animation_speed", this.animationSpeed);
    config.setValue("game", "animation_preset", this.animationPreset);
    config.setValue("game", "hand_layout", this.handLayoutMode);
    config.setValue("game", "hand_emphasis", this.handEmphasis);
    config.setValue("game", "locale", this.locale);
    config.setValue("game", "reduced_motion", this.reducedMotion);
    config.setValue("accessibility", "reveal_text_gradually", this.revealTextGradually);
    config.setValue("game", "auto_animations", this.autoAnimations);
    config.setValue("video", "fullscreen", this.fullscreen);
    config.setValue("video", "monitor", this.windowMonitor);
    if (this.windowMonitorPosition) {
      config.setValue("video", "monitor_position", this.windowMonitorPosition);
    }
    config.setValue("video", "width", this.windowResolution.x);
    config.setValue("video", "height", this.windowResolution.y);
    config.setValue("video", "high_contrast", this.highContrast);
    config.setValue("network", "api_url", this._apiOverride ? this._savedApiUrl : this.apiUrl);
    config.setValue("session", "last_run_id", this.lastRunId);
    config.setValue("session", "selected_setting_id", this.selectedSettingId);
    config.setValue("session", "campaign_counter", this.campaignCounter);
    this.resumeIndex.writeConfig(config);
    for (const action of Preferences.ACTIONS) {
      config.setValue("input", action, this.actionKey(action));
      config.setValue("gamepad", action, this.actionButton(action));
    }
    try {
      if (typeof localStorage !== "undefined") {
        localStorage.setItem(Preferences.STORAGE_KEY, config.toJSON());
      }
    } catch {
      // Ignore storage quota / private mode issues
    }
    if (notify) {
      this.changed.emit();
    }
  }

  saveSession(): void {
    this.save(false);
  }

  static validResolution(value: Resolution): boolean {
    return value.x >= 960 && value.y >= 540 && value.x <= 7680 && value.y <= 4320;
  }

  static fitWindow(requested: Resolution, available: Resolution): Resolution {
    const ratio = Math.min(1.0, Math.min(available.x / requested.x, available.y / requested.y));
    const factor = Math.max(ratio, 0.1);
    return {
      x: Math.round(requested.x * factor),
      y: Math.round(requested.y * factor),
    };
  }

  static resolveMonitor(
    savedIndex: number,
    savedPosition: Resolution | null,
    positions: Resolution[],
    fallback: number
  ): number {
    if (positions.length === 0) return 0;
    if (savedPosition) {
      const matched = positions.findIndex((p) => p.x === savedPosition.x && p.y === savedPosition.y);
      if (matched >= 0) return matched;
    }
    if (savedIndex >= 0 && savedIndex < positions.length) return savedIndex;
    return fallback >= 0 && fallback < positions.length ? fallback : 0;
  }

  resolutionOptions(): Resolution[] {
    const options = Preferences.RESOLUTIONS.map((r) => ({ ...r }));
    if (
      Preferences.validResolution(this.windowResolution) &&
      !options.some((r) => r.x === this.windowResolution.x && r.y === this.windowResolution.y)
    ) {
      options.push({ ...this.windowResolution });
    }
    options.sort((a, b) => (a.x !== b.x ? a.x - b.x : a.y - b.y));
    return options;
  }

  setResolution(value: Resolution, persist = true): boolean {
    if (!Preferences.validResolution(value)) return false;
    this.windowResolution = { ...value };
    if (persist) {
      this.save();
    }
    return true;
  }

  remapKey(action: string, key: string, modifiers = false, persist = true): string {
    if (!Preferences.ACTIONS.includes(action as (typeof Preferences.ACTIONS)[number])) {
      return "Unknown action.";
    }
    const normalized = key.length === 1 ? key.toUpperCase() : key;
    if (Preferences.RESERVED_KEYS.includes(normalized) || modifiers) {
      return "This input is reserved for interface navigation.";
    }
    if (!Preferences.validKey(normalized)) {
      return "Use a keyboard key or a controller button.";
    }
    for (const other of Preferences.ACTIONS) {
      if (other !== action && this.actionKey(other) === normalized) {
        return "This input is already assigned.";
      }
    }
    this._keys[action] = normalized;
    if (persist) this.save();
    return "";
  }

  remapButton(action: string, buttonIndex: number, persist = true): string {
    if (!Preferences.ACTIONS.includes(action as (typeof Preferences.ACTIONS)[number])) {
      return "Unknown action.";
    }
    if (Preferences.RESERVED_BUTTONS.includes(buttonIndex)) {
      return "This input is reserved for interface navigation.";
    }
    if (!Preferences.validButton(buttonIndex)) {
      return "Use a keyboard key or a controller button.";
    }
    for (const other of Preferences.ACTIONS) {
      if (other !== action && this.actionButton(other) === buttonIndex) {
        return "This input is already assigned.";
      }
    }
    this._buttons[action] = buttonIndex;
    if (persist) this.save();
    return "";
  }

  resetBindings(persist = true): void {
    this._applyDefaultKeys();
    if (persist) this.save();
  }

  setApiUrl(value: string): boolean {
    const normalized = Preferences.normalizeApiUrl(value);
    if (!normalized) return false;
    this.apiUrl = normalized;
    this._savedApiUrl = normalized;
    this._apiOverride = false;
    this.save();
    return true;
  }

  static normalizeApiUrl(value: string): string {
    const trimmed = value.trim();
    for (let i = 0; i < trimmed.length; i++) {
      const code = trimmed.charCodeAt(i);
      if (code <= 32 || code === 127) return "";
    }
    if (trimmed.includes("\\")) return "";
    const match = /^(https?):\/\/(\[[0-9a-f:.]+\]|[a-z0-9.-]+)(?::([0-9]{1,5}))?(\/[^?#]*)?$/i.exec(trimmed);
    if (!match) return "";
    const scheme = match[1].toLowerCase();
    const host = match[2];
    const port = match[3] ?? "";
    let path = match[4] ?? "";
    if (host.startsWith("[")) {
      const inner = host.slice(1, -1);
      if (!inner.includes(":") || !/^[0-9a-f:.]+$/i.test(inner)) return "";
    } else {
      if (host.length > 253) return "";
      const parts = host.replace(/\.$/, "").split(".");
      if (parts.length > 1 && parts.every((p) => /^\d+$/.test(p))) {
        if (parts.length !== 4 || parts.some((p) => Number(p) < 0 || Number(p) > 255)) {
          return "";
        }
      }
      const labelRegex = /^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$/;
      for (const part of parts) {
        if (!labelRegex.test(part)) return "";
      }
    }
    if (port.length > 0) {
      const portNum = Number(port);
      if (!Number.isInteger(portNum) || portNum < 1 || portNum > 65535) return "";
    }
    while (path.endsWith("/")) {
      path = path.slice(0, -1);
    }
    return `${scheme}://${host.toLowerCase()}${port ? ":" + port : ""}${path}`;
  }

  nextCampaignSeed(selectedSetting: JsonObject, selectedSettingId: string): number {
    const launch = (selectedSetting.launch as JsonObject) ?? {};
    const counter = this.resumeIndex.nextCounter(
      this.apiUrl,
      String(launch.playerEntityId ?? "player"),
      selectedSettingId,
      String(launch.modeId ?? ""),
      this.campaignCounter
    );
    this.saveSession();
    return 20260911 + counter;
  }

  actionKey(action: string): string {
    return this._keys[action] ?? Preferences.DEFAULT_KEYS[action] ?? "";
  }

  actionLabel(action: string): string {
    return this.actionKey(action);
  }

  actionButton(action: string): number {
    return this._buttons[action] ?? Preferences.DEFAULT_BUTTONS[action] ?? -1;
  }

  controllerLabel(action: string): string {
    return `Pad ${this.actionButton(action)}`;
  }

  private _applyDefaultKeys(): void {
    this._keys = { ...Preferences.DEFAULT_KEYS };
    this._buttons = { ...Preferences.DEFAULT_BUTTONS };
  }
}
