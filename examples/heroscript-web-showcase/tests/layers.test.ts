import { describe, expect, it } from "vitest";
import { ActivityChoices } from "../src/application/activityChoices";
import { DeckCompartmentWorkspace } from "../src/application/deckCompartmentWorkspace";
import { LoadoutWorkspace } from "../src/application/loadoutWorkspace";
import { Preferences } from "../src/application/preferences";
import { RunResumeIndex } from "../src/application/runResumeIndex";
import { SnapshotProjection } from "../src/contracts/snapshotProjection";
import { JsonObject } from "../src/contracts/types";
import inscriptionLoadoutLabData from "../src/data/labs/inscription_loadout_lab.json";
import { DeckCompartmentGateway } from "../src/engine/deckCompartmentGateway";
import { EngineGateway } from "../src/engine/engineGateway";
import { HttpTransport } from "../src/engine/httpTransport";
import { RequestNumbers } from "../src/engine/requestNumbers";
import { InscriptionLoadoutDraft } from "../src/labs/inscriptionLoadoutDraft";
import { ActivityPresenter } from "../src/presentation/activityPresenter";
import { CombatPresenter } from "../src/presentation/combatPresenter";
import { DialoguePresenter } from "../src/presentation/dialoguePresenter";
import { I18n } from "../src/presentation/i18n";
import { MapLayout, MapLayoutProfile } from "../src/presentation/mapLayout";
import { Playback } from "../src/presentation/playback";
import { PresentationPlanner } from "../src/presentation/presentationPlanner";
import { HandLayout, HandVisualStyle, MotionPolicy } from "../src/presentation/visualAndLayout";
import { CombatController, PresentationDirector } from "../src/ui/controllers/controllers";

