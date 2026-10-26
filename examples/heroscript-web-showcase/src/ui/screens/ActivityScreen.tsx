import React, { useEffect, useMemo, useRef, useState } from "react";
import { ActivityChoices } from "../../application/activityChoices";
import { AppServices, ScreenId } from "../../bootstrap";
import { isPlainObject, JsonObject } from "../../contracts/types";
import { ActivityPresenter } from "../../presentation/activityPresenter";
import { CardView } from "../components/CardView";
import { CardDetailsModal, CharacterSidebarView } from "../components/CombatWidgets";
import { ActivityController } from "../controllers/controllers";

export const ActivityScreen: React.FC<{
  services: AppServices;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onNavigate }) => {
  const { session, i18n, audio } = services;
  const [inspectedCard, setInspectedCard] = useState<JsonObject | null>(null);
  const [transformPreview, setTransformPreview] = useState<{
    choice: JsonObject;
    models: JsonObject[];
    text: string;
  } | null>(null);
  const [verificationResult, setVerificationResult] = useState<string>("");

  const controllerRef = useRef<ActivityController | null>(null);
  if (!controllerRef.current) {
    controllerRef.current = new ActivityController(session);
  }
  const controller = controllerRef.current;

  const snapshot = session.captureViewSnapshot();
  const choices = session.activityChoices();
  controller.bind(snapshot.run, choices);

  // Request activity card facts when on an activity node
  useEffect(() => {
    if (!session.hasActiveRun() || session.hasActiveCombat()) return;
    session.ensureActivityCardPresentations();
    session.ensureActivityModifierItems();
  }, [
    session,
    snapshot.run.runId,
    snapshot.run.sequence,
    snapshot.run.currentNodeId,
  ]);

  const presenter = useMemo(
    () =>
      new ActivityPresenter(
        snapshot.run,
        choices,
        services.localizedPresentation(),
        i18n,
        snapshot.activityCardPresentations,
        snapshot.activityModifierItems
      ),
    [snapshot, choices, services, i18n]
  );

  const currentNode = presenter.currentNode();
  const currentNodeId = String(snapshot.run.currentNodeId ?? "");
  const nodeTitle = presenter.nodeName(currentNodeId);
  const nodeDesc = presenter.nodeDescription(currentNode, "");

  const nonTravelChoices = choices.filter(
    (c) => String(c.type ?? "") !== "ADVANCE_NODE"
  );
  const travelChoices = choices.filter(
    (c) => String(c.type ?? "") === "ADVANCE_NODE"
  );

  const offers = useMemo(
    () => nonTravelChoices.map((c) => presenter.offer(c)),
    [nonTravelChoices, presenter]
  );

  const cardOffers = offers.filter((o) => o.category === "cards");
  const actionOffers = offers.filter((o) => o.category !== "cards");

  const handleExecuteChoice = async (choice: JsonObject) => {
    audio.ui();
    const type = String(choice.type ?? "");
    if (ActivityChoices.TRANSFORMATION_TYPES.includes(type as never)) {
      const assessment = await controller.previewTransformation(choice);
      if (Object.keys(assessment).length > 0) {
        setTransformPreview({
          choice,
          models: presenter.transformationModels(assessment),
          text: presenter.transformationText(assessment),
        });
        return;
      }
    }
    const ok = await controller.execute(choice);
    if (ok) {
      if (session.hasActiveCombat()) onNavigate("combat");
      else if (session.hasActiveDialogue()) onNavigate("dialogue");
    }
  };

  const lifecycle = String(snapshot.run.lifecycle ?? snapshot.run.status ?? "Active");
  const isEnded = ["Completed", "Victory", "Defeat", "Abandoned"].includes(
    lifecycle
  );

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      {/* Header */}
      <header className="flex flex-wrap items-center justify-between gap-3 px-6 py-3 bg-[#eee4d1] border-b-2 border-[#20201c]">
        <div>
          <span className="text-[10px] font-display font-semibold uppercase tracking-widest text-[#b83e25]">
            {i18n.text("EXPEDITION STAGE")} · SEQ {Number(snapshot.run.sequence ?? 0)}
          </span>
          <h2 className="font-display text-lg font-bold m-0 text-[#20201c]">
            {nodeTitle || i18n.text("JOURNEY")}
          </h2>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={() => onNavigate("map")}
            className="ivory-button-primary px-3.5 py-1.5 text-xs rounded"
          >
            {i18n.text("OPEN MAP")} ({travelChoices.length})
          </button>
          <button
            type="button"
            onClick={() => onNavigate("loadout")}
            className="ivory-button px-3 py-1.5 text-xs rounded"
          >
            {i18n.text("LOADOUT")}
          </button>
          <button
            type="button"
            onClick={() => onNavigate("timeline")}
            className="ivory-button px-3 py-1.5 text-xs rounded"
          >
            {i18n.text("TIMELINE")}
          </button>
          <button
            type="button"
            onClick={async () => {
              const res = await controller.verify();
              setVerificationResult(
                res.ok
                  ? i18n.text("Deterministic replay verified.")
                  : i18n.error(res)
              );
            }}
            className="ivory-button px-3 py-1.5 text-xs rounded"
          >
            {i18n.text("VERIFY RUN")}
          </button>
          <button
            type="button"
            onClick={() => onNavigate("menu")}
            className="ivory-button px-3 py-1.5 text-xs rounded"
          >
            {i18n.text("MAIN MENU")}
          </button>
        </div>
      </header>

      {verificationResult && (
        <div className="px-6 py-2 bg-[#286853] text-[#faf5eb] text-xs font-semibold flex items-center justify-between">
          <span>{verificationResult}</span>
          <button
            type="button"
            onClick={() => setVerificationResult("")}
            className="underline ml-4"
          >
            {i18n.text("Dismiss")}
          </button>
        </div>
      )}

      {/* Body Grid: Sidebar + Stage Content */}
      <div className="flex-1 overflow-y-auto p-6 grid grid-cols-1 lg:grid-cols-[300px_1fr] gap-6 items-start">
        <CharacterSidebarView run={snapshot.run} translator={i18n} />

        <div className="space-y-6">
          {/* Stage Banner */}
          <div className="ivory-panel rounded-md p-6 space-y-3">
            <div className="flex items-center justify-between">
              <span className="px-2 py-0.5 text-[10px] font-display font-bold uppercase rounded bg-[#20201c] text-[#faf5eb]">
                {String(
                  isPlainObject(currentNode.activity)
                    ? currentNode.activity.type ?? "STAGE"
                    : "STAGE"
                )}
              </span>
              <span className="text-xs font-mono text-[#5b574f]">
                {String(snapshot.run.runId ?? "")}
              </span>
            </div>
            <h3 className="font-display text-2xl font-bold m-0 text-[#20201c]">
              {nodeTitle}
            </h3>
            {nodeDesc && (
              <p className="text-sm text-[#5b574f] m-0 leading-relaxed">
                {nodeDesc}
              </p>
            )}

            {isEnded && (
              <div className="p-4 rounded bg-[#20201c] text-[#faf5eb] space-y-2">
                <div className="font-display text-lg font-bold text-[#f5c26b]">
                  {i18n.text("EXPEDITION CONCLUDED")} — {lifecycle}
                </div>
                <div className="flex gap-3 pt-1">
                  <button
                    type="button"
                    onClick={() => onNavigate("replay")}
                    className="ivory-button-primary px-4 py-1.5 text-xs rounded"
                  >
                    {i18n.text("OPEN REPLAY")}
                  </button>
                  <button
                    type="button"
                    onClick={() => onNavigate("menu")}
                    className="ivory-button px-4 py-1.5 text-xs rounded"
                  >
                    {i18n.text("RETURN TO MENU")}
                  </button>
                </div>
              </div>
            )}
          </div>

          {/* Card Rewards / Shop Card Offers / Upgrade Candidates */}
          {cardOffers.length > 0 && (
            <div className="ivory-panel rounded-md p-5 space-y-4">
              <div className="text-xs font-display font-semibold uppercase tracking-widest text-[#b83e25]">
                {i18n.text("PUBLISHED CARD OFFERS")} ({cardOffers.length})
              </div>
              <div className="flex flex-wrap gap-5 justify-center sm:justify-start">
                {cardOffers.map((offer, idx) => {
                  const choice = isPlainObject(offer.choice)
                    ? offer.choice
                    : {};
                  return (
                    <div
                      key={idx}
                      className="flex flex-col items-center gap-2"
                    >
                      <CardView
                        model={offer}
                        onClick={() => handleExecuteChoice(choice)}
                        onInspect={() => setInspectedCard(offer)}
                      />
                      <button
                        type="button"
                        disabled={session.busy}
                        onClick={() => handleExecuteChoice(choice)}
                        className="w-full ivory-button-primary py-1.5 px-3 text-xs rounded"
                      >
                        {presenter.choiceLabel(choice, offer)}
                      </button>
                    </div>
                  );
                })}
              </div>
            </div>
          )}

          {/* Action / Activity / Relic / Preparation Offers */}
          {actionOffers.length > 0 && (
            <div className="ivory-panel rounded-md p-5 space-y-3">
              <div className="text-xs font-display font-semibold uppercase tracking-widest text-[#5b574f]">
                {i18n.text("STAGE ACTIONS")}
              </div>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                {actionOffers.map((offer, idx) => {
                  const choice = isPlainObject(offer.choice)
                    ? offer.choice
                    : {};
                  const cost = String(offer.cost ?? "");
                  return (
                    <button
                      key={idx}
                      type="button"
                      disabled={session.busy}
                      onClick={() => handleExecuteChoice(choice)}
                      className="ivory-button p-4 rounded text-left flex flex-col justify-between gap-2"
                    >
                      <div className="flex items-center justify-between gap-2 w-full">
                        <span className="font-display font-bold text-sm text-[#b83e25]">
                          {presenter.choiceLabel(choice, offer)}
                        </span>
                        {cost && (
                          <span className="px-2 py-0.5 rounded bg-[#20201c] text-[#faf5eb] font-mono text-xs">
                            {cost}
                          </span>
                        )}
                      </div>
                      <div className="font-body text-xs text-[#5b574f] font-normal">
                        {String(offer.summary ?? "")}
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>
          )}

          {/* Next Route Stops */}
          {travelChoices.length > 0 && (
            <div className="ivory-panel rounded-md p-5 space-y-3">
              <div className="text-xs font-display font-semibold uppercase tracking-widest text-[#286853]">
                {i18n.text("AVAILABLE DESTINATIONS")}
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                {travelChoices.map((choice, idx) => {
                  const offer = presenter.offer(choice);
                  return (
                    <button
                      key={idx}
                      type="button"
                      disabled={session.busy}
                      onClick={async () => {
                        audio.ui();
                        const ok = await controller.execute(choice);
                        if (ok) {
                          if (session.hasActiveCombat()) onNavigate("combat");
                          else if (session.hasActiveDialogue())
                            onNavigate("dialogue");
                        }
                      }}
                      className="ivory-button-primary p-3.5 rounded text-left flex flex-col gap-1"
                    >
                      <span className="font-display text-sm font-bold">
                        {presenter.choiceLabel(choice, offer)}
                      </span>
                      <span className="font-body text-xs opacity-90 font-normal">
                        {String(offer.summary ?? "")}
                      </span>
                    </button>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Before / After Card Transformation Modal */}
      {transformPreview && (
        <div
          className="fixed inset-0 z-50 bg-[#05060b]/75 flex items-center justify-center p-6"
          onClick={() => setTransformPreview(null)}
        >
          <div
            className="ivory-panel max-w-3xl w-full rounded-md overflow-hidden flex flex-col"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="px-5 py-3 bg-[#eee4d1] border-b border-[#8d8270] flex items-center justify-between">
              <h3 className="font-display text-base font-semibold m-0">
                {i18n.text("TRANSFORMATION PREVIEW")}
              </h3>
              <button
                type="button"
                onClick={() => setTransformPreview(null)}
                className="ivory-button px-2.5 py-1 text-xs rounded"
              >
                {i18n.text("Cancel")}
              </button>
            </div>
            <div className="p-6 space-y-4 overflow-y-auto">
              {transformPreview.models.length === 2 ? (
                <div className="flex flex-wrap items-center justify-center gap-8">
                  <div className="text-center space-y-2">
                    <div className="font-display text-xs font-bold uppercase text-[#5b574f]">
                      {i18n.text("BEFORE")}
                    </div>
                    <CardView model={transformPreview.models[0]} />
                  </div>
                  <div className="font-display text-2xl font-bold text-[#b83e25]">
                    →
                  </div>
                  <div className="text-center space-y-2">
                    <div className="font-display text-xs font-bold uppercase text-[#286853]">
                      {i18n.text("AFTER")}
                    </div>
                    <CardView model={transformPreview.models[1]} />
                  </div>
                </div>
              ) : (
                <pre className="p-4 rounded bg-[#20201c] text-[#faf5eb] font-mono text-xs whitespace-pre-wrap">
                  {transformPreview.text}
                </pre>
              )}
              <div className="flex justify-end gap-3 pt-2">
                <button
                  type="button"
                  onClick={() => setTransformPreview(null)}
                  className="ivory-button px-4 py-2 text-xs rounded"
                >
                  {i18n.text("Cancel")}
                </button>
                <button
                  type="button"
                  onClick={async () => {
                    const ch = transformPreview.choice;
                    setTransformPreview(null);
                    audio.reward();
                    await controller.execute(ch);
                  }}
                  className="ivory-button-primary px-5 py-2 text-xs rounded"
                >
                  {i18n.text("CONFIRM TRANSFORMATION")}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {inspectedCard && (
        <CardDetailsModal
          model={inspectedCard}
          translator={i18n}
          onClose={() => setInspectedCard(null)}
        />
      )}
    </div>
  );
};
