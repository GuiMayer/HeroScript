import { Preferences } from "../application/preferences";
import { deepClone, isPlainObject, JsonObject } from "../contracts/types";
import audioManifestData from "../data/audio_manifest.json";
import { CombatAudioPresenter, SemanticCues } from "../presentation/timelineAndReplayPresenters";
import { ArtCatalog, MotionPolicy } from "../presentation/visualAndLayout";

/**
 * Cosmetic audio only: no engine RNG, commands or gameplay authority.
 */
export class GameAudio {
  static readonly VOICE_COUNT = 8;

  private _preferences: Preferences;
  private _events: Record<string, JsonObject> = {};
  private _lastPlayed: Record<string, number> = {};
  private _variant: Record<string, number> = {};
  resourceGainCues: JsonObject = {};
  private _stopped = false;
  private _music: HTMLAudioElement | null = null;
  private _voices: HTMLAudioElement[] = [];
  private _voiceMeta: { eventId: string; combatCue: boolean }[] = [];

  constructor(preferences: Preferences) {
    this._preferences = preferences;
    const data = deepClone(audioManifestData as unknown as JsonObject);
    if (isPlainObject(data.events)) {
      this._events = data.events as Record<string, JsonObject>;
    }
    if (isPlainObject(data.resource_gain_cues)) {
      this.resourceGainCues = data.resource_gain_cues;
    }

    if (typeof Audio !== "undefined") {
      for (let i = 0; i < GameAudio.VOICE_COUNT; i++) {
        this._voices.push(new Audio());
        this._voiceMeta.push({ eventId: "", combatCue: false });
      }
      const musicPath = String(data.music ?? "");
      if (musicPath.length > 0) {
        try {
          this._music = new Audio(ArtCatalog.toWebPath(musicPath));
          this._music.loop = true;
        } catch {
          this._music = null;
        }
      }
    }

    this._preferences.changed.connect(() => this._apply());
    this._apply();
  }

  ui(): void {
    this.playEvent("ui");
  }

  card(): void {
    this.playEvent("card_play", true);
  }

  hit(): void {
    this.playEvent("impact");
  }

  reward(): void {
    this.playEvent("reward");
  }

  selectCard(): void {
    this.playEvent("card_select");
  }

  cancel(): void {
    this.playEvent("cancel");
  }

  turn(): void {
    this.playEvent("turn", true);
  }

  error(): void {
    this.playEvent("error");
  }

  frame(frameData: JsonObject): void {
    for (const event of CombatAudioPresenter.cues(
      frameData,
      this.resourceGainCues
    )) {
      this.playEvent(event, true);
    }
  }

  playBeat(frameData: JsonObject, policy: MotionPolicy): void {
    const emitted: string[] = [];
    const applications = Array.isArray(frameData.applications)
      ? (frameData.applications as JsonObject[])
      : [];
    for (const application of applications) {
      const kind = SemanticCues.kind(application, frameData);
      const cueSpec = policy.cue(kind);
      if (cueSpec.enabled === false) continue;
      if (
        application.previousValue != null &&
        application.currentValue != null &&
        Math.abs(
          Number(application.previousValue) - Number(application.currentValue)
        ) < 1e-6
      ) {
        continue;
      }
      let event = String(cueSpec.sound ?? "");
      if (kind === "resource_gain") {
        event = String(
          this.resourceGainCues[String(application.resourceId ?? "")] ?? event
        );
      }
      if (event.length > 0 && !emitted.includes(event)) {
        this.playEvent(event, true);
        emitted.push(event);
      }
    }
  }

  playEvent(eventId: string, combatCue = false): boolean {
    if (this._stopped || !(eventId in this._events)) return false;
    if (
      this._preferences.masterVolume <= 0 ||
      this._preferences.sfxVolume <= 0 ||
      this._voices.length === 0
    ) {
      return false;
    }

    const event = this._events[eventId];
    const now = Date.now();
    const cooldown = Math.trunc(Number(event.cooldown_ms ?? 60));
    if (now - (this._lastPlayed[eventId] ?? -100000) < cooldown) {
      return false;
    }

    const clips = Array.isArray(event.clips) ? event.clips.map(String) : [];
    if (clips.length === 0) return false;
    const variant = (this._variant[eventId] ?? 0) % clips.length;
    const clipPath = ArtCatalog.toWebPath(clips[variant]);

    let active = 0;
    for (let i = 0; i < this._voices.length; i++) {
      if (!this._voices[i].paused && this._voiceMeta[i].eventId === eventId) {
        active += 1;
      }
    }
    if (active >= Math.trunc(Number(event.max_instances ?? 2))) {
      return false;
    }

    let chosen = -1;
    for (let i = 0; i < this._voices.length; i++) {
      if (this._voices[i].paused || this._voices[i].ended) {
        chosen = i;
        break;
      }
    }
    if (chosen < 0) return false;

    const player = this._voices[chosen];
    const gainDb = Math.min(Math.max(Number(event.gain_db ?? -16), -40), -10);
    const linearGain = Math.pow(10, gainDb / 20);
    player.volume = Math.min(
      1,
      Math.max(
        0,
        linearGain *
          this._preferences.masterVolume *
          this._preferences.sfxVolume *
          2.5
      )
    );
    player.src = clipPath;
    this._voiceMeta[chosen] = { eventId, combatCue };
    player.play().catch(() => {
      // Autoplay policy before first interaction
    });

    this._variant[eventId] = variant + 1;
    this._lastPlayed[eventId] = now;
    return true;
  }

  private _apply(): void {
    if (this._music) {
      this._music.volume = Math.min(
        1,
        Math.max(
          0,
          this._preferences.masterVolume * this._preferences.musicVolume * 0.35
        )
      );
    }
  }

  shutdown(): void {
    this._stopped = true;
    if (this._music) {
      this._music.pause();
      this._music = null;
    }
    for (const voice of this._voices) {
      voice.pause();
    }
    this._voices = [];
    this._lastPlayed = {};
  }
}