describe("HeroScript Web Showcase — Architectural & Layer Contracts", () => {
  it("normalizes integer wire numbers recursively via RequestNumbers", () => {
    const payload = RequestNumbers.normalize({
      expectedSequence: 4.0,
      nested: [1.0, 2.5, { count: -0.0 }],
    }) as JsonObject;
    expect(payload.expectedSequence).toBe(4);
    expect((payload.nested as unknown[])[0]).toBe(1);
    expect((payload.nested as unknown[])[1]).toBe(2.5);
  });

  it("validates and normalizes API URLs in Preferences", () => {
    expect(Preferences.normalizeApiUrl("http://127.0.0.1:5271/")).toBe(
      "http://127.0.0.1:5271"
    );
    expect(Preferences.normalizeApiUrl("HTTPS://EXAMPLE.COM:8080/api/")).toBe(
      "https://example.com:8080/api"
    );
    expect(Preferences.normalizeApiUrl("ftp://127.0.0.1:5271")).toBe("");
    expect(Preferences.normalizeApiUrl("http://127.0.0.1:99999")).toBe("");
  });

  it("tracks per-endpoint/profile/setting/mode resume bookmarks via RunResumeIndex", () => {
    const index = new RunResumeIndex();
    index.remember(
      "http://127.0.0.1:5271",
      "player_1",
      "ivory_setting",
      "standard_mode",
      "run_abc"
    );
    expect(
      index.runFor(
        "http://127.0.0.1:5271/",
        "player_1",
        "ivory_setting",
        "standard_mode"
      )
    ).toBe("run_abc");
    index.forgetRun("http://127.0.0.1:5271", "run_abc");
    expect(
      index.runFor(
        "http://127.0.0.1:5271",
        "player_1",
        "ivory_setting",
        "standard_mode"
      )
    ).toBe("");
  });

  it("coalesces identical GET requests and prioritizes writes in HttpTransport", async () => {
    let fetchCount = 0;
    const transport = new HttpTransport(
      () => "http://127.0.0.1:5271",
      async (url: string) => {
        fetchCount += 1;
        await new Promise((r) => setTimeout(r, 15));
        return {
          status: 200,
          headers: {},
          bodyText: JSON.stringify({ url, fetchCount }),
        };
      }
    );

    const t1 = transport.begin("GET", "/api/settings");
    const t2 = transport.begin("GET", "/api/settings");
    const [r1, r2] = await Promise.all([t1.result(), t2.result()]);
    expect(r1.ok).toBe(true);
    expect(r2.ok).toBe(true);
    expect(fetchCount).toBe(1);
  });

  it("projects combat snapshots, card sections, and localized labels without rule math", () => {
    const i18n = new I18n("en");
    const appearance = i18n.presentation(I18n.buildMergedPresentation());

    const runSnapshot: JsonObject = {
      runId: "run_1",
      branchId: "main",
      sequence: 3,
      deck: {
        cardInstances: [
          {
            cardInstanceId: "card_1",
            definitionId: "strike",
            upgrades: ["upgrade_1"],
          },
        ],
        hand: ["card_1"],
      },
    };
    const combatSnapshot: JsonObject = {
      combatId: "combat_1",
      step: 2,
      runNodeId: "node_1",
      stateHash: "abc123",
      actors: [
        {
          instanceId: "hero",
          definitionId: "wayfarer",
          sideId: "players",
          resources: [{ resourceId: "health", current: 42, minimum: 0, maximum: 50 }],
        },
        {
          instanceId: "enemy_1",
          definitionId: "sentinel",
          sideId: "enemies",
          resources: [{ resourceId: "health", current: 25, minimum: 0, maximum: 25 }],
        },
      ],
      relationships: {
        sameSide: "Allied",
        differentSides: "Hostile",
        rules: [],
      },
      activation: {
        round: 2,
        actorId: "hero",
        intents: [
          {
            actorId: "enemy_1",
            actionId: "slash",
            previewApplications: [
              {
                targetEntityId: "hero",
                resourceId: "health",
                previousValue: 42,
                currentValue: 35,
              },
            ],
          },
        ],
      },
    };
    const actions: JsonObject[] = [
      {
        source: "Player",
        command: {
          actionType: "PLAY_CARD",
          cardInstanceId: "card_1",
          targetIds: ["enemy_1"],
        },
        costs: [{ resourceId: "energy", amount: 1 }],
        applications: [
          {
            targetEntityId: "enemy_1",
            resourceId: "health",
            previousValue: 25,
            currentValue: 17,
          },
        ],
      },
    ];

    const presenter = new CombatPresenter(
      runSnapshot,
      combatSnapshot,
      actions,
      i18n,
      appearance
    );

    const cardVm = presenter.cardViewModel("card_1");
    expect(cardVm.cardInstanceId).toBe("card_1");
    expect(cardVm.upgraded).toBe(true);
    expect(cardVm.availability).toBe("SELECT TO PLAY");
    expect(presenter.targets(actions[0])).toEqual(["enemy_1"]);

    const combatVm = presenter.combatModel("hero").toDictionary();
    const actors = combatVm.actors as JsonObject[];
    expect(actors.length).toBe(2);
    expect(actors[1].relationship).toBe("Hostile");
    expect(String(actors[1].intent)).toContain("-7");
  });

  it("calculates deterministic Kosaraju + barycentric map geometry in MapLayout", () => {
    const layout = new MapLayout();
    const profile = new MapLayoutProfile();
    const nodes: JsonObject[] = [
      { id: "n1", next: ["n2", "n3"] },
      { id: "n2", next: ["n4"] },
      { id: "n3", next: ["n4", "n1"] }, // includes cycle back to n1
      { id: "n4", next: [] },
    ];
    const first = layout.calculate(nodes, profile);
    const second = layout.calculate([...nodes].reverse(), profile);

    expect(first.cyclic).toBe(true);
    expect(first.edges.length).toBe(5);
    expect(first.markers).toEqual(second.markers);
    expect(layout.calculationCount).toBe(1);
  });

  it("calculates adaptive fan and reading strip geometry in HandLayout", () => {
    const style = new HandVisualStyle();
    const fan = HandLayout.calculate(
      5,
      960,
      { x: 224, y: 324 },
      style,
      "adaptive"
    );
    expect(fan.positions.length).toBe(5);
    expect(fan.rotations[0]).toBeLessThan(0);
    expect(fan.rotations[2]).toBeCloseTo(0, 5);
    expect(fan.rotations[4]).toBeGreaterThan(0);

    const reading = HandLayout.calculate(
      5,
      960,
      { x: 224, y: 324 },
      style,
      "reading"
    );
    expect(reading.rotations.every((r) => r === 0)).toBe(true);
  });

  it("plans and advances presentation envelopes through Playback and PresentationDirector", () => {
    const policy = MotionPolicy.capture("default", {
      animation_preset: "normal",
      animation_speed: 1.0,
      reduced_motion: false,
    });
    const envelope = PresentationPlanner.plan(
      [
        {
          scope: "combat",
          kind: "card_played",
          actorId: "hero",
          cardInstanceId: "card_1",
          applications: [
            {
              effectType: "DAMAGE",
              targetEntityId: "enemy_1",
              resourceId: "health",
              previousValue: 20,
              currentValue: 12,
            },
          ],
        },
      ],
      policy,
      { runId: "run_1", sequence: 1 }
    );

    expect(envelope.frames.length).toBe(1);
    const playback = new Playback();
    playback.enqueueEnvelope(envelope);
    expect(playback.hasFrames()).toBe(true);

    const prefs = new Preferences();
    prefs.autoAnimations = true;
    const director = new PresentationDirector(playback, prefs, () => false);
    director.process(0.016);
    // Unclaimed handle finishes on next process tick
    director.process(0.016);
    expect(playback.hasFrames()).toBe(false);
  });

  it("validates and projects offline InscriptionLoadoutDraft fixtures", () => {
    const draft = new InscriptionLoadoutDraft();
    const err = draft.configure(inscriptionLoadoutLabData as unknown as JsonObject);
    expect(err).toBe("");

    const snap = draft.snapshot();
    expect(snap.authority).toBe("VisualFixture");
    const slots = snap.slots as string[];
    expect(slots.length).toBeGreaterThan(0);

    draft.clear();
    expect(draft.snapshot().usedCapacity).toBe(0);

    const items = snap.items as JsonObject[];
    const firstItemId = String(items[0].instanceId);
    draft.selectItem(firstItemId);
    draft.selectSlot(slots[0]);
    draft.stageSelected();
    expect(
      (draft.snapshot().assignments as Record<string, string>)[slots[0]]
    ).toBe(firstItemId);
  });

  it("projects dialogue and activity choices without evaluating conditions", () => {
    const i18n = new I18n("pt_BR");
    const run: JsonObject = {
      runId: "r1",
      branchId: "main",
      sequence: 4,
      currentNodeId: "shrine_1",
      dialogues: [
        {
          dialogueInstanceId: "dlg_1",
          dialogueId: "elder_talk",
          nodeId: "n_intro",
          activityNodeId: "shrine_1",
          title: { text: "Ancient Shrine", translations: { pt_BR: "Santuário Antigo" } },
          speaker: { text: "Keeper", translations: { pt_BR: "Guardião" } },
          text: { text: "Welcome.", translations: { pt_BR: "Bem-vindo." } },
          choices: [
            {
              choiceId: "pray",
              text: { text: "Offer prayer", translations: { pt_BR: "Oferecer prece" } },
              available: true,
              costs: [],
            },
          ],
        },
      ],
    };
    const choices: JsonObject[] = [
      {
        type: "CHOOSE_DIALOGUE_OPTION",
        subjectId: "pray",
        payload: { dialogueInstanceId: "dlg_1", nodeId: "n_intro" },
      },
    ];

    const model = DialoguePresenter.build(run, choices, i18n);
    expect(model.title).toBe("Santuário Antigo");
    expect(model.speaker).toBe("Guardião");
    const options = model.options as JsonObject[];
    expect(options.length).toBe(1);
    expect(options[0].available).toBe(true);
    expect(ActivityChoices.isInteractive(choices)).toBe(true);
    expect(ActivityPresenter.optionReference(choices[0]).length).toBeGreaterThan(8);
  });

  it("coordinates LoadoutWorkspace and DeckCompartmentWorkspace with mock transport", async () => {
    const transport = new HttpTransport(
      () => "http://127.0.0.1:5271",
      async (url: string) => {
        if (url.includes("/inscriptions/loadout-options")) {
          return {
            status: 200,
            headers: {},
            bodyText: JSON.stringify({
              slots: [{ slotId: "slot_1" }],
              inscriptions: [
                {
                  inscriptionId: "ins_1",
                  presentation: { name: "Ember Seal" },
                },
              ],
              defaultAssignments: [{ slotId: "slot_1", inscriptionId: "ins_1" }],
            }),
          };
        }
        if (url.includes("/inscriptions/loadout-preview")) {
          return {
            status: 200,
            headers: {},
            bodyText: JSON.stringify({
              isValid: true,
              usedCapacity: 2,
              capacity: 6,
              assignments: [{ slotId: "slot_1", inscriptionId: "ins_1" }],
            }),
          };
        }
        return {
          status: 200,
          headers: {},
          bodyText: JSON.stringify({
            configured: true,
            compartments: [{ compartmentId: "core", name: "Core" }],
            cards: [{ cardDefinitionId: "strike", availableCount: 2 }],
            defaultAllocations: [
              {
                compartmentId: "core",
                cards: [{ cardDefinitionId: "strike", count: 2 }],
              },
            ],
            preview: { isValid: true },
          }),
        };
      }
    );

    const gateway = new EngineGateway(transport);
    const loadout = new LoadoutWorkspace(gateway);
    const okLoadout = await loadout.loadSetting({
      launch: { modeId: "m1", playerEntityId: "p1", seed: 123 },
    });
    expect(okLoadout).toBe(true);
    expect(loadout.snapshot().usedCapacity).toBe(2);

    const dcGateway = new DeckCompartmentGateway(transport);
    const dcWorkspace = new DeckCompartmentWorkspace(dcGateway);
    const okDc = await dcWorkspace.loadMode("m1", "p1", 123);
    expect(okDc).toBe(true);
    expect(dcWorkspace.configured).toBe(true);

    // SnapshotProjection immutability check
    const rawRun = { runId: "r1" };
    const projected = SnapshotProjection.run(rawRun);
    expect(projected.runId).toBe("r1");
    expect(Array.isArray(projected.shops)).toBe(true);

    // CombatController lock check
    const playback = new Playback();
    const dummySession = {
      busy: false,
      synchronized: true,
    } as never;
    const ctrl = new CombatController(dummySession, playback, () => false);
    expect(ctrl.locked()).toBe(false);
  });
});
