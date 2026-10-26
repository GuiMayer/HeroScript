import React, { useEffect, useMemo, useRef, useState } from "react";
import { AppServices, ScreenId } from "../../bootstrap";
import { isPlainObject, JsonObject } from "../../contracts/types";
import { CardZonePresenter } from "../../presentation/cardZonePresenter";
import { CombatPresenter } from "../../presentation/combatPresenter";
import { ActionOptionModel } from "../../presentation/viewModels";
import { ActorPanel, CardDetailsModal, CardZoneDialog } from "../components/CombatWidgets";
import { CardHandView } from "../components/CardHandView";
import { CombatController } from "../controllers/controllers";

export const CombatScreen: React.FC<{
  services: AppServices;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onNavigate }) => {
  const { session, preferences, i18n, playback, director, audio } = services;
  const [, setTick] = useState(0);
  const [hoveredCardId, setHoveredCardId] = useState("");
  const [inspectedCard, setInspectedCard] = useState<{
    model: JsonObject;
    text: string;
  } | null>(null);
  const [openZoneId, setOpenZoneId] = useState<string | null>(null);
  const [optionChoices, setOptionChoices] = useState<{
    cardId: string;
    options: ActionOptionModel[];
  } | null>(null);
  const [zoneToolsOpen, setZoneToolsOpen] = useState(false);
  const [toolFlowIdx, setToolFlowIdx] = useState(0);
  const [toolDefIds, setToolDefIds] = useState("");
  const [toolInstIds, setToolInstIds] = useState("");
  const [toolActorId, setToolActorId] = useState("");
  const [floatingByActor, setFloatingByActor] = useState<
    Record<string, { id: string; text: string; tone: string }[]>
  >({});

  const controllerRef = useRef<CombatController | null>(null);
  if (!controllerRef.current) {
    controllerRef.current = new CombatController(
      session,
      playback,
      () => services.paused
    );
  }
  const controller = controllerRef.current;

  useEffect(() => {
    const unsub = controller.changed.connect(() => setTick((t) => t + 1));
    return () => {
      unsub();
    };
  }, [controller]);

  // Listen to playback frames for floating actor feedback
  useEffect(() => {
    const unsub = playback.framePresented.connect((frame) => {
      const apps = Array.isArray(frame.applications)
        ? (frame.applications as JsonObject[])
        : [];
      if (apps.length === 0) return;
      const nextFloats: Record<
        string,
        { id: string; text: string; tone: string }[]
      > = {};
      apps.forEach((app, idx) => {
        const targetId = String(app.targetEntityId ?? "");
        if (!targetId) return;
        const prev = app.previousValue;
        const curr = app.currentValue;
        let text = "";
        if (prev != null && curr != null) {
          const delta = Number(curr) - Number(prev);
          if (Math.abs(delta) > 1e-6) {
            text = `${delta > 0 ? "+" : ""}${i18n.number(
              delta
            )} ${i18n.contentName(String(app.resourceId ?? ""))}`;
          }
        } else if (app.statusId != null) {
          text = `+ ${i18n.contentName(String(app.statusId))}`;
        }
        if (text) {
          if (!nextFloats[targetId]) nextFloats[targetId] = [];
          nextFloats[targetId].push({
            id: `${Date.now()}-${idx}`,
            text,
            tone: String(app.effectType ?? "fact"),
          });
        }
      });
      if (Object.keys(nextFloats).length > 0) {
        setFloatingByActor(nextFloats);
        setTimeout(() => setFloatingByActor({}), 1200);
      }
    });
    return () => unsub();
  }, [playback, i18n]);

  const snapshot = session.captureViewSnapshot();
  const localizedAppearance = useMemo(
    () => services.localizedPresentation(),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [i18n.locale]
  );

  const presenter = useMemo(() => {
    const p = new CombatPresenter(
      snapshot.run,
      snapshot.combat,
      snapshot.combatActions,
      i18n,
      localizedAppearance,
      snapshot.combatCardZones
    );
    p.acceptPresentations(snapshot.combatCardPresentations);
    return p;
  }, [snapshot, i18n, localizedAppearance]);

  const zonePresenter = useMemo(
    () => new CardZonePresenter(snapshot.combatCardZones, i18n),
    [snapshot.combatCardZones, i18n]
  );

  const playableInstances = useMemo(() => {
    const fromZones = zonePresenter.playableCards();
    if (fromZones.length > 0) return fromZones;
    const deck = isPlainObject(snapshot.run.deck) ? snapshot.run.deck : {};
    const handIds: string[] = Array.isArray(deck.hand) ? deck.hand.map(String) : [];
    return handIds
      .map((id: string) => presenter.cardInstance(id))
      .filter((c: JsonObject) => Object.keys(c).length > 0);
  }, [zonePresenter, snapshot.run, presenter]);

  const handCardIds = useMemo(
    () => playableInstances.map((c: JsonObject) => String(c.cardInstanceId ?? "")),
    [playableInstances]
  );

  controller.bind(presenter, presenter.contextKey(), handCardIds);

  // Request contextual hand evaluation when hand changes
  useEffect(() => {
    if (handCardIds.length === 0 || !session.hasActiveCombat()) return;
    let active = true;
    controller.inspectHand([]).then((res) => {
      if (!active || !Array.isArray(res.cards)) return;
      presenter.acceptEvaluations(res.cards as JsonObject[]);
      setTick((t) => t + 1);
    });
    return () => {
      active = false;
    };
  }, [handCardIds.join(","), presenter, controller, session]);

  const handViewModels = useMemo(
    () => handCardIds.map((id: string) => presenter.cardViewModel(id)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [handCardIds, presenter, snapshot.run.sequence, snapshot.combat.step]
  );

  const viewpoint = session.inputActorId();
  const combatModel = presenter.combatModel(viewpoint).toDictionary();
  const headerModel = presenter
    .headerModel({
      open_timeline: preferences.actionLabel("open_timeline"),
    })
    .toDictionary();
  const hud = controller.interactionModel().toDictionary();

  const actorsList = Array.isArray(combatModel.actors)
    ? (combatModel.actors as JsonObject[])
    : [];
  const friendlyActors = actorsList.filter(
    (a) => String(a.relationship ?? "") !== "Hostile"
  );
  const hostileActors = actorsList.filter(
    (a) => String(a.relationship ?? "") === "Hostile"
  );

  const selectedCardId = String(hud.selected_card ?? "");
  const activeCardId = hoveredCardId || selectedCardId;

  const validTargetsForSelectedCard = useMemo(() => {
    if (!selectedCardId) return new Set<string>();
    const set = new Set<string>();
    for (const cand of presenter.candidates(selectedCardId)) {
      for (const t of presenter.targets(cand)) {
        set.add(t);
      }
    }
    return set;
  }, [selectedCardId, presenter]);

  const handleSelectCard = async (instanceId: string) => {
    audio.selectCard();
    if (selectedCardId === instanceId) {
      controller.cancel();
      return;
    }
    if (!controller.selectCard(instanceId)) return;
    const cands = presenter.candidates(instanceId);
    if (cands.length === 1 && presenter.targets(cands[0]).length === 0) {
      audio.card();
      await controller.submit(cands[0]);
    } else if (
      cands.length > 1 &&
      cands.every((c) => presenter.targets(c).length === 0)
    ) {
      setOptionChoices({
        cardId: instanceId,
        options: presenter.actionOptions(instanceId),
      });
    }
  };

  const handleSelectTarget = async (actorId: string) => {
    const matches = controller.targetOptions(actorId);
    if (matches.length === 0) return;
    if (matches.length === 1) {
      audio.card();
      await controller.submit(matches[0]);
    } else {
      const allOptions = presenter.actionOptions(selectedCardId);
      const filtered = allOptions.filter((opt) =>
        presenter
          .targets(opt.read<JsonObject>("candidate", {}))
          .includes(actorId)
      );
      setOptionChoices({ cardId: selectedCardId, options: filtered });
    }
  };

  const handleInspectCard = async (instanceId: string) => {
    audio.ui();
    const vm = presenter.cardViewModel(instanceId);
    const detail = await controller.inspectCard(instanceId);
    if (Object.keys(detail).length > 0) {
      presenter.acceptEvaluations([detail]);
    }
    setInspectedCard({
      model: presenter.cardViewModel(instanceId) ?? vm,
      text: presenter.inspectionText(instanceId),
    });
  };

  const endTurnCandidate = presenter.systemCandidate([
    "END_TURN",
    "END_ACTIVATION",
    "PASS",
  ]);
  const resolveChoice = session
    .activityChoices()
    .find((c) => String(c.type ?? "") === "RESOLVE_COMBAT");

  const auxZones = zonePresenter.auxiliaryZones();
  const toolFlows = CardZonePresenter.toolFlows(snapshot.run);

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      {/* Top Combat Header */}
      <header className="flex flex-wrap items-center justify-between gap-3 px-5 py-2.5 bg-[#eee4d1] border-b-2 border-[#20201c]">
        <div className="flex items-center gap-4">
          <h2 className="font-display text-lg font-bold m-0 text-[#20201c]">
            {String(headerModel.title ?? "COMBAT")}
          </h2>
          <span className="px-2.5 py-0.5 rounded bg-[#20201c] text-[#faf5eb] font-display text-xs font-semibold">
            {String(headerModel.round ?? "ROUND 1")}
          </span>
          <span className="text-xs font-mono text-[#5b574f] hidden sm:inline">
            {String(headerModel.context ?? "")}
          </span>
        </div>

        <div className="flex items-center gap-2">
          {Boolean(hud.has_frames) && (
            <div className="flex items-center gap-1.5 bg-[#faf5eb] px-2.5 py-1 rounded border border-[#8d8270]">
              <span className="text-xs font-semibold text-[#b83e25]">
                {i18n.text("Frames")}: {Number(hud.remaining_frames ?? 0)}
              </span>
              {!preferences.autoAnimations && (
                <button
                  type="button"
                  onClick={() => director.advance()}
                  className="ivory-button px-2 py-0.5 text-[11px] rounded"
                >
                  {i18n.text("Step")}
                </button>
              )}
              <button
                type="button"
                onClick={() => director.skip()}
                className="ivory-button px-2 py-0.5 text-[11px] rounded"
              >
                {i18n.text("Skip")}
              </button>
            </div>
          )}

          {!hud.synchronized && (
            <button
              type="button"
              onClick={() => controller.reconnect()}
              className="ivory-button-primary px-3 py-1 text-xs rounded"
            >
              {i18n.text("RECONNECT")}
            </button>
          )}

          {Boolean(headerModel.allow_tools) && (
            <button
              type="button"
              onClick={() => setZoneToolsOpen(true)}
              className="ivory-button px-3 py-1 text-xs rounded"
            >
              {String(headerModel.tools ?? "ZONE TOOLS")}
            </button>
          )}

          <button
            type="button"
            onClick={() => onNavigate("timeline")}
            className="ivory-button px-3 py-1 text-xs rounded"
          >
            {String(headerModel.timeline ?? "TIMELINE")}
          </button>

          <button
            type="button"
            onClick={() => onNavigate("journey")}
            className="ivory-button px-3 py-1 text-xs rounded"
          >
            {i18n.text("JOURNEY")}
          </button>
        </div>
      </header>

      {/* Main Battlefield Area */}
      <div className="flex-1 overflow-y-auto px-6 py-4 flex flex-col justify-between gap-4">
        {/* Actors Grid: Friendly vs Hostile */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start">
          {/* Allied Side */}
          <div className="space-y-3">
            <div className="text-xs font-display font-semibold uppercase tracking-widest text-[#286853] border-b border-[#286853]/30 pb-1">
              {i18n.text("EXPEDITION PARTY")}
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              {friendlyActors.map((item, idx) => {
                const actor = isPlainObject(item.actor) ? item.actor : item;
                const id = String(actor.instanceId ?? idx);
                return (
                  <ActorPanel
                    key={id}
                    item={item}
                    validTarget={validTargetsForSelectedCard.has(id)}
                    selectedTarget={String(hud.selected_target ?? "") === id}
                    floatingTexts={floatingByActor[id] ?? []}
                    translator={i18n}
                    onSelectTarget={handleSelectTarget}
                  />
                );
              })}
            </div>
          </div>

          {/* Hostile Side */}
          <div className="space-y-3">
            <div className="text-xs font-display font-semibold uppercase tracking-widest text-[#a4322c] border-b border-[#a4322c]/30 pb-1">
              {i18n.text("OPPOSITION")}
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              {hostileActors.map((item, idx) => {
                const actor = isPlainObject(item.actor) ? item.actor : item;
                const id = String(actor.instanceId ?? idx);
                return (
                  <ActorPanel
                    key={id}
                    item={item}
                    validTarget={validTargetsForSelectedCard.has(id)}
                    selectedTarget={String(hud.selected_target ?? "") === id}
                    floatingTexts={floatingByActor[id] ?? []}
                    translator={i18n}
                    onSelectTarget={handleSelectTarget}
                  />
                );
              })}
            </div>
          </div>
        </div>

        {/* Action Preview & Control Dock */}
        <div className="ivory-panel rounded-md px-4 py-2.5 flex flex-wrap items-center justify-between gap-4">
          <div className="flex-1 min-w-[240px]">
            <div className="text-[10px] font-display font-semibold uppercase tracking-wider text-[#5b574f]">
              {i18n.text("TACTICAL PREVIEW")}
            </div>
            <div className="text-xs font-medium text-[#20201c] whitespace-pre-line">
              {activeCardId
                ? presenter.actionPreview(
                    activeCardId,
                    String(hud.selected_target ?? "")
                  )
                : selectedCardId
                ? i18n.text("Choose a highlighted target.")
                : i18n.text("Select a card from your hand to preview its outcome.")}
            </div>
          </div>

          {/* Auxiliary Piles (Draw / Discard / Exile) */}
          <div className="flex flex-wrap items-center gap-2">
            {auxZones.length > 0 ? (
              auxZones.map((zone) => {
                const zId = String(zone.zoneId ?? "");
                const count = Math.trunc(Number(zone.count ?? 0));
                return (
                  <button
                    key={zId}
                    type="button"
                    onClick={() => setOpenZoneId(zId)}
                    className="ivory-button px-2.5 py-1 text-xs rounded flex items-center gap-1.5"
                  >
                    <span>{zonePresenter.zoneLabel(zone)}</span>
                    <span className="px-1.5 py-0.2 rounded bg-[#20201c] text-[#faf5eb] font-mono text-[11px]">
                      {count}
                    </span>
                  </button>
                );
              })
            ) : (
              <>
                {(["drawPile", "discardPile", "exilePile"] as const).map(
                  (pileKey) => {
                    const cards = presenter.pileCards(pileKey);
                    return (
                      <button
                        key={pileKey}
                        type="button"
                        onClick={() => setOpenZoneId(pileKey)}
                        className="ivory-button px-2.5 py-1 text-xs rounded flex items-center gap-1.5"
                      >
                        <span>{i18n.text(pileKey)}</span>
                        <span className="px-1.5 py-0.2 rounded bg-[#20201c] text-[#faf5eb] font-mono text-[11px]">
                          {cards.length}
                        </span>
                      </button>
                    );
                  }
                )}
              </>
            )}

            {selectedCardId && (
              <button
                type="button"
                onClick={() => {
                  audio.cancel();
                  controller.cancel();
                }}
                className="ivory-button px-3 py-1.5 text-xs rounded text-[#a4322c]"
              >
                {i18n.text("CANCEL")}
              </button>
            )}

            {resolveChoice ? (
              <button
                type="button"
                onClick={async () => {
                  audio.reward();
                  await session.submitActivity(resolveChoice);
                  onNavigate("journey");
                }}
                className="ivory-button-primary px-4 py-1.5 text-xs rounded"
              >
                {i18n.text("CONFIRM RESULT")}
              </button>
            ) : (
              <button
                type="button"
                disabled={
                  Boolean(hud.locked) ||
                  Object.keys(endTurnCandidate).length === 0
                }
                onClick={async () => {
                  audio.turn();
                  await session.submitCandidate(endTurnCandidate);
                }}
                className="ivory-button-primary px-4 py-1.5 text-xs rounded"
              >
                {i18n.text("END TURN")} [{preferences.actionLabel("end_turn")}]
              </button>
            )}
          </div>
        </div>
      </div>

      {/* Bottom Floating Hand Dock */}
      <div className="bg-[#f2ebdd] border-t border-[#8d8270]">
        <CardHandView
          cards={handViewModels}
          selectedId={selectedCardId}
          mode={preferences.handLayoutMode as "adaptive" | "reading"}
          emphasis={preferences.handEmphasis}
          readOnly={Boolean(hud.locked)}
          translator={i18n}
          label={zonePresenter.playableLabel()}
          onSelectCard={handleSelectCard}
          onHoverCard={setHoveredCardId}
          onInspectCard={handleInspectCard}
        />
      </div>

      {/* Alternative Cost / Option Selector Modal */}
      {optionChoices && (
        <div
          className="fixed inset-0 z-50 bg-[#05060b]/75 flex items-center justify-center p-6"
          onClick={() => setOptionChoices(null)}
        >
          <div
            className="ivory-panel max-w-lg w-full rounded-md overflow-hidden"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="px-5 py-3 bg-[#eee4d1] border-b border-[#8d8270] flex items-center justify-between">
              <h3 className="font-display text-base font-semibold m-0">
                {i18n.text("CHOOSE ACTION OPTION")}
              </h3>
              <button
                type="button"
                onClick={() => setOptionChoices(null)}
                className="ivory-button px-2.5 py-1 text-xs rounded"
              >
                {i18n.text("Cancel")}
              </button>
            </div>
            <div className="p-4 space-y-2.5">
              {optionChoices.options.map((opt) => {
                const cand = opt.read<JsonObject>("candidate", {});
                return (
                  <button
                    key={opt.reference}
                    type="button"
                    onClick={async () => {
                      const ref = opt.reference;
                      const cId = optionChoices.cardId;
                      setOptionChoices(null);
                      audio.card();
                      await controller.submitOption(ref, cId);
                    }}
                    className="w-full text-left ivory-button p-3 rounded flex flex-col gap-1"
                  >
                    <div className="font-display text-xs font-bold text-[#b83e25]">
                      {presenter.costText(cand)}
                    </div>
                    <div className="text-xs font-body text-[#20201c]">
                      {presenter.preview(cand)}
                    </div>
                  </button>
                );
              })}
            </div>
          </div>
        </div>
      )}

      {/* Auxiliary Zone Dialog */}
      {openZoneId && (
        <CardZoneDialog
          title={i18n.text(openZoneId)}
          cards={presenter
            .pileCards(openZoneId)
            .map((c) =>
              presenter.cardViewModel(String(c.cardInstanceId ?? ""))
            )}
          translator={i18n}
          onInspectCard={(id) => {
            setOpenZoneId(null);
            handleInspectCard(id);
          }}
          onClose={() => setOpenZoneId(null)}
        />
      )}

      {/* Zone Tools Modal */}
      {zoneToolsOpen && toolFlows.length > 0 && (
        <div
          className="fixed inset-0 z-50 bg-[#05060b]/75 flex items-center justify-center p-6"
          onClick={() => setZoneToolsOpen(false)}
        >
          <div
            className="ivory-panel max-w-md w-full rounded-md overflow-hidden"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="px-5 py-3 bg-[#eee4d1] border-b border-[#8d8270] flex items-center justify-between">
              <h3 className="font-display text-base font-semibold m-0">
                {i18n.text("CARD ZONE TOOLS")}
              </h3>
              <button
                type="button"
                onClick={() => setZoneToolsOpen(false)}
                className="ivory-button px-2.5 py-1 text-xs rounded"
              >
                {i18n.text("Close")}
              </button>
            </div>
            <div className="p-5 space-y-3 text-xs">
              <div>
                <label className="block font-semibold mb-1">Flow</label>
                <select
                  value={toolFlowIdx}
                  onChange={(e) => setToolFlowIdx(Number(e.target.value))}
                  className="w-full p-2 rounded border border-[#8d8270] bg-[#faf5eb]"
                >
                  {toolFlows.map((f, i) => (
                    <option key={String(f.flowId ?? i)} value={i}>
                      {String(f.flowId ?? "")}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block font-semibold mb-1">
                  Card Definition IDs (comma-separated)
                </label>
                <input
                  type="text"
                  value={toolDefIds}
                  onChange={(e) => setToolDefIds(e.target.value)}
                  className="w-full p-2 rounded border border-[#8d8270] bg-[#faf5eb]"
                />
              </div>
              <div>
                <label className="block font-semibold mb-1">
                  Card Instance IDs (comma-separated)
                </label>
                <input
                  type="text"
                  value={toolInstIds}
                  onChange={(e) => setToolInstIds(e.target.value)}
                  className="w-full p-2 rounded border border-[#8d8270] bg-[#faf5eb]"
                />
              </div>
              <div>
                <label className="block font-semibold mb-1">Actor ID</label>
                <input
                  type="text"
                  value={toolActorId}
                  onChange={(e) => setToolActorId(e.target.value)}
                  className="w-full p-2 rounded border border-[#8d8270] bg-[#faf5eb]"
                />
              </div>
              <div className="pt-2 flex justify-end gap-2">
                <button
                  type="button"
                  onClick={async () => {
                    const flow = toolFlows[toolFlowIdx] ?? toolFlows[0];
                    const payload = CardZonePresenter.toolPayload(
                      flow,
                      toolDefIds,
                      toolInstIds,
                      toolActorId
                    );
                    setZoneToolsOpen(false);
                    await controller.executeZoneFlow(payload);
                  }}
                  className="ivory-button-primary px-4 py-2 rounded"
                >
                  {i18n.text("Execute Flow")}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Card Inspection Modal */}
      {inspectedCard && (
        <CardDetailsModal
          model={inspectedCard.model}
          inspectionText={inspectedCard.text}
          translator={i18n}
          onClose={() => setInspectedCard(null)}
        />
      )}
    </div>
  );
};
