import { vi } from "vitest";
import { AppServices } from "../../src/bootstrap";
import { JsonObject } from "../../src/contracts/types";

/** A fully-formed card view model, shaped like CombatPresenter.cardViewModel output. */
export function cardModel(overrides: JsonObject = {}): JsonObject {
  return {
    cardInstanceId: "card_strike_1",
    definitionId: "strike",
    name: "Ember Strike",
    tone: "attack",
    cardType: "ATTACK",
    rarity: "Common",
    costs: [{ amount: 1, symbol: "⚡", name: "Energy" }],
    costsKnown: true,
    effectRows: [
      {
        segments: [
          { text: "Deal " },
          { text: "6", value: 6, baseValue: 6 },
          { text: " damage." },
        ],
      },
    ],
    summary: "Deal 6 damage.",
    behaviors: [],
    identityTags: [],
    requirements: [],
    changeBadges: [],
    availability: "SELECT TO PLAY",
    ...overrides,
  };
}

export function actorItem(overrides: JsonObject = {}): JsonObject {
  return {
    relationship: "Hostile",
    intent: "Attack for 8",
    actor: {
      instanceId: "enemy_1",
      definitionId: "ash_wisp",
      name: "Ash Wisp",
      resources: [{ resourceId: "health", current: 12, minimum: 0, maximum: 20 }],
      statuses: [{ statusId: "burn", stacks: 2 }],
      modifiers: [],
      ...((overrides.actor as JsonObject) ?? {}),
    },
    ...Object.fromEntries(Object.entries(overrides).filter(([k]) => k !== "actor")),
  };
}

/** Network stub that makes every engine request fail fast (engine offline). */
export function stubOfflineEngine(): void {
  vi.stubGlobal(
    "fetch",
    vi.fn(() => Promise.reject(new TypeError("network offline (test stub)")))
  );
}

/**
 * Builds the real service graph with an offline engine and the English locale,
 * so screen tests exercise production wiring instead of hand-written mocks.
 */
export function createTestServices(): AppServices {
  stubOfflineEngine();
  const services = new AppServices();
  services.i18n.setLocale("en");
  return services;
}
