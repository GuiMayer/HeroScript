import { useEffect, useState } from "react";
import { DeckCompartmentWorkspace } from "./application/deckCompartmentWorkspace";
import { GameSession } from "./application/gameSession";
import { LoadoutWorkspace } from "./application/loadoutWorkspace";
import { Preferences } from "./application/preferences";
import { JsonObject } from "./contracts/types";
import sceneryData from "./data/scenery.json";
import { DeckCompartmentGateway } from "./engine/deckCompartmentGateway";
import { EngineGateway } from "./engine/engineGateway";
import { HttpTransport } from "./engine/httpTransport";
import { I18n } from "./presentation/i18n";
import { Playback } from "./presentation/playback";
import { PresentationDirector } from "./ui/controllers/controllers";
import { GameAudio } from "./ui/gameAudio";

export type ScreenId =
  | "menu"
  | "loadout"
  | "compartments"
  | "journey"
  | "map"
  | "dialogue"
  | "combat"
  | "timeline"
  | "history"
  | "replay"
  | "sandbox"
  | "codex"
  | "settings"
  | "loadout_lab"
  | "card_hand_lab";

export class AppServices {
  readonly preferences: Preferences;
  readonly i18n: I18n;
  readonly transport: HttpTransport;
  readonly gateway: EngineGateway;
  readonly deckCompartmentGateway: DeckCompartmentGateway;
  readonly session: GameSession;
  readonly playback: Playback;
  readonly director: PresentationDirector;
  readonly audio: GameAudio;
  readonly loadoutWorkspace: LoadoutWorkspace;
  readonly deckCompartmentWorkspace: DeckCompartmentWorkspace;

  readonly mergedPresentation: JsonObject;
  readonly scenery: JsonObject;

  paused = false;

  constructor() {
    this.preferences = new Preferences();
    this.i18n = new I18n(this.preferences.locale);
    this.transport = new HttpTransport(() => this.preferences.apiUrl);
    this.gateway = new EngineGateway(this.transport);
    this.deckCompartmentGateway = new DeckCompartmentGateway(this.transport);
    this.playback = new Playback();
    this.audio = new GameAudio(this.preferences);

    this.session = new GameSession(this.gateway, this.preferences, {
      enqueueEnvelope: (envelope) => this.playback.enqueueEnvelope(envelope),
      clear: () => this.playback.clear(),
    });

    this.director = new PresentationDirector(
      this.playback,
      this.preferences,
      () => this.paused
    );

    this.loadoutWorkspace = new LoadoutWorkspace(this.gateway);
    this.deckCompartmentWorkspace = new DeckCompartmentWorkspace(
      this.deckCompartmentGateway
    );

    this.mergedPresentation = I18n.buildMergedPresentation();
    this.scenery = sceneryData as unknown as JsonObject;

    this.preferences.changed.connect(() => {
      this.i18n.setLocale(this.preferences.locale);
    });

    this.playback.framePresented.connect((frame) => {
      this.audio.frame(frame);
    });
  }

  localizedPresentation(): JsonObject {
    return this.i18n.presentation(this.mergedPresentation);
  }
}

let singletonServices: AppServices | null = null;

export function getAppServices(): AppServices {
  if (!singletonServices) {
    singletonServices = new AppServices();
  }
  return singletonServices;
}

export function useAppServices(): {
  services: AppServices;
  revision: number;
  forceUpdate: () => void;
} {
  const services = getAppServices();
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    const bump = () => setRevision((r) => r + 1);
    const unsubState = services.session.stateChanged.connect(bump);
    const unsubError = services.session.errorReported.connect(bump);
    const unsubNotice = services.session.noticeReported.connect(bump);
    const unsubPlayback = services.playback.changed.connect(bump);
    const unsubDirector = services.director.changed.connect(bump);
    const unsubPrefs = services.preferences.changed.connect(bump);
    const unsubLocale = services.i18n.localeChanged.connect(bump);

    let rafId = 0;
    let lastTime = performance.now();
    const loop = (now: number) => {
      const dt = Math.min(0.1, Math.max(0, (now - lastTime) / 1000));
      lastTime = now;
      services.director.process(dt);
      rafId = requestAnimationFrame(loop);
    };
    rafId = requestAnimationFrame(loop);

    return () => {
      unsubState();
      unsubError();
      unsubNotice();
      unsubPlayback();
      unsubDirector();
      unsubPrefs();
      unsubLocale();
      cancelAnimationFrame(rafId);
    };
  }, [services]);

  return {
    services,
    revision,
    forceUpdate: () => setRevision((r) => r + 1),
  };
}
